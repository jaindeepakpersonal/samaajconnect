using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.RemoveGroupMember;

/// <summary>
/// Removes a member from the group. The president's decision, the same way
/// accepting them in was.
/// </summary>
/// <remarks>
/// <para>
/// <b>`VolunteerGroup.RemoveMember` existed and was called from nowhere.</b>
/// It has its own doc comment explaining that the president cannot be removed
/// and that replacing one is a Samaaj admin's decision via
/// `ChangePresident` - and `GroupPresidentChangedDomainEvent` has sat in this
/// service's own `CLAUDE.md`, in its "Raised by" column, naming a method that
/// nothing ever called either. A president could accept an application and
/// give somebody a position; there was no way to undo either.
/// </para>
/// <para>
/// Removing does not erase the application that let them in. "Were they ever
/// accepted?" stays answerable, the same reasoning `VolunteerGroup`'s own class
/// doc gives for keeping membership and applications as two separate lists.
/// </para>
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record RemoveGroupMemberCommand(Guid GroupId, Guid MemberId)
    : ICommand<GroupDetailResponse>;

public sealed class RemoveGroupMemberCommandValidator : AbstractValidator<RemoveGroupMemberCommand>
{
    public RemoveGroupMemberCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
    }
}

public sealed class RemoveGroupMemberCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RemoveGroupMemberCommand, Result<GroupDetailResponse>>
{
    public async Task<Result<GroupDetailResponse>> Handle(
        RemoveGroupMemberCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<GroupDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var group = await groups.GetByIdAsync(command.GroupId, cancellationToken);

        if (group is null
            || (tenantContext.TenantId is { } tenantId && group.TenantId != tenantId))
        {
            return Result.Failure<GroupDetailResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        // "Not found", for the same reason as deciding an application or
        // assigning a position: a non-president learns nothing about who is in
        // this group.
        if (!group.IsPresident(actorId))
        {
            return Result.Failure<GroupDetailResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        // Checked ahead of the domain call so the two reasons `RemoveMember`
        // can refuse - "is the president" and "is not a member" - get the
        // distinct messages they deserve rather than one that fits neither
        // well.
        if (group.IsPresident(command.MemberId))
        {
            return Result.Failure<GroupDetailResponse>(Error.Conflict(
                "Group.CannotRemovePresident",
                "The president cannot be removed from their own group. Name a different "
                + "president first."));
        }

        if (!group.RemoveMember(command.MemberId, actorId, clock.UtcNow))
        {
            return Result.Failure<GroupDetailResponse>(Error.NotFound(
                "Group.NotAMember", "That member is not in this group."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(group.ToDetail(actorId));
    }
}
