using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// A member asked to reset their password. Carries the plaintext code and the
/// address to send it to, the same shape as
/// <see cref="LoginOtpRequestedDomainEvent"/> and for the same reason: this
/// pipeline is the only way the code ever reaches whoever asked for it.
/// </summary>
public sealed record PasswordResetRequestedDomainEvent(
    Guid UserId,
    Guid TenantId,
    string Code,
    string MobileOrEmail,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.password-reset.requested.v1";
}
