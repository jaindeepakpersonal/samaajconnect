using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Media;
using DomainGroupPhoto = Sangam.VolunteerGroups.Domain.Media.GroupPhoto;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.GroupPhoto;

// Setting and removing a photo is editing the group, so it is gated exactly
// like UpdateGroupCommand: VolunteerGroups.Manage, and an Area Admin only for
// a group in their own locality. Anyone else is told the group does not exist,
// the same answer a different Samaaj's group id gets.

// ---- Upload ------------------------------------------------------------------

/// <summary>Stores a group's photo - its page banner - replacing whatever was there.</summary>
[RequiresPermission(PermissionKeys.VolunteerGroupsManage)]
public sealed record UploadGroupPhotoCommand(Guid GroupId, byte[] Bytes) : ICommand<GroupPhotoResponse>;

public sealed class UploadGroupPhotoCommandValidator : AbstractValidator<UploadGroupPhotoCommand>
{
    public UploadGroupPhotoCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();

        // Three rules rather than one: "nothing arrived", "too big" and "not a
        // picture" are different problems, and an administrator can only act on
        // the one they are told about.
        RuleFor(x => x.Bytes)
            .NotEmpty()
            .WithMessage("No photo was uploaded.");

        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null || bytes.Length <= ImageContent.MaxBytes)
            .WithMessage($"A photo has to be {ImageContent.MaxBytes / (1024 * 1024)} MB or smaller.");

        // The declared content type is never consulted - it is a string the
        // uploader chose. The format is read from the bytes.
        RuleFor(x => x.Bytes)
            .Must(bytes => bytes is null or { Length: 0 }
                || bytes.Length > ImageContent.MaxBytes
                || ImageContent.IsAcceptable(bytes))
            .WithMessage("A photo has to be a JPEG, PNG or WebP image.");
    }
}

public sealed class UploadGroupPhotoCommandHandler(
    IGroupRepository groups,
    IGroupPhotoStore photos,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UploadGroupPhotoCommand, Result<GroupPhotoResponse>>
{
    public async Task<Result<GroupPhotoResponse>> Handle(
        UploadGroupPhotoCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<GroupPhotoResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var group = await groups.GetByIdAsync(command.GroupId, cancellationToken);

        // The write re-checks the tenant itself rather than trusting the query
        // filter alone (root CLAUDE.md section 6).
        if (group is null
            || (tenantContext.TenantId is { } tenantId && group.TenantId != tenantId)
            || !areaScope.MayAdminister(group.LocalityId))
        {
            return Result.Failure<GroupPhotoResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        var now = clock.UtcNow;
        var photo = DomainGroupPhoto.Capture(group.TenantId, group.Id, command.Bytes, actorId, now);

        photos.Add(photo);

        // The previous photo goes in the same transaction, so replacing always
        // leaves exactly one.
        if (group.SetPhoto(photo.Id, now) is { } previousId)
        {
            await photos.RemoveAsync(previousId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new GroupPhotoResponse(group.Id, photo.ContentType, photo.ByteSize, photo.UploadedAt));
    }
}

// ---- Remove ------------------------------------------------------------------

/// <summary>Takes a group's photo down. Doing it twice is success.</summary>
[RequiresPermission(PermissionKeys.VolunteerGroupsManage)]
public sealed record RemoveGroupPhotoCommand(Guid GroupId) : ICommand<Unit>;

public sealed class RemoveGroupPhotoCommandValidator : AbstractValidator<RemoveGroupPhotoCommand>
{
    public RemoveGroupPhotoCommandValidator() => RuleFor(x => x.GroupId).NotEmpty();
}

public sealed class RemoveGroupPhotoCommandHandler(
    IGroupRepository groups,
    IGroupPhotoStore photos,
    IUnitOfWork unitOfWork,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RemoveGroupPhotoCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        RemoveGroupPhotoCommand command,
        CancellationToken cancellationToken)
    {
        var group = await groups.GetByIdAsync(command.GroupId, cancellationToken);

        if (group is null
            || (tenantContext.TenantId is { } tenantId && group.TenantId != tenantId)
            || !areaScope.MayAdminister(group.LocalityId))
        {
            return Result.Failure<Unit>(Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        if (group.RemovePhoto(clock.UtcNow) is { } photoId)
        {
            await photos.RemoveAsync(photoId, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(Unit.Value);
    }
}

// ---- Read --------------------------------------------------------------------

/// <summary>
/// A group's photo bytes - for any signed-in member of the Samaaj, the same
/// people who can open the group's page (<c>GetGroupQuery</c>).
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetGroupPhotoQuery(Guid GroupId) : IQuery<GroupPhotoContent>;

public sealed class GetGroupPhotoQueryValidator : AbstractValidator<GetGroupPhotoQuery>
{
    public GetGroupPhotoQueryValidator() => RuleFor(x => x.GroupId).NotEmpty();
}

public sealed class GetGroupPhotoQueryHandler(IGroupRepository groups, IGroupPhotoStore photos)
    : IRequestHandler<GetGroupPhotoQuery, Result<GroupPhotoContent>>
{
    public async Task<Result<GroupPhotoContent>> Handle(
        GetGroupPhotoQuery query,
        CancellationToken cancellationToken)
    {
        var group = await groups.GetByIdAsync(query.GroupId, cancellationToken);

        // No such group and no photo answer the same way: the difference is of
        // no use to a screen, which only ever asks for a photo the group's own
        // response said it has.
        var photo = group?.PhotoId is { } photoId ? await photos.GetAsync(photoId, cancellationToken) : null;

        return photo is null
            ? Result.Failure<GroupPhotoContent>(Error.NotFound("Group.NoPhoto", "This group has no photo."))
            : Result.Success(new GroupPhotoContent(photo.Bytes, photo.ContentType, photo.ContentHash, photo.UploadedAt));
    }
}

/// <summary>What an upload answers with. Never the bytes it was just sent.</summary>
public sealed record GroupPhotoResponse(Guid GroupId, string ContentType, int ByteSize, DateTimeOffset UploadedAt);

/// <summary>The bytes to serve, with what a browser needs to cache them.</summary>
public sealed record GroupPhotoContent(byte[] Bytes, string ContentType, string ETag, DateTimeOffset UploadedAt);
