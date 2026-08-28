namespace Sangam.IdentityTenant.Application.Consents;

public sealed record ConsentNoticeItemResponse(
    string Purpose,
    string Title,
    string Description,
    bool Required);

public sealed record ConsentNoticeResponse(
    string Version,
    IReadOnlyList<ConsentNoticeItemResponse> Items);

/// <summary>One decision, as recorded. The history, not a current-state flag.</summary>
public sealed record ConsentRecordResponse(
    string Purpose,
    string Action,
    string NoticeVersion,
    string Source,
    DateTimeOffset RecordedAt);

public sealed record ConsentStateResponse(
    string Purpose,
    bool Granted,
    string NoticeVersion,
    DateTimeOffset DecidedAt);
