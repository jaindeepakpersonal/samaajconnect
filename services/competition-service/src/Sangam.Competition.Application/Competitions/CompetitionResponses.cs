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
    Guid? LocalityId);

public sealed record JudgeResponse(Guid Id, Guid MemberId, DateTimeOffset AssignedAt);

public sealed record RoundResponse(
    Guid Id,
    string Name,
    int SequenceNumber,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    int EntryCount,
    bool HasPublishedResult);

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
    bool CanManage);

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

public sealed record JudgeAssignmentResponse(Guid CompetitionId, Guid MemberId, bool Assigned);

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
