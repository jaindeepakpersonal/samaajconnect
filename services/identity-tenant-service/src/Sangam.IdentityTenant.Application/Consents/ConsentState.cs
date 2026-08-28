using Sangam.IdentityTenant.Domain.Consents;

namespace Sangam.IdentityTenant.Application.Consents;

internal static class ConsentState
{
    /// <summary>
    /// Collapses the append-only history into the current state of each
    /// purpose: the latest decision wins. The history itself is never
    /// rewritten, so this is always derived rather than stored.
    /// </summary>
    public static IReadOnlyList<ConsentStateResponse> From(IEnumerable<ConsentRecord> history) =>
        history
            .GroupBy(record => record.Purpose)
            .Select(group => group.OrderByDescending(record => record.RecordedAt).First())
            .Select(latest => new ConsentStateResponse(
                latest.Purpose.ToString(),
                latest.Action == ConsentAction.Granted,
                latest.NoticeVersion,
                latest.RecordedAt))
            .OrderBy(state => state.Purpose)
            .ToList();

    public static IReadOnlyList<ConsentRecordResponse> ToHistory(IEnumerable<ConsentRecord> history) =>
        history
            .OrderBy(record => record.RecordedAt)
            .Select(record => new ConsentRecordResponse(
                record.Purpose.ToString(),
                record.Action.ToString(),
                record.NoticeVersion,
                record.Source,
                record.RecordedAt))
            .ToList();
}
