namespace Sangam.Trust.Application.Elections;

/// <summary>An election as the list and detail screens show it.</summary>
public sealed record TrustElectionResponse(
    Guid Id,
    Guid TrustId,
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

/// <summary>One name on the ballot. The vote count is never hidden - a Trust election has no results-visibility setting.</summary>
public sealed record TrustElectionCandidateResponse(
    Guid Id, Guid MemberId, string Status, Guid NominatedBy, int Votes);

public sealed record TrustElectionDetailResponse(
    TrustElectionResponse Election, IReadOnlyList<TrustElectionCandidateResponse> Candidates);
