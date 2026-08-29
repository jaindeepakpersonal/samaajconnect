namespace Sangam.CelebrityVoting.Application.Campaigns;

/// <summary>A campaign as the list and detail screens show it.</summary>
public sealed record CampaignResponse(
    Guid Id,
    string Title,
    string? Description,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt,
    int TopN,
    string ResultsVisibility,
    string Status,

    /// <summary>Whether nominations are open <i>right now</i>: status and clock both.</summary>
    bool AcceptsNominations,
    bool AcceptsVotes,

    /// <summary>The candidate this member voted for, if they have voted.</summary>
    Guid? MyVoteCandidateId,
    int CandidateCount,
    DateTimeOffset CreatedAt);

/// <summary>
/// One name on the ballot.
/// </summary>
/// <remarks>
/// <paramref name="Votes"/> is null when the tally is not visible to this
/// caller — a campaign set to HiddenUntilClose, still running, seen by a
/// member. Null rather than zero: zero is a claim, and the wrong one.
/// </remarks>
public sealed record CandidateResponse(
    Guid Id,
    Guid MemberId,
    string? Category,
    string Status,
    Guid NominatedBy,
    int? Votes);

public sealed record CampaignDetailResponse(
    CampaignResponse Campaign,
    IReadOnlyList<CandidateResponse> Candidates,

    /// <summary>
    /// False when this caller is being shown a ballot without counts. The
    /// screen needs to know the difference between "no votes yet" and "you may
    /// not see the votes".
    /// </summary>
    bool TallyVisible);

/// <summary>
/// The published result, in order, with the counts that produced it.
/// </summary>
/// <remarks>
/// The order comes from the stored <c>CampaignResult</c> rather than from a
/// fresh tally. A result recomputed on every read could change after it was
/// announced, and an announced result that moves is worse than none.
/// </remarks>
public sealed record CampaignResultResponse(
    Guid CampaignId,
    IReadOnlyList<ResultEntry> Ranking,
    Guid PublishedBy,
    DateTimeOffset PublishedAt);

public sealed record ResultEntry(int Rank, Guid CandidateId, Guid MemberId, int Votes);
