using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Elections;

namespace Sangam.Trust.Application.Elections.Commands.OpenTrustElection;

/// <summary>
/// Sets an election up for one position on one Trust's roster. It stays a
/// Draft until an admin opens nominations.
/// </summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record OpenTrustElectionCommand(
    Guid TrustId,
    string PositionTitle,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt) : ICommand<TrustElectionResponse>;

public sealed class OpenTrustElectionCommandValidator : AbstractValidator<OpenTrustElectionCommand>
{
    public OpenTrustElectionCommandValidator()
    {
        RuleFor(x => x.TrustId).NotEmpty();
        RuleFor(x => x.PositionTitle).NotEmpty().MaximumLength(100);

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

        var now = clock.UtcNow;

        var election = TrustElection.Open(
            trust.TenantId,
            trust.Id,
            command.PositionTitle,
            command.NominationStartAt,
            command.NominationEndAt,
            command.VotingStartAt,
            command.VotingEndAt,
            now);

        elections.Add(election);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(election.ToResponse(now, myVoteCandidateId: null));
    }
}
