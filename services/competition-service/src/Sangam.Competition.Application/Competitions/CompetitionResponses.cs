namespace Sangam.Competition.Application.Competitions;

/// <summary>A competition as the list and detail screens show it.</summary>
public sealed record CompetitionResponse(
    Guid Id,
    string Title,
    string? Description,
    Guid? CompetitionTypeId,
    DateTimeOffset RegistrationStartAt,
    DateTimeOffset RegistrationEndAt,
    string Status,

    /// <summary>Whether registration is open <i>right now</i>: status and clock both.</summary>
    bool AcceptsRegistrations,
    int RegistrationCount,

    /// <summary>Null when this caller has never registered.</summary>
    string? MyRegistrationStatus,
    bool IsJudge,
    DateTimeOffset CreatedAt,

    /// <summary>Null for the whole Samaaj.</summary>
    Guid? LocalityId,

    /// <summary>Where it is held, in the organiser's words. The city or village and locality are the ids beside it.</summary>
    string? Venue = null,

    Guid? CityId = null,

    /// <summary>Where to fetch its photo - versioned by the photo's id, so a replaced one is a new URL. Null when none.</summary>
    string? PhotoUrl = null,

    /// <summary>Main rounds. Zero is one judging, shown to members as its top three and nothing else.</summary>
    int RoundCount = 0,

    int MaxMarks = 100,
    bool AuditionRequired = false,
    DateOnly? AuditionDate = null,

    /// <summary>How many go through from the audition - the top N, ties at the cut-off included.</summary>
    int? FinalistCount = null,

    /// <summary>Main, Audition or Both when this caller is a judge of it; null otherwise.</summary>
    string? MyJudgePanel = null);

/// <summary>A judge, and the panel they sit on - Main, Audition or Both.</summary>
public sealed record JudgeResponse(Guid Id, Guid MemberId, DateTimeOffset AssignedAt, string Panel = "Main");

public sealed record RoundResponse(
    Guid Id,
    string Name,
    int SequenceNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    int EntryCount,
    bool HasPublishedResult,

    /// <summary>Audition or Main.</summary>
    string Kind = "Main");

public sealed record CompetitionDetailResponse(
    CompetitionResponse Competition,
    IReadOnlyList<RoundResponse> Rounds,
    IReadOnlyList<JudgeResponse> Judges,

    /// <summary>
    /// Currently-registered member ids, in registration order - what an
    /// organiser picks a round's entries from. Only meaningful alongside
    /// <see cref="CanManage"/>; there is no reason for an ordinary member to
    /// see the rest of the roster, so nothing hides it beyond that this is
    /// the one place it is asked for.
    /// </summary>
    IReadOnlyList<Guid> RegisteredMemberIds,

    /// <summary>Whether this caller holds `Competition.Manage` - the organiser controls the screen should offer.</summary>
    bool CanManage,

    /// <summary>Whether the final standings have been published.</summary>
    bool HasPublishedResult = false);

/// <summary>
/// One entry in a round.
/// </summary>
/// <remarks>
/// <paramref name="AverageScore"/> and <paramref name="MyScore"/> are both
/// null for a caller who is neither the organiser nor an assigned judge for
/// this competition - individual marks are never shown to an ordinary
/// member, before or after publication, the same reasoning
/// social-issues-service keeps a reviewer's own reason private.
/// </remarks>
public sealed record EntryResponse(
    Guid Id,
    Guid ParticipantMemberId,
    DateTimeOffset CreatedAt,
    double? AverageScore,
    int? MyScore);

public sealed record RoundDetailResponse(
    RoundResponse Round,
    IReadOnlyList<EntryResponse> Entries,

    /// <summary>False when this caller is seeing entries with no marks attached - the organiser or judges see them, nobody else does.</summary>
    bool CanSeeScores);

public sealed record ScoreResponse(
    Guid EntryId, Guid JudgeMemberId, int Value, string? Comments, DateTimeOffset UpdatedAt);

public sealed record JudgeAssignmentResponse(Guid CompetitionId, Guid MemberId, bool Assigned, string? Panel = null);

/// <summary>A place in the standings: rank (shared on an equal total), who, and their total of round averages.</summary>
public sealed record StandingResponse(int Rank, Guid MemberId, double Total);

/// <summary>
/// The published final standings. Everyone sees the winners - first, second
/// and third, ties included; the full standings are the organiser's.
/// </summary>
public sealed record CompetitionResultResponse(
    Guid CompetitionId,
    IReadOnlyList<StandingResponse> Winners,

    /// <summary>Every participant, best first - for the organiser only; empty for anybody else.</summary>
    IReadOnlyList<StandingResponse> Standings,

    int RoundCount,
    int MaxMarks,
    Guid PublishedBy,
    DateTimeOffset PublishedAt);

/// <summary>One judge's mark on one entry, as the organiser's scoresheet shows it.</summary>
public sealed record JudgeMarkResponse(Guid JudgeMemberId, int Value, string? Comments, DateTimeOffset UpdatedAt);

public sealed record ScoresheetEntryResponse(
    Guid EntryId,
    Guid ParticipantMemberId,
    IReadOnlyList<JudgeMarkResponse> Marks,

    /// <summary>The judges' average; null when nobody has marked it.</summary>
    double? Average,

    /// <summary>On an audition round: whether this participant went through. Null until the audition is closed, and on a main round.</summary>
    bool? WentThrough);

public sealed record ScoresheetRoundResponse(RoundResponse Round, IReadOnlyList<ScoresheetEntryResponse> Entries);

/// <summary>
/// Every mark every judge has given, round by round, and the standings they
/// add up to now - the organiser's panel. Live: the standings move while
/// rounds are open, and only the published result is frozen.
/// </summary>
public sealed record ScoresheetResponse(
    Guid CompetitionId,
    int MaxMarks,
    IReadOnlyList<JudgeResponse> Judges,
    IReadOnlyList<ScoresheetRoundResponse> Rounds,
    IReadOnlyList<StandingResponse> Standings,
    bool HasPublishedResult);

/// <summary>One participant on a judge's sheet, with this judge's own mark if given.</summary>
public sealed record JudgingEntryResponse(Guid EntryId, Guid ParticipantMemberId, int? MyScore, string? MyComments);

public sealed record JudgingRoundResponse(
    RoundResponse Round,

    /// <summary>Whether this judge may mark it now: their panel judges it and it is open.</summary>
    bool CanScore,

    IReadOnlyList<JudgingEntryResponse> Entries);

/// <summary>
/// A judge's own sheet: the rounds their panel judges, each participant, and
/// the marks they have given. Never another judge's marks, and never the
/// judge's own entry, if they are also taking part.
/// </summary>
public sealed record JudgingSheetResponse(
    Guid CompetitionId,
    int MaxMarks,
    string Panel,
    IReadOnlyList<JudgingRoundResponse> Rounds);

public sealed record RegistrationResponse(Guid CompetitionId, Guid MemberId, bool Registered);

public sealed record RoundResultEntry(int Rank, Guid EntryId, Guid ParticipantMemberId, double AverageScore);

/// <summary>
/// A round's published ranking, with the averages that produced it. The
/// order comes from the stored `RoundResult` rather than a fresh
/// computation - see `RoundResult`'s own remarks for why an announced
/// result must not move. The averages themselves are still computed fresh
/// for display, the same as celebrity-voting-service's own
/// `GetResultsQuery` re-tallies alongside its frozen ranking.
/// </summary>
public sealed record RoundResultResponse(
    Guid RoundId,
    IReadOnlyList<RoundResultEntry> Ranking,
    Guid PublishedBy,
    DateTimeOffset PublishedAt);
