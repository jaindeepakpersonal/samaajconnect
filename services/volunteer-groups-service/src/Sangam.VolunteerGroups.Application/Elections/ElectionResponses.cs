namespace Sangam.VolunteerGroups.Application.Elections;

/// <summary>An election as the list and detail screens show it.</summary>
public sealed record ElectionResponse(
    Guid Id,
    Guid GroupId,
    string PositionTitle,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt,
    string Status,

    /// <summary>Whether nominations are open <i>right now</i>: status and clock both.</summary>
    bool AcceptsNominations,
    bool AcceptsVotes,

    /// <summary>The candidate this member voted for, if they have voted.</summary>
    Guid? MyVoteCandidateId,
    int CandidateCount,

    /// <summary>Set once the election is Published. Null until then.</summary>
    Guid? WinnerMemberId,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// One name on the ballot. Unlike celebrity-voting-service's own
/// <c>CandidateResponse</c>, the vote count is never hidden - a group
/// election has no equivalent of a "results visibility" setting, so there is
/// nothing to gate it on.
/// </summary>
public sealed record ElectionCandidateResponse(
    Guid Id, Guid MemberId, string Status, Guid NominatedBy, int Votes);

public sealed record ElectionDetailResponse(
    ElectionResponse Election, IReadOnlyList<ElectionCandidateResponse> Candidates);
