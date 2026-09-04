using FluentValidation;
using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Media;

namespace Sangam.MemberFamily.Application.Media;

/// <summary>
/// Stores a child's photo, replacing whatever was there.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the endpoint DPDP s.9(3) was actually about.</b> A child's photo
/// used to be a URL somebody typed, which meant every viewer of that child's
/// record — their family, and the Pathshala teaching them — fetched the picture
/// from whatever host it named, telling that host that a child's photograph had
/// just been looked at and from which address. The platform holds the bytes
/// now, and no third party is told anything.
/// </para>
/// <para>
/// The permission is the household's, not the Samaaj's. Unlike a member photo,
/// <c>Members.Write</c> does not open this: an administrator correcting a
/// member's own details is administrative work, and a child's photograph is
/// not. That matches <c>DecideJoinRequestCommand</c>, which stays with the head
/// even for administrators, and the reasoning is the same — who is in a
/// household, and what their children look like, is not administration.
/// </para>
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record UploadChildPhotoCommand(Guid ChildId, byte[] Bytes)
    : ICommand<ChildPhotoResponse>;

public sealed class UploadChildPhotoCommandValidator : AbstractValidator<UploadChildPhotoCommand>
{
    public UploadChildPhotoCommandValidator()
    {
        RuleFor(x => x.ChildId).NotEmpty();

        RuleFor(x => x.Bytes)
            .NotEmpty()
            .WithMessage("No photo was uploaded.");

        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null || bytes.Length <= ImageContent.MaxBytes)
            .WithMessage(
                $"A photo has to be {ImageContent.MaxBytes / (1024 * 1024)} MB or smaller.");

        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null or { Length: 0 }
                || bytes.Length > ImageContent.MaxBytes
                || ImageContent.IsAcceptable(bytes))
            .WithMessage("A photo has to be a JPEG, PNG or WebP image.");
    }
}

public sealed class UploadChildPhotoCommandHandler(
    IChildRepository children,
    IFamilyRepository families,
    IImageStore images,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UploadChildPhotoCommand, Result<ChildPhotoResponse>>
{
    public async Task<Result<ChildPhotoResponse>> Handle(
        UploadChildPhotoCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<ChildPhotoResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var child = await children.GetByIdAsync(command.ChildId, cancellationToken);
        var family = await families.GetForMemberAsync(memberId, cancellationToken);

        // Not their household is "no such child", not 403. A 403 would confirm
        // the id names a real child in some other family.
        if (child is null || family is null || child.FamilyId != family.Id)
        {
            return Result.Failure<ChildPhotoResponse>(
                Error.NotFound("Child.NotFound", "No such child in your family."));
        }

        // The IDOR guard (CLAUDE.md §6), independent of the query filter.
        if (tenantContext.TenantId is { } tenantId && child.TenantId != tenantId)
        {
            return Result.Failure<ChildPhotoResponse>(
                Error.NotFound("Child.NotFound", "No such child in your family."));
        }

        var image = StoredImage.Capture(
            child.TenantId,
            ImageOwnerKind.Child,
            child.Id,
            command.Bytes,
            memberId,
            clock.UtcNow);

        images.Add(image);

        var replaced = child.SetPhoto(image.Id);

        if (replaced is { } previousId)
        {
            await images.RemoveAsync(previousId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ChildPhotoResponse(
            child.Id, image.ContentType, image.ByteSize, image.UploadedAt));
    }
}

/// <summary>Takes a child's photo down. Idempotent.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record RemoveChildPhotoCommand(Guid ChildId) : ICommand<Unit>;

public sealed class RemoveChildPhotoCommandValidator : AbstractValidator<RemoveChildPhotoCommand>
{
    public RemoveChildPhotoCommandValidator() => RuleFor(x => x.ChildId).NotEmpty();
}

public sealed class RemoveChildPhotoCommandHandler(
    IChildRepository children,
    IFamilyRepository families,
    IImageStore images,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext)
    : IRequestHandler<RemoveChildPhotoCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        RemoveChildPhotoCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<Unit>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var child = await children.GetByIdAsync(command.ChildId, cancellationToken);
        var family = await families.GetForMemberAsync(memberId, cancellationToken);

        if (child is null || family is null || child.FamilyId != family.Id)
        {
            return Result.Failure<Unit>(
                Error.NotFound("Child.NotFound", "No such child in your family."));
        }

        if (tenantContext.TenantId is { } tenantId && child.TenantId != tenantId)
        {
            return Result.Failure<Unit>(
                Error.NotFound("Child.NotFound", "No such child in your family."));
        }

        var removed = child.RemovePhoto();

        if (removed is { } imageId)
        {
            await images.RemoveAsync(imageId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(Unit.Value);
    }
}

/// <summary>What an upload answers with. Never the bytes it was just sent.</summary>
public sealed record ChildPhotoResponse(
    Guid ChildId,
    string ContentType,
    int ByteSize,
    DateTimeOffset UploadedAt);
