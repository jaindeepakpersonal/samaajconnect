using Sangam.VolunteerGroups.Domain.Elections;

namespace Sangam.VolunteerGroups.Application.Elections;

/// <summary>Domain to wire. No decisions here beyond shape.</summary>
internal static class ElectionMappings
{
    public static ElectionResponse ToResponse(
        this GroupElection election, DateTimeOffset now, IReadOnlyList<Guid> myVoteCandidateIds) =>
        new(election.Id,
            election.GroupId,
            election.PositionTitle,
            election.NominationStartAt,
            election.NominationEndAt,
            election.VotingStartAt,
            election.VotingEndAt,
            election.Status.ToString(),
            election.AcceptsNominations(now),
            election.AcceptsVotes(now),
            myVoteCandidateIds,
            election.Candidates.Count,
            election.WinnerMemberIds,
            election.PublishedAt,
            election.CreatedAt,
            election.RoleId,
            election.Seats);

    public static ElectionDetailResponse ToDetail(
        this GroupElection election,
        DateTimeOffset now,
        IReadOnlyList<Guid> myVoteCandidateIds,
        IReadOnlyDictionary<Guid, int> tally) =>
        new(election.ToResponse(now, myVoteCandidateIds),
            [.. election.Candidates.Select(c => new ElectionCandidateResponse(
                c.Id, c.MemberId, c.Status.ToString(), c.NominatedBy, tally.GetValueOrDefault(c.Id)))]);
}
