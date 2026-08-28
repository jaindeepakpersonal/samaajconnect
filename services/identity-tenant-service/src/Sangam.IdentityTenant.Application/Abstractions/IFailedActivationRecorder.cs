namespace Sangam.IdentityTenant.Application.Abstractions;

/// <summary>
/// Records a wrong activation-code guess outside the ambient transaction, for
/// the same reason <see cref="IFailedLoginRecorder"/> exists: the command
/// returns a failure, TransactionBehavior rolls it back, and a counter written
/// on the tracked aggregate would be rolled back with it - leaving the code
/// guessable without limit.
/// </summary>
public interface IFailedActivationRecorder
{
    /// <summary>Returns true when this attempt used up the code.</summary>
    Task<bool> RecordAsync(Guid userId, CancellationToken cancellationToken = default);
}
