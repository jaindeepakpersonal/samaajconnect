using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Trusts;

/// <summary>A Trust was set up for a locality that had none.</summary>
public sealed record TrustCreatedDomainEvent(
    Guid TrustId,
    Guid TenantId,
    Guid LocalityId,
    string Name,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.trust.created.v1";
}

/// <summary>
/// A Trust's status changed. Carries the previous status as well as the new
/// one, per SECURITY-CHECKLIST.md.
/// </summary>
public sealed record TrustStatusChangedDomainEvent(
    Guid TrustId,
    Guid TenantId,
    string PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.trust.status-changed.v1";
}

/// <summary>A Trust's name was corrected. Ids and who, not the name.</summary>
public sealed record TrustRenamedDomainEvent(
    Guid TrustId,
    Guid TenantId,
    Guid RenamedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.trust.renamed.v1";
}

/// <summary>A Trust's roles changed. Ids and who; the role is on the Trust.</summary>
public sealed record TrustRoleDefinedDomainEvent(
    Guid TrustId,
    Guid TenantId,
    Guid RoleId,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.role.defined.v1";
}

/// <summary>A Trust's roles changed. Ids and who; the role is on the Trust.</summary>
public sealed record TrustRoleUpdatedDomainEvent(
    Guid TrustId,
    Guid TenantId,
    Guid RoleId,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.role.updated.v1";
}

/// <summary>A Trust's roles changed. Ids and who; the role is on the Trust.</summary>
public sealed record TrustRoleRemovedDomainEvent(
    Guid TrustId,
    Guid TenantId,
    Guid RoleId,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.role.removed.v1";
}
