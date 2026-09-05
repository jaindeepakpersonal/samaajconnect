using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.ChangeGroupPresident;

/// <summary>
/// Hands a group to a different president.
/// </summary>
/// <remarks>
/// <para>
/// A Samaaj admin's decision, not the outgoing president's - the same split
/// `ChangeGroupStatusCommand` draws, and for the same reason: who runs a group
/// is part of how a Samaaj organises itself, not a call a president makes about
/// their own replacement.
/// </para>
/// <para>
/// <b>`VolunteerGroup.ChangePresident` existed and was called from nowhere.</b>
/// This service's own `CLAUDE.md` lists `GroupPresidentChangedDomainEvent` as
/// "Raised by `VolunteerGroup.ChangePresident`" in its Events published table -
/// stated as built, and never once true, because nothing ever called that
/// method. A group's only president was whoever created it, for the entire
/// life of the group, with no way to change that short of asking the
/// platform's own database to be edited directly.
/// </para>
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsManage)]
public sealed record ChangeGroupPresidentCommand(Guid GroupId, Guid NewPresidentMemberId)
    : ICommand<GroupResponse>;

public sealed class ChangeGroupPresidentCommandValidator
    : AbstractValidator<ChangeGroupPresidentCommand>
{
    public ChangeGroupPresidentCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();

        RuleFor(x => x.NewPresidentMemberId)
            .NotEmpty()
            .WithMessage("Name the new president. A group without one has nobody to decide "
                + "who joins it.");
    }
}

public sealed class ChangeGroupPresidentCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<ChangeGroupPresidentCommand, Result<GroupResponse>>
{
    public async Task<Result<GroupResponse>> Handle(
        ChangeGroupPresidentCommand command,
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

        // A no-op (empty id, or already the president) returns success without
        // raising an event, so the audit log records decisions rather than
        // repeated clicks - the same rule ChangeGroupStatusCommand follows.
        group.ChangePresident(command.NewPresidentMemberId, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(group.ToResponse(actorId));
    }
}
