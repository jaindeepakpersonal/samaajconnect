using Sangam.Trust.Domain.Elections;

namespace Sangam.Trust.Application.Elections;

/// <summary>Domain to wire. No decisions here beyond shape.</summary>
/// <remarks>
/// The Trust supplies the role's title - the election holds only its id, so a
/// renamed role reads the same on every election for it. A role since removed
/// reads as the empty string rather than failing the whole list.
/// </remarks>
internal static class TrustElectionMappings
{
    public static TrustElectionResponse ToResponse(
        this TrustElection election,
        Domain.Trusts.Trust trust,
        DateTimeOffset now,
        IReadOnlyList<Guid> myVoteCandidateIds) =>
        new(election.Id,
            election.TrustId,
            trust.FindRole(election.RoleId)?.Title ?? string.Empty,
            election.NominationStartAt,
            election.NominationEndAt,
            election.VotingStartAt,
            election.VotingEndAt,
            election.Status.ToString(),
            election.AcceptsNominations(now),
            election.AcceptsVotes(now),
            myVoteCandidateIds,
            election.Candidates.Count,
            [.. election.WinnerMemberIds],
            election.PublishedAt,
            election.CreatedAt,
            election.RoleId,
            election.Seats);

    public static TrustElectionDetailResponse ToDetail(
        this TrustElection election,
        Domain.Trusts.Trust trust,
        DateTimeOffset now,
        IReadOnlyList<Guid> myVoteCandidateIds,
        IReadOnlyDictionary<Guid, int> tally) =>
        new(election.ToResponse(trust, now, myVoteCandidateIds),
            [.. election.Candidates.Select(c => new TrustElectionCandidateResponse(
                c.Id, c.MemberId, c.Status.ToString(), c.NominatedBy, tally.GetValueOrDefault(c.Id)))]);
}
