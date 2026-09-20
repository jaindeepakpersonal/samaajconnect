using Sangam.Trust.Domain.Elections;

namespace Sangam.Trust.Application.Elections;

/// <summary>Domain to wire. No decisions here beyond shape.</summary>
internal static class TrustElectionMappings
{
    public static TrustElectionResponse ToResponse(
        this TrustElection election, DateTimeOffset now, Guid? myVoteCandidateId) =>
        new(election.Id,
            election.TrustId,
            election.PositionTitle,
            election.NominationStartAt,
            election.NominationEndAt,
            election.VotingStartAt,
            election.VotingEndAt,
            election.Status.ToString(),
            election.AcceptsNominations(now),
            election.AcceptsVotes(now),
            myVoteCandidateId,
            election.Candidates.Count,
            election.WinnerMemberId,
            election.PublishedAt,
            election.CreatedAt);

    public static TrustElectionDetailResponse ToDetail(
        this TrustElection election,
        DateTimeOffset now,
        Guid? myVoteCandidateId,
        IReadOnlyDictionary<Guid, int> tally) =>
        new(election.ToResponse(now, myVoteCandidateId),
            [.. election.Candidates.Select(c => new TrustElectionCandidateResponse(
                c.Id, c.MemberId, c.Status.ToString(), c.NominatedBy, tally.GetValueOrDefault(c.Id)))]);
}
