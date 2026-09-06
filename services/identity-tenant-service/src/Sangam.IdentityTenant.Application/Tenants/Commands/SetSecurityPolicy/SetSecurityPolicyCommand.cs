using FluentValidation;
using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Application.Tenants.Queries.GetSecurityPolicy;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.SetSecurityPolicy;

/// <summary>
/// Replaces a Samaaj's security-policy overrides, whole - like
/// <see cref="Commands.SetGrievanceContact.SetGrievanceContactCommand"/>, not
/// a partial PATCH. A null field clears that override back to the platform
/// default; it does not leave the previous override standing.
/// </summary>
/// <remarks>
/// A Samaaj Admin may set their own Samaaj's policy, not any other's - the
/// same shape as the grievance contact and the tenant logo, and for the same
/// reason: routing every change through the platform operator would make it
/// stale by design.
/// </remarks>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.TenantManageSecurityPolicy)]
public sealed record SetSecurityPolicyCommand(
    Guid TenantId,
    int? MinPasswordLength,
    int? OtpLifetimeMinutes,
    int? MaxFailedLoginAttempts,
    int? LockoutDurationMinutes,
    int? RefreshTokenLifetimeDays) : ICommand<SecurityPolicyResponse>;

public sealed class SetSecurityPolicyCommandValidator : AbstractValidator<SetSecurityPolicyCommand>
{
    public SetSecurityPolicyCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();

        // Never below the platform floor - a Samaaj may only ask for longer
        // passwords than 10 characters, never shorter.
        RuleFor(x => x.MinPasswordLength).InclusiveBetween(10, 64)
            .When(x => x.MinPasswordLength is not null);

        RuleFor(x => x.OtpLifetimeMinutes).InclusiveBetween(5, 30)
            .When(x => x.OtpLifetimeMinutes is not null);

        RuleFor(x => x.MaxFailedLoginAttempts).InclusiveBetween(3, 10)
            .When(x => x.MaxFailedLoginAttempts is not null);

        RuleFor(x => x.LockoutDurationMinutes).InclusiveBetween(5, 1440)
            .When(x => x.LockoutDurationMinutes is not null);

        RuleFor(x => x.RefreshTokenLifetimeDays).InclusiveBetween(1, 90)
            .When(x => x.RefreshTokenLifetimeDays is not null);
    }
}

public sealed class SetSecurityPolicyCommandHandler(
    ITenantRepository tenants,
    ITenantSecurityPolicyRepository policies,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
    : IRequestHandler<SetSecurityPolicyCommand, Result<SecurityPolicyResponse>>
{
    public async Task<Result<SecurityPolicyResponse>> Handle(
        SetSecurityPolicyCommand command, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, cancellationToken);

        if (tenant is null)
        {
            return Result.Failure<SecurityPolicyResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        var isSuperAdmin = currentUser.IsInRole(Roles.SuperAdmin);

        if (!isSuperAdmin && tenantContext.TenantId != tenant.Id)
        {
            return Result.Failure<SecurityPolicyResponse>(
                Error.NotFound("Tenant.NotFound", "No Samaaj with that id exists."));
        }

        var policy = await policies.FindAsync(tenant.Id, cancellationToken);

        if (policy is null)
        {
            policy = TenantSecurityPolicy.Create(tenant.Id);
            policies.Add(policy);
        }

        policy.Update(
            command.MinPasswordLength,
            command.OtpLifetimeMinutes,
            command.MaxFailedLoginAttempts,
            command.LockoutDurationMinutes,
            command.RefreshTokenLifetimeDays);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new SecurityPolicyResponse(
            policy.MinPasswordLength,
            policy.OtpLifetimeMinutes,
            policy.MaxFailedLoginAttempts,
            policy.LockoutDurationMinutes,
            policy.RefreshTokenLifetimeDays));
    }
}
