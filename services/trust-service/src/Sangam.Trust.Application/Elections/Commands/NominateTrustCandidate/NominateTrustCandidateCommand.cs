using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Members;

namespace Sangam.Trust.Application.Elections.Commands.NominateTrustCandidate;

/// <summary>
/// A Trust member puts another Trust member forward for the position this
/// election is running.
/// </summary>
/// <remarks>
/// <paramref name="MemberId"/> is a string, not a <see cref="Guid"/> - a
/// Guid-typed wire field crashes ASP.NET Core's own JSON model binding with
/// an unhandled 400 the moment it cannot be parsed, before FluentValidation
/// ever runs - the same defense-in-depth this repository already applies to
/// every body-bound id (panch-service's <c>NominateCandidateCommand</c>,
/// this service's own <c>AddTrustMemberCommand</c>).
///
/// The electorate is this Trust's own active roster, not the whole Samaaj:
/// both the nominator and the nominee must be an active
/// <see cref="TrustMember"/> of the election's own Trust, checked against
/// that separate aggregate rather than anything held on the election itself -
/// volunteer-groups-service's own "electorate is the group, not the Samaaj"
/// rule, applied here.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record NominateTrustCandidateCommand(Guid ElectionId, string MemberId)
    : ICommand<NominateTrustCandidateResponse>;

/// <summary>
/// <paramref name="Nominated"/> is false when this member was already put
/// forward - reported as success, since the second nominator has done
/// nothing wrong and one candidacy per member is what keeps a vote from
/// splitting.
/// </summary>
public sealed record NominateTrustCandidateResponse(
    Guid ElectionId, Guid CandidateId, Guid MemberId, bool Nominated);

public sealed class NominateTrustCandidateCommandValidator
    : AbstractValidator<NominateTrustCandidateCommand>
{
    public NominateTrustCandidateCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();
        RuleFor(x => x.MemberId).Must(BeAGuid).WithMessage("Choose a member to nominate.");
    }

    private static bool BeAGuid(string value) => Guid.TryParse(value, out _);
}

public sealed class NominateTrustCandidateCommandHandler(
    IElectionRepository elections,
    ITrustMemberRepository members,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<NominateTrustCandidateCommand, Result<NominateTrustCandidateResponse>>
{
    public async Task<Result<NominateTrustCandidateResponse>> Handle(
        NominateTrustCandidateCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } nominatorId)
        {
            return Result.Failure<NominateTrustCandidateResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null)
        {
            return Result.Failure<NominateTrustCandidateResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var nominator = await members.FindAsync(election.TrustId, nominatorId, cancellationToken);

        // The nominator must be an active member of the Trust this election
        // is for - "everybody on the roster" is the electorate, not every
        // Samaaj member.
        if (nominator is null || nominator.Status != TrustMemberStatus.Active)
        {
            return Result.Failure<NominateTrustCandidateResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        // Guaranteed parseable by NominateTrustCandidateCommandValidator.BeAGuid.
        var memberId = Guid.Parse(command.MemberId);

        var nominee = await members.FindAsync(election.TrustId, memberId, cancellationToken);

        if (nominee is null || nominee.Status != TrustMemberStatus.Active)
        {
            return Result.Failure<NominateTrustCandidateResponse>(Error.Conflict(
                "Election.NotAMember", "Only an active member of this Trust can be nominated."));
        }

        var now = clock.UtcNow;

        if (!election.AcceptsNominations(now))
        {
            return Result.Failure<NominateTrustCandidateResponse>(Error.Conflict(
                "Election.NominationsClosed", "Nominations for this election are not open."));
        }

        var candidate = election.Nominate(memberId, nominatorId, now);

        if (candidate is null)
        {
            // Already put forward. The candidacy that stands is the one to
            // report back, not an empty id.
            var existing = election.FindCandidateForMember(memberId);

            return Result.Success(new NominateTrustCandidateResponse(
                election.Id, existing?.Id ?? Guid.Empty, memberId, false));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new NominateTrustCandidateResponse(election.Id, candidate.Id, memberId, true));
    }
}
