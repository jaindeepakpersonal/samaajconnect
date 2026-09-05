using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// A member changed their own password. Carries the id and nothing else, the
/// same shape as <see cref="UserErasedDomainEvent"/> - the fact that this
/// happened belongs in the audit trail, the password itself never should.
/// </summary>
public sealed record PasswordChangedDomainEvent(
    Guid UserId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.user.password-changed.v1";
}
