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
    /// <summary>The built-in role every trustee holds until they hold another.</summary>
    public const string TrusteeTitle = "Trustee";

    private readonly List<TrustRole> _roles = [];

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

    public IReadOnlyCollection<TrustRole> Roles => _roles.AsReadOnly();

    public TrustRole TrusteeRole => _roles.Single(r => r.Kind == TrustRoleKind.Trustee);

    public TrustRole? FindRole(Guid roleId) => _roles.FirstOrDefault(r => r.Id == roleId);

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

        trust._roles.Add(new TrustRole(trust.Id, TrusteeTitle, TrustRoleKind.Trustee, seats: null, now));

        trust.Raise(new TrustCreatedDomainEvent(trust.Id, tenantId, localityId, trust.Name, now));

        return trust;
    }

    // ---- Roles -----------------------------------------------------------------
    //
    // Holder counts come from the roster, which is its own aggregate, so the
    // caller passes them in rather than this aggregate loading every trustee.

    public TrustRoleRefusal DefineRole(string title, int seats, Guid changedBy, DateTimeOffset now, out TrustRole? role)
    {
        role = null;

        if (TitleTaken(title, exceptRoleId: null))
        {
            return TrustRoleRefusal.DuplicateTitle;
        }

        role = new TrustRole(Id, title, TrustRoleKind.Elected, seats, now);
        _roles.Add(role);

        Raise(new TrustRoleDefinedDomainEvent(Id, TenantId, role.Id, changedBy, now));

        return TrustRoleRefusal.None;
    }

    public TrustRoleRefusal UpdateRole(
        Guid roleId, string title, int seats, int holders, Guid changedBy, DateTimeOffset now)
    {
        var role = FindRole(roleId);

        if (role is null)
        {
            return TrustRoleRefusal.NotFound;
        }

        if (!role.IsElected)
        {
            return TrustRoleRefusal.NotEditable;
        }

        if (TitleTaken(title, exceptRoleId: roleId))
        {
            return TrustRoleRefusal.DuplicateTitle;
        }

        if (seats < holders)
        {
            return TrustRoleRefusal.SeatsBelowHolders;
        }

        role.Change(title, seats);

        Raise(new TrustRoleUpdatedDomainEvent(Id, TenantId, roleId, changedBy, now));

        return TrustRoleRefusal.None;
    }

    public TrustRoleRefusal RemoveRole(Guid roleId, int holders, Guid changedBy, DateTimeOffset now)
    {
        var role = FindRole(roleId);

        if (role is null)
        {
            return TrustRoleRefusal.NotFound;
        }

        if (!role.IsElected)
        {
            return TrustRoleRefusal.NotEditable;
        }

        if (holders > 0)
        {
            return TrustRoleRefusal.HasHolders;
        }

        _roles.Remove(role);

        Raise(new TrustRoleRemovedDomainEvent(Id, TenantId, roleId, changedBy, now));

        return TrustRoleRefusal.None;
    }

    private bool TitleTaken(string title, Guid? exceptRoleId) =>
        _roles.Any(r => r.Id != exceptRoleId
            && string.Equals(r.Title, title.Trim(), StringComparison.OrdinalIgnoreCase));

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

    /// <summary>
    /// Moves this Trust to a new status. A no-op that changes nothing is
    /// reported to the caller as success rather than raising a duplicate event
    /// - a repeated click is not a second fact.
    /// </summary>
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
