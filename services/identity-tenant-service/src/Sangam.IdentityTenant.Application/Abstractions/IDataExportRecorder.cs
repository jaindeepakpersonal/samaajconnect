namespace Sangam.IdentityTenant.Application.Abstractions;

/// <summary>
/// Announces that someone exported a copy of a member's data.
/// </summary>
/// <remarks>
/// SECURITY-CHECKLIST.md asks for exports of member data to be logged as audit
/// events. Without this, the one operation that produces a complete copy of a
/// person's data leaves no trace at all - which is the wrong way round, since
/// it is more worth recording than most of what already is.
///
/// It writes on its own scope, like <see cref="IFailedLoginRecorder"/>, for a
/// related reason: the export is a query, so no transaction is open and no
/// unit of work will be committed on its behalf. A recorder that assumed one
/// would silently record nothing.
///
/// A failure here must not fail the export. The right to a copy of your data
/// (DPDP s.11) does not depend on the platform's bookkeeping succeeding, so
/// implementations log and move on.
/// </remarks>
public interface IDataExportRecorder
{
    Task RecordAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);
}
