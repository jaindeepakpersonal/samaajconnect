using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Trusts;

/// <summary>
/// A Samaaj's local Trust: exactly one per locality. Holds its own status;
/// its roster is a separate aggregate, <see cref="Members.TrustMember"/> -
/// see that class's own remarks for why.
/// </summary>
/// <remarks>
/// Phase 1 of a larger plan. This aggregate deliberately carries nothing yet
/// about elections, donations, expenses or payment collection - those are
/// later phases. See this service's own CLAUDE.md, "Decisions worth knowing".
/// </remarks>
public sealed class Trust : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>
    /// A loose reference into master-data-service's <c>Locality</c>. No
    /// referential integrity across the service boundary - the same
    /// convention every other consumer of Locality already uses
    /// (<c>Business.LocalityId</c>, <c>BiodataProfile.LocalityId</c>).
    /// </summary>
    public Guid LocalityId { get; private set; }

    public string Name { get; private set; } = null!;

    public TrustStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private Trust() { }   // EF Core

    public static Trust Create(Guid tenantId, Guid localityId, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trust = new Trust
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LocalityId = localityId,
            Name = name.Trim(),
            Status = TrustStatus.Active,
            CreatedAt = now,
        };

        trust.Raise(new TrustCreatedDomainEvent(trust.Id, tenantId, localityId, trust.Name, now));

        return trust;
    }

    /// <summary>
    /// Moves this Trust to a new status. A no-op that changes nothing is
    /// reported to the caller as success rather than raising a duplicate event
    /// - a repeated click is not a second fact.
    /// </summary>
    /// <summary>Corrects the name. False when it is already that name.</summary>
    public bool Rename(string name, Guid renamedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmed = name.Trim();

        if (trimmed == Name)
        {
            return false;
        }

        Name = trimmed;

        // Ids only: the name is on the Trust, and a name in an append-only audit
        // payload is a name nobody can correct there.
        Raise(new TrustRenamedDomainEvent(Id, TenantId, renamedBy, now));

        return true;
    }

    public bool ChangeStatus(TrustStatus target, DateTimeOffset now)
    {
        if (Status == target)
        {
            return false;
        }

        var previous = Status;
        Status = target;

        Raise(new TrustStatusChangedDomainEvent(Id, TenantId, previous.ToString(), target.ToString(), now));

        return true;
    }
}

public enum TrustStatus
{
    Active = 1,
    Inactive = 2,
}
