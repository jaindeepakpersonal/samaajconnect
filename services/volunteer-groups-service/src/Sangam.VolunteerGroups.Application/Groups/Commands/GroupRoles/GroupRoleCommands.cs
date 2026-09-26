using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Application.Groups.Commands.GroupRoles;

// A group's volunteer roles, and who holds them. All four commands are the
// president's or an administrator's of this group (GroupAuthority
// .CanRunElections), refused to anyone else as "not found" - the same answer
// every other leader-only command here gives, so a stranger learns nothing
// about the group's roles or who holds them.

// ---- Define a role -----------------------------------------------------------

/// <summary>Adds a role - Secretary, Coordinator - with the seats an election for it fills.</summary>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record DefineGroupRoleCommand(Guid GroupId, string Title, int Seats)
    : ICommand<GroupDetailResponse>;

public sealed class DefineGroupRoleCommandValidator : AbstractValidator<DefineGroupRoleCommand>
{
    public DefineGroupRoleCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Seats).InclusiveBetween(1, 100);
    }
}

public sealed class DefineGroupRoleCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<DefineGroupRoleCommand, Result<GroupDetailResponse>>
{
    public async Task<Result<GroupDetailResponse>> Handle(
        DefineGroupRoleCommand command, CancellationToken cancellationToken)
    {
        var found = await GroupRoleAccess.LoadAsync(
            groups, currentUser, areaScope, tenantContext, command.GroupId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<GroupDetailResponse>(found.Error);
        }

        var (group, actorId, isAdmin) = found.Value;

        var refusal = group.DefineRole(command.Title, command.Seats, actorId, clock.UtcNow, out _);

        return await GroupRoleAccess.SaveAsync(group, refusal, actorId, isAdmin, unitOfWork, cancellationToken);
    }
}

// ---- Rename or resize a role -------------------------------------------------

/// <summary>
/// Renames a role or changes its seats. Neither built-in role, and never below
/// how many hold it now. An election already set up keeps the seats it was
/// opened with.
/// </summary>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record UpdateGroupRoleCommand(Guid GroupId, Guid RoleId, string Title, int Seats)
    : ICommand<GroupDetailResponse>;

public sealed class UpdateGroupRoleCommandValidator : AbstractValidator<UpdateGroupRoleCommand>
{
    public UpdateGroupRoleCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Seats).InclusiveBetween(1, 100);
    }
}

public sealed class UpdateGroupRoleCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateGroupRoleCommand, Result<GroupDetailResponse>>
{
    public async Task<Result<GroupDetailResponse>> Handle(
        UpdateGroupRoleCommand command, CancellationToken cancellationToken)
    {
        var found = await GroupRoleAccess.LoadAsync(
            groups, currentUser, areaScope, tenantContext, command.GroupId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<GroupDetailResponse>(found.Error);
        }

        var (group, actorId, isAdmin) = found.Value;

        var refusal = group.UpdateRole(command.RoleId, command.Title, command.Seats, actorId, clock.UtcNow);

        return await GroupRoleAccess.SaveAsync(group, refusal, actorId, isAdmin, unitOfWork, cancellationToken);
    }
}

// ---- Remove a role -----------------------------------------------------------

/// <summary>
/// Removes a role nobody holds and no election is still deciding. Past
/// elections for it keep their own record of its title.
/// </summary>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record RemoveGroupRoleCommand(Guid GroupId, Guid RoleId) : ICommand<GroupDetailResponse>;

public sealed class RemoveGroupRoleCommandValidator : AbstractValidator<RemoveGroupRoleCommand>
{
    public RemoveGroupRoleCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class RemoveGroupRoleCommandHandler(
    IGroupRepository groups,
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RemoveGroupRoleCommand, Result<GroupDetailResponse>>
{
    public async Task<Result<GroupDetailResponse>> Handle(
        RemoveGroupRoleCommand command, CancellationToken cancellationToken)
    {
        var found = await GroupRoleAccess.LoadAsync(
            groups, currentUser, areaScope, tenantContext, command.GroupId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<GroupDetailResponse>(found.Error);
        }

        var (group, actorId, isAdmin) = found.Value;

        // An election still running for it would have nowhere to install its
        // winners.
        if (group.FindRole(command.RoleId) is not null
            && await elections.HasUnpublishedForRoleAsync(group.Id, command.RoleId, null, cancellationToken))
        {
            return Result.Failure<GroupDetailResponse>(Error.Conflict(
                "Role.ElectionRunning",
                "An election for this role is still running. Publish it before removing the role."));
        }

        var refusal = group.RemoveRole(command.RoleId, actorId, clock.UtcNow);

        return await GroupRoleAccess.SaveAsync(group, refusal, actorId, isAdmin, unitOfWork, cancellationToken);
    }
}

// ---- Assign a role -----------------------------------------------------------

/// <summary>
/// Moves a member into a role directly - the manual override beside an
/// election, and how somebody is put back to Volunteer. Not the President
/// role, in either direction: that moves only with the presidency.
/// </summary>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record AssignGroupRoleCommand(Guid GroupId, Guid MemberId, Guid RoleId)
    : ICommand<GroupDetailResponse>;

public sealed class AssignGroupRoleCommandValidator : AbstractValidator<AssignGroupRoleCommand>
{
    public AssignGroupRoleCommandValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class AssignGroupRoleCommandHandler(
    IGroupRepository groups,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<AssignGroupRoleCommand, Result<GroupDetailResponse>>
{
    public async Task<Result<GroupDetailResponse>> Handle(
        AssignGroupRoleCommand command, CancellationToken cancellationToken)
    {
        var found = await GroupRoleAccess.LoadAsync(
            groups, currentUser, areaScope, tenantContext, command.GroupId, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<GroupDetailResponse>(found.Error);
        }

        var (group, actorId, isAdmin) = found.Value;

        var refusal = group.AssignRole(command.MemberId, command.RoleId, clock.UtcNow);

        return await GroupRoleAccess.SaveAsync(group, refusal, actorId, isAdmin, unitOfWork, cancellationToken);
    }
}

// ---- Shared ------------------------------------------------------------------

internal static class GroupRoleAccess
{
    public static async Task<Result<(VolunteerGroup Group, Guid ActorId, bool IsAdmin)>> LoadAsync(
        IGroupRepository groups,
        ICurrentUser currentUser,
        IAreaScope areaScope,
        ITenantContext tenantContext,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<(VolunteerGroup, Guid, bool)>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var group = await groups.GetByIdAsync(groupId, cancellationToken);

        if (group is null || (tenantContext.TenantId is { } tenantId && group.TenantId != tenantId))
        {
            return Result.Failure<(VolunteerGroup, Guid, bool)>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        var isAdmin = currentUser.Administers(areaScope, group);

        if (!group.CanRunElections(actorId, isAdmin))
        {
            return Result.Failure<(VolunteerGroup, Guid, bool)>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        return Result.Success((group, actorId, isAdmin));
    }

    public static async Task<Result<GroupDetailResponse>> SaveAsync(
        VolunteerGroup group,
        RoleRefusal refusal,
        Guid actorId,
        bool isAdmin,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (refusal != RoleRefusal.None)
        {
            return Result.Failure<GroupDetailResponse>(ToError(refusal));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(group.ToDetail(actorId, isAdmin));
    }

    private static Error ToError(RoleRefusal refusal) => refusal switch
    {
        RoleRefusal.NotFound => Error.NotFound("Role.NotFound", "This group has no such role."),
        RoleRefusal.NotAMember => Error.NotFound("Group.NotAMember", "That member is not in this group."),
        RoleRefusal.DuplicateTitle => Error.Conflict(
            "Role.DuplicateTitle", "This group already has a role with that title."),
        RoleRefusal.NotEditable => Error.Conflict(
            "Role.BuiltIn", "Volunteer and President are built into every group and cannot be changed or removed."),
        RoleRefusal.SeatsBelowHolders => Error.Conflict(
            "Role.SeatsBelowHolders",
            "More members hold this role than that. Move some to another role first."),
        RoleRefusal.HasHolders => Error.Conflict(
            "Role.HasHolders", "Members still hold this role. Move them to another role first."),
        RoleRefusal.RoleFull => Error.Conflict(
            "Role.Full", "Every seat in this role is taken. Move somebody out of it first."),
        RoleRefusal.PresidencyIsSeparate => Error.Conflict(
            "Role.PresidencyIsSeparate",
            "The President role moves only with the presidency - change the president, or run a President election."),
        _ => Error.Failure("Role.Refused", "That change could not be made."),
    };
}
