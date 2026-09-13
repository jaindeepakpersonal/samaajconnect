using Sangam.VolunteerGroups.Domain.Elections;

namespace Sangam.VolunteerGroups.Application.Elections;

/// <summary>Domain to wire. No decisions here beyond shape.</summary>
internal static class ElectionMappings
{
    public static ElectionResponse ToResponse(
        this GroupElection election, DateTimeOffset now, Guid? myVoteCandidateId) =>
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
            myVoteCandidateId,
            election.Candidates.Count,
            election.WinnerMemberId,
            election.PublishedAt,
            election.CreatedAt);

    public static ElectionDetailResponse ToDetail(
        this GroupElection election,
        DateTimeOffset now,
        Guid? myVoteCandidateId,
        IReadOnlyDictionary<Guid, int> tally) =>
        new(election.ToResponse(now, myVoteCandidateId),
            [.. election.Candidates.Select(c => new ElectionCandidateResponse(
                c.Id, c.MemberId, c.Status.ToString(), c.NominatedBy, tally.GetValueOrDefault(c.Id)))]);
}
