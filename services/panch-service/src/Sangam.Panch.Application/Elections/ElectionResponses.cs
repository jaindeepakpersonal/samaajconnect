namespace Sangam.Panch.Application.Elections;

/// <summary>An election as the list and detail screens show it.</summary>
public sealed record ElectionResponse(
    Guid Id,
    string Title,
    string? Description,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt,

    /// <summary>The Panch is this many members, and a voter may back up to this many.</summary>
    int Seats,
    string ResultsVisibility,
    string Status,

    /// <summary>Whether nominations are open <i>right now</i>: status and clock both.</summary>
    bool AcceptsNominations,
    bool AcceptsVotes,

    /// <summary>The candidates this member backed, or null if they have not voted.</summary>
    IReadOnlyList<Guid>? MyChoices,
    int CandidateCount,
    DateTimeOffset CreatedAt);

/// <summary>
/// One name on the ballot.
/// </summary>
/// <remarks>
/// <paramref name="Votes"/> is null when the tally is not visible to this
/// caller - an election set to HiddenUntilClose, still running, seen by a
/// member. Null rather than zero: zero is a claim, and the wrong one.
/// </remarks>
public sealed record CandidateResponse(
    Guid Id,
    Guid MemberId,
    string Status,
    Guid NominatedBy,
    int? Votes);

public sealed record ElectionDetailResponse(
    ElectionResponse Election,
    IReadOnlyList<CandidateResponse> Candidates,

    /// <summary>
    /// False when this caller is being shown a ballot without counts. The
    /// screen needs to know the difference between "no votes yet" and "you may
    /// not see the votes".
    /// </summary>
    bool TallyVisible);

/// <summary>
/// The published result, in order, with the counts that produced it, and who
/// took the seats.
/// </summary>
public sealed record ElectionResultResponse(
    Guid ElectionId,
    IReadOnlyList<ResultEntry> Ranking,
    IReadOnlyList<Guid> SeatedMemberIds,
    Guid PublishedBy,
    DateTimeOffset PublishedAt);

public sealed record ResultEntry(int Rank, Guid CandidateId, Guid MemberId, int Votes);

/// <summary>
/// The seated Samaaj Panch. <paramref name="ViewerIsMember"/> says whether the
/// asker sits on it - the screen never has to work that out from the list.
/// </summary>
public sealed record PanelResponse(
    IReadOnlyList<PanelMemberResponse> Members,
    int Seats,
    bool ViewerIsMember);

public sealed record PanelMemberResponse(Guid MemberId, Guid ElectionId, DateTimeOffset SeatedAt);
