using FluentValidation;
using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Media;

namespace Sangam.MemberFamily.Application.Media;

// ---- Upload -----------------------------------------------------------------

/// <summary>
/// Stores a member's photo on the platform, replacing whatever was there.
/// </summary>
/// <remarks>
/// Its own command rather than a field on <c>UpdateProfileCommand</c>. Saving
/// text fields and uploading a file are different requests with different
/// shapes - one is JSON, one is multipart - and they were only ever the same
/// field because the photo used to be a URL somebody typed.
///
/// The authorization is deliberately identical to correcting a profile: your
/// own, or <c>Members.Write</c>. A Samaaj administrator can already change a
/// member's name and address, so being unable to fix a photo would be an odd
/// place to draw the line.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record UploadMemberPhotoCommand(Guid MemberId, byte[] Bytes)
    : ICommand<MemberPhotoResponse>;

public sealed class UploadMemberPhotoCommandValidator : AbstractValidator<UploadMemberPhotoCommand>
{
    public UploadMemberPhotoCommandValidator()
    {
        RuleFor(x => x.MemberId).NotEmpty();

        // Two rules rather than one, because "that file is too big" and "that
        // file is not a picture" are different problems and a member can only
        // act on the one they are actually told about.
        RuleFor(x => x.Bytes)
            .NotEmpty()
            .WithMessage("No photo was uploaded.");

        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null || bytes.Length <= ImageContent.MaxBytes)
            .WithMessage(
                $"A photo has to be {ImageContent.MaxBytes / (1024 * 1024)} MB or smaller.");

        // The declared content type is not consulted anywhere: it is a string
        // the uploader chose. This reads the format out of the bytes, and the
        // type served back to viewers is the one derived here.
        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null or { Length: 0 }
                || bytes.Length > ImageContent.MaxBytes
                || ImageContent.IsAcceptable(bytes))
            .WithMessage("A photo has to be a JPEG, PNG or WebP image.");
    }
}

public sealed class UploadMemberPhotoCommandHandler(
    IMemberProfileRepository profiles,
    IImageStore images,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UploadMemberPhotoCommand, Result<MemberPhotoResponse>>
{
    public async Task<Result<MemberPhotoResponse>> Handle(
        UploadMemberPhotoCommand command,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.GetByIdAsync(command.MemberId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<MemberPhotoResponse>(
                Error.NotFound("Member.NotFound", "No such member in this Samaaj."));
        }

        // The IDOR guard (CLAUDE.md §6): the write path re-checks the target's
        // tenant rather than trusting the query filter already did.
        if (tenantContext.TenantId is { } tenantId && profile.TenantId != tenantId)
        {
            return Result.Failure<MemberPhotoResponse>(
                Error.NotFound("Member.NotFound", "No such member in this Samaaj."));
        }

        var isSelf = currentUser.UserId == profile.Id;

        if (!isSelf && !currentUser.HasPermission(PermissionKeys.MembersWrite))
        {
            return Result.Failure<MemberPhotoResponse>(Error.Forbidden(
                "Member.NotYours", "You can only change your own photo."));
        }

        var actor = currentUser.UserId ?? profile.Id;

        var image = StoredImage.Capture(
            profile.TenantId,
            ImageOwnerKind.Member,
            profile.Id,
            command.Bytes,
            actor,
            clock.UtcNow);

        images.Add(image);

        // The previous image goes in the same transaction. Replacing a photo has
        // to leave one photo: a failure between the two writes that kept both
        // would leave a picture of somebody with nothing pointing at it, which
        // no later path would ever clean up.
        var replaced = profile.SetPhoto(image.Id, clock.UtcNow, actor);

        if (replaced is { } previousId)
        {
            await images.RemoveAsync(previousId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new MemberPhotoResponse(
            profile.Id, image.ContentType, image.ByteSize, image.UploadedAt));
    }
}

// ---- Remove -----------------------------------------------------------------

/// <summary>Takes a member's photo down. Idempotent.</summary>
/// <remarks>
/// Removing a photo that is not there is success and changes nothing. A member
/// who clicks twice, or a client that retries, has not done anything wrong -
/// the same reasoning that makes closing a Boli and publishing a result
/// idempotent elsewhere on this platform.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record RemoveMemberPhotoCommand(Guid MemberId) : ICommand<Unit>;

public sealed class RemoveMemberPhotoCommandValidator : AbstractValidator<RemoveMemberPhotoCommand>
{
    public RemoveMemberPhotoCommandValidator() => RuleFor(x => x.MemberId).NotEmpty();
}

public sealed class RemoveMemberPhotoCommandHandler(
    IMemberProfileRepository profiles,
    IImageStore images,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RemoveMemberPhotoCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        RemoveMemberPhotoCommand command,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.GetByIdAsync(command.MemberId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<Unit>(
                Error.NotFound("Member.NotFound", "No such member in this Samaaj."));
        }

        if (tenantContext.TenantId is { } tenantId && profile.TenantId != tenantId)
        {
            return Result.Failure<Unit>(
                Error.NotFound("Member.NotFound", "No such member in this Samaaj."));
        }

        var isSelf = currentUser.UserId == profile.Id;

        if (!isSelf && !currentUser.HasPermission(PermissionKeys.MembersWrite))
        {
            return Result.Failure<Unit>(Error.Forbidden(
                "Member.NotYours", "You can only change your own photo."));
        }

        var removed = profile.RemovePhoto(clock.UtcNow, currentUser.UserId ?? profile.Id);

        if (removed is { } imageId)
        {
            await images.RemoveAsync(imageId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(Unit.Value);
    }
}

/// <summary>What an upload answers with. Never the bytes it was just sent.</summary>
public sealed record MemberPhotoResponse(
    Guid MemberId,
    string ContentType,
    int ByteSize,
    DateTimeOffset UploadedAt);
