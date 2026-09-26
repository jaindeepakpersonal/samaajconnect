using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.UpdateGroup;

/// <summary>
/// Corrects a group's name, description or focus area.
/// </summary>
/// <remarks>
/// A Samaaj admin's decision, same as <see cref="CreateGroup.CreateGroupCommand"/>
/// - a group's own name and description are part of how the Samaaj organises
/// itself, not something its president edits. The president, membership and
/// status each have their own command already; this touches none of them.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsManage)]
public sealed record UpdateGroupCommand(
    Guid GroupId,
    string Name,
    string? Description,
    string? FocusArea,

    /// <summary>
    /// The locality the group serves; null for none. An Area Admin cannot move
    /// a group out of their own locality - omitted, it stays there.
    /// </summary>
    string? LocalityId = null) : ICommand<GroupResponse>;

public sealed class UpdateGroupCommandValidator : AbstractValidator<UpdateGroupCommand>
{
    public UpdateGroupCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.FocusArea).MaximumLength(100);

        // A string on the wire, parsed only in the handler, so a malformed id
        // fails here rather than in the JSON binder.
        RuleFor(x => x.LocalityId)
            .Must(value => string.IsNullOrWhiteSpace(value) || Guid.TryParse(value, out _))
            .WithMessage("Locality is not valid.");
    }
}

public sealed class UpdateGroupCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateGroupCommand, Result<GroupResponse>>
{
    public async Task<Result<GroupResponse>> Handle(
        UpdateGroupCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<GroupResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var group = await groups.GetByIdAsync(command.GroupId, cancellationToken);

        if (group is null
            || (tenantContext.TenantId is { } tenantId && group.TenantId != tenantId)
            || !areaScope.MayAdminister(group.LocalityId))
        {
            return Result.Failure<GroupResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        var localityId = string.IsNullOrWhiteSpace(command.LocalityId)
            ? areaScope.AreaLocalityId
            : Guid.Parse(command.LocalityId);

        if (!areaScope.MayAdminister(localityId))
        {
            return Result.Failure<GroupResponse>(Error.Forbidden(
                "Group.OutsideLocality", "You cannot move a group out of your own locality."));
        }

        var name = command.Name.Trim();

        if (await groups.NameExistsAsync(name, group.Id, cancellationToken))
        {
            return Result.Failure<GroupResponse>(Error.Conflict(
                "Group.NameTaken", "This Samaaj already has a group with that name."));
        }

        group.Update(name, command.Description, command.FocusArea, clock.UtcNow, localityId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(group.ToResponse(actorId, currentUser.Administers(areaScope, group)));
    }
}
