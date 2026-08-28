using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.RegisterMember;

/// <summary>
/// Self-service member registration into exactly one Samaaj.
/// </summary>
/// <remarks>
/// Takes a <paramref name="TenantSlug"/> rather than a tenant id. Registration
/// happens on the apex domain, where no subdomain has been resolved, so the
/// Samaaj comes from the form's "Select Samaaj" field. That is not the same as
/// trusting a client-supplied tenant id (SECURITY-CHECKLIST.md): the slug is
/// resolved server-side against the tenant table, exactly as the gateway would,
/// and a request arriving with a resolved tenant that disagrees is rejected.
/// </remarks>
[AllowAnonymousRequest]
public sealed record RegisterMemberCommand(
    string TenantSlug,
    string FullName,
    string MobileOrEmail,
    string Password) : ICommand<RegisterMemberResponse>;
