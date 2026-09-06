using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Tenants.Queries.GetSecurityPolicy;

/// <summary>
/// A Samaaj's own security-policy overrides, as raw nullable values - not the
/// resolved ones. The Settings screen needs to tell "this Samaaj customised
/// this field" from "this Samaaj is using the platform default", which the
/// resolved value alone cannot say.
/// </summary>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.TenantManageSecurityPolicy)]
public sealed record GetSecurityPolicyQuery(Guid TenantId) : IQuery<SecurityPolicyResponse>;

public sealed record SecurityPolicyResponse(
    int? MinPasswordLength,
    int? OtpLifetimeMinutes,
    int? MaxFailedLoginAttempts,
    int? LockoutDurationMinutes,
    int? RefreshTokenLifetimeDays);

public sealed class GetSecurityPolicyQueryHandler(
    ITenantRepository tenants,
    ITenantSecurityPolicyRepository policies,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
    : IRequestHandler<GetSecurityPolicyQuery, Result<SecurityPolicyResponse>>
{
    public async Task<Result<SecurityPolicyResponse>> Handle(
        GetSecurityPolicyQuery query, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(query.TenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure<SecurityPolicyResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        // Same self-tenant-only shape as the grievance contact and the logo:
        // a Samaaj Admin may read their own Samaaj's policy, not any other's.
        var isSuperAdmin = currentUser.IsInRole(Roles.SuperAdmin);

        if (!isSuperAdmin && tenantContext.TenantId != tenant.Id)
        {
            return Result.Failure<SecurityPolicyResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        var policy = await policies.FindAsync(tenant.Id, cancellationToken);

        return Result.Success(new SecurityPolicyResponse(
            policy?.MinPasswordLength,
            policy?.OtpLifetimeMinutes,
            policy?.MaxFailedLoginAttempts,
            policy?.LockoutDurationMinutes,
            policy?.RefreshTokenLifetimeDays));
    }
}
