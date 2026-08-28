namespace Sangam.AuditNotification.Domain.Common;

/// <summary>
/// Base for every aggregate root. Collects domain events raised during a unit
/// of work; the DbContext drains them at SaveChanges time and writes one
/// Outbox row per event inside the same transaction.
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
