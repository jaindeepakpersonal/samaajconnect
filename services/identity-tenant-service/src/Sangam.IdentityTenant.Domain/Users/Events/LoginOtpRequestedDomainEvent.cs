using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// A member asked for a one-time sign-in code. Carries the plaintext code and
/// the address to send it to - unlike most events, which name a person and
/// nothing else, because this one is the only way the code ever leaves this
/// service. There is no admin standing between issuing it and delivering it,
/// the way there is for <see cref="ActivationCode"/>.
/// </summary>
public sealed record LoginOtpRequestedDomainEvent(
    Guid UserId,
    Guid TenantId,
    string Code,
    string MobileOrEmail,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.login-otp.requested.v1";
}
