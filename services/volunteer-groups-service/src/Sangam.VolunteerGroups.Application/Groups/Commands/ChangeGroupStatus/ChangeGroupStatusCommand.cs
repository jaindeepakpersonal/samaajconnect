using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.ChangeGroupStatus;

/// <summary>
/// Activates or deactivates a group.
/// </summary>
/// <remarks>
/// A Samaaj admin's decision rather than the president's: winding a group up is
/// about how the Samaaj is organised, and a president deactivating their own
/// group would take its members' history with it on one person's say-so.
///
/// A deactivated group is still visible and keeps its members - it simply takes
/// no new applications. Deleting it would erase the record of who volunteered
/// for what, which is the part worth keeping.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsManage)]
public sealed record ChangeGroupStatusCommand(Guid GroupId, string Status)
    : ICommand<GroupResponse>;

public sealed class ChangeGroupStatusCommandValidator : AbstractValidator<ChangeGroupStatusCommand>
{
    public ChangeGroupStatusCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => Enum.TryParse<GroupStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Active or Inactive.");
    }
}

public sealed class ChangeGroupStatusCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<ChangeGroupStatusCommand, Result<GroupResponse>>
{
    public async Task<Result<GroupResponse>> Handle(
        ChangeGroupStatusCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<GroupResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var group = await groups.GetByIdAsync(command.GroupId, cancellationToken);

        if (group is null
            || (tenantContext.TenantId is { } tenantId && group.TenantId != tenantId))
        {
            return Result.Failure<GroupResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        // A no-op returns success without raising an event, so the audit log
        // records decisions rather than repeated clicks.
        group.ChangeStatus(Enum.Parse<GroupStatus>(command.Status, ignoreCase: true), clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(group.ToResponse(actorId));
    }
}
