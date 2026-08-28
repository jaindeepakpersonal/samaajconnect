using Sangam.MemberFamily.Domain.Common;

namespace Sangam.MemberFamily.Domain.Children;

/// <summary>
/// Consumed by identity-tenant-service, which creates the login. Deliberately
/// carries no credential: the audit service records every payload verbatim into
/// an append-only table.
/// </summary>
public sealed record ChildConversionApprovedDomainEvent(
    Guid RequestId,
    Guid TenantId,
    Guid ChildProfileId,
    string FullName,
    string MobileOrEmail,
    Guid ApprovedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "members.child-conversion.approved.v1";
}
