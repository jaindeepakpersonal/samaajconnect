namespace Sangam.AuditNotification.Application.Abstractions;

/// <summary>
/// The only writes in this service that change or remove existing rows.
/// </summary>
/// <remarks>
/// Deliberately its own interface, and deliberately narrow. Audit rows are
/// otherwise append-only, and the way to keep that true is to make the single
/// exception impossible to reach by accident: nothing here takes arbitrary
/// criteria, and the de-identify call cannot touch the action, entity or
/// timestamp - only the fields that name a person.
/// </remarks>
public interface IErasureRepository
{
    /// <summary>Deletes outright. A notification is a message to a person and nothing else.</summary>
    Task<int> DeleteNotificationsForAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the person from the audit rows where they were the actor,
    /// keeping the fact that the action happened. Returns how many were changed.
    /// </summary>
    Task<int> DeIdentifyAuditRowsForAsync(Guid userId, CancellationToken cancellationToken = default);
}
