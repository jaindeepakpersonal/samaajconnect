namespace Sangam.Trust.Application.Elections;

/// <summary>An election as the list and detail screens show it.</summary>
public sealed record TrustElectionResponse(
    Guid Id,
    Guid TrustId,

    /// <summary>The title of the role it fills, as the Trust names it now.</summary>
    string PositionTitle,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt,
    string Status,

    /// <summary>Whether nominations are open <i>right now</i>: status and clock both.</summary>
    bool AcceptsNominations,
    bool AcceptsVotes,

    /// <summary>The candidates this member picked, in their order. Empty until they vote.</summary>
    IReadOnlyList<Guid> MyVoteCandidateIds,
    int CandidateCount,

    /// <summary>Set once the election is Published, in ranked order. Empty until then.</summary>
    IReadOnlyList<Guid> WinnerMemberIds,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    Guid RoleId,

    /// <summary>How many it fills, and so the most candidates one ballot may pick.</summary>
    int Seats);

/// <summary>One name on the ballot. The vote count is never hidden - a Trust election has no results-visibility setting.</summary>
public sealed record TrustElectionCandidateResponse(
    Guid Id, Guid MemberId, string Status, Guid NominatedBy, int Votes);

public sealed record TrustElectionDetailResponse(
    TrustElectionResponse Election, IReadOnlyList<TrustElectionCandidateResponse> Candidates);
