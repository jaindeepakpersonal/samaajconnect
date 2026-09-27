using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Trusts;

namespace Sangam.Trust.Application.Trusts.Roles;

// A Trust's roles - Trustee built in, and the rest its own (President,
// Secretary, Treasurer...), each with the seats an election for it fills. The
// shape volunteer-groups-service gives a group's volunteer roles, copied
// rather than shared.

/// <summary>One role, with how many hold it now.</summary>
public sealed record TrustRoleResponse(
    Guid Id,
    string Title,
    string Kind,

    /// <summary>Null for Trustee, which has no limit.</summary>
    int? Seats,
    int Holders,

    /// <summary>Whether it can be renamed, resized, removed and elected to. False for Trustee.</summary>
    bool IsElected);

internal static class TrustRoleMappings
{
    public static TrustRoleResponse ToResponse(this TrustRole role, int holders) =>
        new(role.Id, role.Title, role.Kind.ToString(), role.Seats, holders, role.IsElected);

    public static Result<T> Refused<T>(TrustRoleRefusal refusal) => Result.Failure<T>(refusal switch
    {
        TrustRoleRefusal.NotFound => Error.NotFound("TrustRole.NotFound", "No such role on this Trust."),
        TrustRoleRefusal.DuplicateTitle => Error.Conflict(
            "TrustRole.DuplicateTitle", "This Trust already has a role by that name."),
        TrustRoleRefusal.NotEditable => Error.Conflict(
            "TrustRole.NotEditable", "Trustee is built in: it cannot be renamed, resized or removed."),
        TrustRoleRefusal.SeatsBelowHolders => Error.Conflict(
            "TrustRole.SeatsBelowHolders", "More trustees hold this role than that many seats. Move some first."),
        TrustRoleRefusal.HasHolders => Error.Conflict(
            "TrustRole.HasHolders", "Somebody holds this role. Move them to another before removing it."),
        TrustRoleRefusal.RoleFull => Error.Conflict(
            "TrustRole.Full", "Every seat of this role is taken."),
        TrustRoleRefusal.NotElected => Error.Conflict(
            "TrustRole.NotElected", "Nobody is elected to Trustee - every trustee holds it until they hold another role."),
        _ => Error.Conflict("TrustRole.Refused", "That change to the Trust's roles was refused."),
    });
}

public sealed class TrustRoleInputValidator<T> : AbstractValidator<T>
    where T : ITrustRoleInput
{
    public TrustRoleInputValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Seats).InclusiveBetween(1, 50);
    }
}

public interface ITrustRoleInput
{
    string Title { get; }

    int Seats { get; }
}

// ---- List ---------------------------------------------------------------------

[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListTrustRolesQuery(Guid TrustId) : IQuery<IReadOnlyList<TrustRoleResponse>>;

public sealed class ListTrustRolesQueryHandler(ITrustRepository trusts, ITrustMemberRepository members)
    : IRequestHandler<ListTrustRolesQuery, Result<IReadOnlyList<TrustRoleResponse>>>
{
    public async Task<Result<IReadOnlyList<TrustRoleResponse>>> Handle(
        ListTrustRolesQuery query, CancellationToken cancellationToken)
    {
        var trust = await trusts.GetByIdAsync(query.TrustId, cancellationToken);

        if (trust is null)
        {
            return Result.Failure<IReadOnlyList<TrustRoleResponse>>(
                Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."));
        }

        var holders = await members.CountActiveByRoleAsync(trust.Id, cancellationToken);

        // Trustee first, then the Trust's own roles in the order they were defined.
        return Result.Success<IReadOnlyList<TrustRoleResponse>>(
            [.. trust.Roles
                .OrderBy(r => r.IsElected ? 1 : 0)
                .ThenBy(r => r.CreatedAt)
                .Select(r => r.ToResponse(holders.GetValueOrDefault(r.Id)))]);
    }
}

// ---- Define ---------------------------------------------------------------------

[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record DefineTrustRoleCommand(Guid TrustId, string Title, int Seats)
    : ICommand<TrustRoleResponse>, ITrustRoleInput;

public sealed class DefineTrustRoleCommandValidator : AbstractValidator<DefineTrustRoleCommand>
{
    public DefineTrustRoleCommandValidator()
    {
        RuleFor(x => x.TrustId).NotEmpty();
        Include(new TrustRoleInputValidator<DefineTrustRoleCommand>());
    }
}

public sealed class DefineTrustRoleCommandHandler(
    ITrustRepository trusts,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<DefineTrustRoleCommand, Result<TrustRoleResponse>>
{
    public async Task<Result<TrustRoleResponse>> Handle(DefineTrustRoleCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<TrustRoleResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var trust = await trusts.GetByIdAsync(command.TrustId, cancellationToken);

        if (trust is null)
        {
            return Result.Failure<TrustRoleResponse>(Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."));
        }

        var refusal = trust.DefineRole(command.Title, command.Seats, actorId, clock.UtcNow, out var role);

        if (refusal != TrustRoleRefusal.None)
        {
            return TrustRoleMappings.Refused<TrustRoleResponse>(refusal);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(role!.ToResponse(holders: 0));
    }
}

// ---- Update ---------------------------------------------------------------------

[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record UpdateTrustRoleCommand(Guid TrustId, Guid RoleId, string Title, int Seats)
    : ICommand<TrustRoleResponse>, ITrustRoleInput;

public sealed class UpdateTrustRoleCommandValidator : AbstractValidator<UpdateTrustRoleCommand>
{
    public UpdateTrustRoleCommandValidator()
    {
        RuleFor(x => x.TrustId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
        Include(new TrustRoleInputValidator<UpdateTrustRoleCommand>());
    }
}

public sealed class UpdateTrustRoleCommandHandler(
    ITrustRepository trusts,
    ITrustMemberRepository members,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateTrustRoleCommand, Result<TrustRoleResponse>>
{
    public async Task<Result<TrustRoleResponse>> Handle(UpdateTrustRoleCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<TrustRoleResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var trust = await trusts.GetByIdAsync(command.TrustId, cancellationToken);

        if (trust is null)
        {
            return Result.Failure<TrustRoleResponse>(Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."));
        }

        var holders = await members.CountActiveHoldersAsync(trust.Id, command.RoleId, cancellationToken);
        var refusal = trust.UpdateRole(command.RoleId, command.Title, command.Seats, holders, actorId, clock.UtcNow);

        if (refusal != TrustRoleRefusal.None)
        {
            return TrustRoleMappings.Refused<TrustRoleResponse>(refusal);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(trust.FindRole(command.RoleId)!.ToResponse(holders));
    }
}

// ---- Remove ---------------------------------------------------------------------

/// <summary>Refused while anybody holds it, and while an election for it is still unpublished.</summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record RemoveTrustRoleCommand(Guid TrustId, Guid RoleId) : ICommand<Guid>;

public sealed class RemoveTrustRoleCommandValidator : AbstractValidator<RemoveTrustRoleCommand>
{
    public RemoveTrustRoleCommandValidator()
    {
        RuleFor(x => x.TrustId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class RemoveTrustRoleCommandHandler(
    ITrustRepository trusts,
    ITrustMemberRepository members,
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RemoveTrustRoleCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(RemoveTrustRoleCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<Guid>(Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var trust = await trusts.GetByIdAsync(command.TrustId, cancellationToken);

        if (trust is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."));
        }

        if (await elections.AnyUnpublishedForRoleAsync(command.RoleId, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "TrustRole.BeingElected", "An election for this role is under way. Publish it before removing the role."));
        }

        var holders = await members.CountActiveHoldersAsync(trust.Id, command.RoleId, cancellationToken);
        var refusal = trust.RemoveRole(command.RoleId, holders, actorId, clock.UtcNow);

        if (refusal != TrustRoleRefusal.None)
        {
            return TrustRoleMappings.Refused<Guid>(refusal);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(command.RoleId);
    }
}
