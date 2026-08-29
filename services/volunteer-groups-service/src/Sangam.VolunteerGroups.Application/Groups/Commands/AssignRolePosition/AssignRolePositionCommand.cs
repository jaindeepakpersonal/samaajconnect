using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.AssignRolePosition;

/// <summary>
/// Gives a member a position inside the group - Secretary, Coordinator - or
/// clears it by sending nothing.
/// </summary>
/// <remarks>
/// Free text, and deliberately not a platform role. What someone is called
/// inside a Seva group grants nothing anywhere, and should not need a
/// deployment to add; the roles in AuthorizationCatalog are what actually gate
/// and are a closed list for exactly that reason.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record AssignRolePositionCommand(Guid GroupId, Guid MemberId, string? RolePosition)
    : ICommand<GroupDetailResponse>;

public sealed class AssignRolePositionCommandValidator
    : AbstractValidator<AssignRolePositionCommand>
{
    public AssignRolePositionCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.RolePosition).MaximumLength(100);
    }
}

public sealed class AssignRolePositionCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<AssignRolePositionCommand, Result<GroupDetailResponse>>
{
    public async Task<Result<GroupDetailResponse>> Handle(
        AssignRolePositionCommand command,
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

        // "Not found", for the same reason as deciding an application: a
        // non-president learns nothing about who is in this group or what
        // positions it has.
        if (!group.IsPresident(actorId))
        {
            return Result.Failure<GroupDetailResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        if (!group.AssignRolePosition(command.MemberId, command.RolePosition, clock.UtcNow))
        {
            return Result.Failure<GroupDetailResponse>(Error.NotFound(
                "Group.NotAMember", "That member is not in this group."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(group.ToDetail(actorId));
    }
}
