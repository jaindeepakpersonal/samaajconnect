using Sangam.Panch.Domain.Elections;

namespace Sangam.Panch.Application.Elections;

/// <summary>
/// The one place an election becomes a response, so there is one place to check
/// that a hidden tally stays hidden.
/// </summary>
internal static class ElectionMappings
{
    public static ElectionResponse ToResponse(
        this PanchElection election, DateTimeOffset now, IReadOnlyList<Guid>? myChoices) => new(
        election.Id,
        election.Title,
        election.Description,
        election.NominationStartAt,
        election.NominationEndAt,
        election.VotingStartAt,
        election.VotingEndAt,
        PanchElection.SeatCount,
        election.ResultsVisibility.ToString(),
        election.Status.ToString(),
        election.AcceptsNominations(now),
        election.AcceptsVotes(now),
        myChoices,
        election.Candidates.Count,
        election.CreatedAt);

    /// <summary>
    /// The ballot, with counts only when this caller may see them.
    /// </summary>
    /// <remarks>
    /// <paramref name="tally"/> is passed in rather than fetched here, so a
    /// caller who may not see it never causes the query at all. Fetching and then
    /// discarding would work and would be one refactor away from leaking.
    /// </remarks>
    public static ElectionDetailResponse ToDetail(
        this PanchElection election,
        DateTimeOffset now,
        IReadOnlyList<Guid>? myChoices,
        bool canAdminister,
        IReadOnlyDictionary<Guid, int>? tally)
    {
        var tallyVisible = election.TallyVisibleTo(canAdminister) && tally is not null;

        // An administrator sees every nomination, including ones nobody has
        // approved: approving them is their job. A member sees the ballot.
        var candidates = canAdminister
            ? election.Candidates.OrderBy(c => c.NominatedAt).ToList()
            : [.. election.Ballot];

        return new ElectionDetailResponse(
            election.ToResponse(now, myChoices),
            [
                .. candidates.Select(c => new CandidateResponse(
                    c.Id,
                    c.MemberId,
                    c.Status.ToString(),
                    c.NominatedBy,

                    // Null, not zero: zero is a claim, and the wrong one.
                    tallyVisible ? tally!.GetValueOrDefault(c.Id) : null))
            ],
            tallyVisible);
    }
}
