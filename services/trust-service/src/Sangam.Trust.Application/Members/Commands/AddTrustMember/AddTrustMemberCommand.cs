using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Members;

namespace Sangam.Trust.Application.Members.Commands.AddTrustMember;

/// <summary>
/// Adds a member to a Trust's roster. Adding somebody already active is a
/// no-op reported as success; adding somebody previously removed reactivates
/// their old row (see <see cref="TrustMember.Reactivate"/>).
/// </summary>
/// <remarks>
/// <paramref name="MemberId"/> is a string, not a <see cref="Guid"/> - a
/// Guid-typed wire field crashes ASP.NET Core's own JSON model binding with an
/// unhandled 400 the moment the value cannot be parsed, before FluentValidation
/// ever runs (the same defense-in-depth panch-service's
/// <c>NominateCandidateCommand</c> uses for a body-bound id).
/// </remarks>
[RequiresPermission(PermissionKeys.TrustManage)]
/// <remarks>
/// <paramref name="RoleId"/> is optional: a new trustee holds Trustee unless
/// given another role with a free seat.
/// </remarks>
public sealed record AddTrustMemberCommand(Guid TrustId, string MemberId, string? RoleId = null)
    : ICommand<TrustMemberResponse>;

public sealed class AddTrustMemberCommandValidator : AbstractValidator<AddTrustMemberCommand>
{
    public AddTrustMemberCommandValidator()
    {
        RuleFor(x => x.TrustId).NotEmpty();
        RuleFor(x => x.MemberId).Must(BeAGuid).WithMessage("Choose a member to add.");
        RuleFor(x => x.RoleId)
            .Must(id => id is null || Guid.TryParse(id, out _))
            .WithMessage("Choose a role this Trust has.");
    }

    private static bool BeAGuid(string value) => Guid.TryParse(value, out _);
}

public sealed class AddTrustMemberCommandHandler(
    ITrustRepository trusts,
    ITrustMemberRepository members,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<AddTrustMemberCommand, Result<TrustMemberResponse>>
{
    public async Task<Result<TrustMemberResponse>> Handle(
        AddTrustMemberCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<TrustMemberResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var trust = await trusts.GetByIdAsync(command.TrustId, cancellationToken);

        if (trust is null)
        {
            return Result.Failure<TrustMemberResponse>(
                Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."));
        }

        // Guaranteed parseable by AddTrustMemberCommandValidator.BeAGuid.
        var memberId = Guid.Parse(command.MemberId);

        var role = command.RoleId is { } requested ? trust.FindRole(Guid.Parse(requested)) : trust.TrusteeRole;

        if (role is null)
        {
            return Result.Failure<TrustMemberResponse>(
                Error.NotFound("TrustRole.NotFound", "No such role on this Trust."));
        }

        var existing = await members.FindAsync(trust.Id, memberId, cancellationToken);

        if (existing is not { Status: TrustMemberStatus.Active }
            && role.Seats is { } seats
            && await members.CountActiveHoldersAsync(trust.Id, role.Id, cancellationToken) >= seats)
        {
            return TrustRoleMappingsAccess.Full<TrustMemberResponse>();
        }

        if (existing is null)
        {
            var member = Domain.Members.TrustMember.Create(
                trust.TenantId, trust.Id, memberId, role.Id, actorId, clock.UtcNow);

            members.Add(member);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(member.ToResponse(trust));
        }

        if (existing.Status == TrustMemberStatus.Active)
        {
            return Result.Success(existing.ToResponse(trust));
        }

        existing.Reactivate(role.Id, actorId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(existing.ToResponse(trust));
    }
}

/// <summary>The one role refusal the roster commands share with the role commands.</summary>
internal static class TrustRoleMappingsAccess
{
    public static Result<T> Full<T>() =>
        Result.Failure<T>(Error.Conflict("TrustRole.Full", "Every seat of this role is taken."));
}
