using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Tenants.Queries.GetTenantById;

/// <summary>
/// Resolves a tenant id to its public summary. This is what the gateway calls
/// on every authenticated request to confirm the Samaaj named by a token's
/// `tenant_id` claim is still active, and which modules it runs.
/// </summary>
/// <remarks>
/// Anonymous for the same reason slug resolution is: the gateway calls it while
/// deciding whether a request may proceed at all, and it returns only the
/// public summary - no contact details. A tenant id is a Guid, so this is not
/// an enumerable surface either.
/// </remarks>
[AllowAnonymousRequest]
public sealed record GetTenantByIdQuery(Guid TenantId) : IQuery<TenantSummaryResponse>;
