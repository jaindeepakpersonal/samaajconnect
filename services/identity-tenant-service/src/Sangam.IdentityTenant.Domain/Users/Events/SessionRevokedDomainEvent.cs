using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// A sign-in session was ended before its natural expiry.
/// </summary>
/// <remarks>
/// Audited because the reasons matter operationally. A member signing out is
/// routine; <see cref="SessionEndReason.ReuseDetected"/> means a refresh token
/// was presented twice and somebody other than the member is holding a copy,
/// which is the closest thing this platform has to an intrusion signal. Treat a
/// run of those on one account as an incident.
///
/// Ids and a reason. No token, no hash, no contact details.
/// </remarks>
public sealed record SessionRevokedDomainEvent(
    Guid UserId,
    Guid TenantId,
    Guid SessionId,
    string Reason,
    int TokensRevoked,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.session.revoked.v1";
}
