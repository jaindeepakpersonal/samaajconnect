namespace Sangam.IdentityTenant.Application.Abstractions;

/// <summary>
/// Records a failed password attempt <b>outside</b> the ambient transaction.
/// </summary>
/// <remarks>
/// This exists because of an interaction that is easy to get wrong.
/// <c>LoginCommand</c> is a command, so <c>TransactionBehavior</c> wraps it and
/// rolls back whenever the handler returns a failure — which is exactly what a
/// wrong password returns. Incrementing the attempt counter on the tracked
/// aggregate would therefore be rolled back with everything else, and the
/// lockout required by SECURITY-CHECKLIST.md would silently never trigger.
/// The implementation uses its own scope, context and connection so the
/// increment survives the rollback of the request that caused it.
/// </remarks>
public interface IFailedLoginRecorder
{
    /// <summary>Returns true when this attempt locked the account.</summary>
    Task<bool> RecordAsync(Guid userId, CancellationToken cancellationToken = default);
}
