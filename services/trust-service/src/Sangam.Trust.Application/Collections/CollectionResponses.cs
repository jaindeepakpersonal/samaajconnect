namespace Sangam.Trust.Application.Collections;

public sealed record CollectionResponse(
    Guid Id,
    Guid TrustId,
    string SourceType,
    Guid SourceId,
    Guid? PayerMemberId,
    string? PayerName,
    long Amount,
    string Status,
    string? Mode,
    Guid? CollectedBy,
    DateTimeOffset? CollectedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastRemindedAt);
