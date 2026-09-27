using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Elections;

namespace Sangam.Trust.Application.Elections.Commands.OpenTrustElection;

/// <summary>
/// Sets an election up for one of a Trust's roles. It stays a Draft until an
/// admin opens nominations, and fills as many seats as the role has now.
/// </summary>
/// <remarks>
/// Refused for Trustee, which is not elected, and while another election for
/// the same role is still unpublished - two would each claim the same seats.
/// </remarks>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record OpenTrustElectionCommand(
    Guid TrustId,
    Guid RoleId,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt) : ICommand<TrustElectionResponse>;

public sealed class OpenTrustElectionCommandValidator : AbstractValidator<OpenTrustElectionCommand>
{
    public OpenTrustElectionCommandValidator()
    {
        RuleFor(x => x.TrustId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();

        RuleFor(x => x.NominationEndAt)
            .GreaterThan(x => x.NominationStartAt)
            .WithMessage("Nominations cannot close before they open.");

        RuleFor(x => x.VotingEndAt)
            .GreaterThan(x => x.VotingStartAt)
            .WithMessage("Voting cannot close before it opens.");

        // Voting on a ballot still being nominated to means early and late
        // voters see different ballots - the same rule
        // volunteer-groups-service's own OpenElectionCommandValidator already
        // enforces.
        RuleFor(x => x.VotingStartAt)
            .GreaterThanOrEqualTo(x => x.NominationEndAt)
            .WithMessage("Voting must start after nominations close.");
    }
}

public sealed class OpenTrustElectionCommandHandler(
    ITrustRepository trusts,
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock)
    : IRequestHandler<OpenTrustElectionCommand, Result<TrustElectionResponse>>
{
    public async Task<Result<TrustElectionResponse>> Handle(
        OpenTrustElectionCommand command, CancellationToken cancellationToken)
    {
        var trust = await trusts.GetByIdAsync(command.TrustId, cancellationToken);

        if (trust is null)
        {
            return Result.Failure<TrustElectionResponse>(
                Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."));
        }

        var role = trust.FindRole(command.RoleId);

        if (role is null)
        {
            return Result.Failure<TrustElectionResponse>(
                Error.NotFound("TrustRole.NotFound", "No such role on this Trust."));
        }

        if (!role.IsElected || role.Seats is not { } seats)
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "TrustRole.NotElected",
                "Nobody is elected to Trustee - every trustee holds it until they hold another role."));
        }

        if (await elections.AnyUnpublishedForRoleAsync(role.Id, cancellationToken))
        {
            return Result.Failure<TrustElectionResponse>(Error.Conflict(
                "Election.AlreadyRunning",
                "An election for this role is already under way. Publish it before setting up another."));
        }

        var now = clock.UtcNow;

        var election = TrustElection.Open(
            trust.TenantId,
            trust.Id,
            role.Id,
            seats,
            command.NominationStartAt,
            command.NominationEndAt,
            command.VotingStartAt,
            command.VotingEndAt,
            now);

        elections.Add(election);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(election.ToResponse(trust, now, []));
    }
}
