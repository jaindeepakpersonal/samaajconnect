using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// Closes the conversion loop: member-family-service consumes this and marks
/// the child record Converted, linking it to the account that now exists.
/// </summary>
public sealed record UserActivatedFromChildDomainEvent(
    Guid UserId,
    Guid TenantId,
    Guid ChildProfileId,
    string MobileOrEmail,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.child-conversion.completed.v1";
}
