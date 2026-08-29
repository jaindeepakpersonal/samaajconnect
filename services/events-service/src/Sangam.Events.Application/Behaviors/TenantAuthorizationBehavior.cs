using System.Collections.Concurrent;
using System.Reflection;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Security;

namespace Sangam.Events.Application.Behaviors;

/// <summary>
/// Behavior 2 of 5 (CLAUDE.md section 4.4). Runs before validation so an
/// unauthorized caller never learns anything about the validation rules for
/// data they cannot access.
/// </summary>
public sealed class TenantAuthorizationBehavior<TRequest, TResponse>(
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    ILogger<TenantAuthorizationBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    private static readonly ConcurrentDictionary<Type, RequestPolicy> Policies = new();

    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Checked first, ahead of even the anonymous short-circuit: a tenant
        // override is the single most sensitive thing a request can carry, and
        // it must be both audited and refused to anyone but a Super Admin, on
        // every request (SECURITY-CHECKLIST.md).
        if (tenantContext.IsOverride)
        {
            logger.LogWarning(
                "Tenant override in use: actor {ActorUserId} acting on tenant {TenantId} via {Request}",
                currentUser.UserId,
                tenantContext.TenantId,
                typeof(TRequest).Name);

            if (!currentUser.IsAuthenticated || !currentUser.IsInRole(Roles.SuperAdmin))
            {
                logger.LogError(
                    "Rejected tenant override from non-Super-Admin caller {ActorUserId} on {Request}",
                    currentUser.UserId,
                    typeof(TRequest).Name);

                return Task.FromResult(ResultFactory.Failure<TResponse>(
                    Error.Forbidden("Auth.OverrideDenied", "You are not allowed to act on another Samaaj.")));
            }
        }

        if (tenantContext.HasTenantConflict)
        {
            logger.LogError(
                "Rejected {Request}: the X-Tenant-Id header disagrees with the tenant the token was issued for",
                typeof(TRequest).Name);

            return Task.FromResult(ResultFactory.Failure<TResponse>(Error.Forbidden(
                "Tenant.Mismatch", "This request does not belong to the Samaaj you are signed in to.")));
        }

        var policy = Policies.GetOrAdd(typeof(TRequest), static type => new RequestPolicy(
            type.GetCustomAttribute<AllowAnonymousRequestAttribute>() is not null
                || type.GetCustomAttribute<InternalRequestAttribute>() is not null,
            type.GetCustomAttribute<RequiresRolesAttribute>()?.Roles ?? [],
            type.GetCustomAttribute<RequiresPermissionAttribute>()?.PermissionKey));

        if (policy.AllowAnonymous)
        {
            return next();
        }

        if (!currentUser.IsAuthenticated)
        {
            return Task.FromResult(ResultFactory.Failure<TResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request.")));
        }

        // Fail closed: an unannotated request is a mistake, not a public endpoint.
        if (policy.Roles.Length == 0 && policy.PermissionKey is null)
        {
            logger.LogError(
                "{Request} declares no authorization policy. Annotate it with "
                + "[RequiresRoles], [RequiresPermission], or [AllowAnonymousRequest].",
                typeof(TRequest).Name);

            return Task.FromResult(ResultFactory.Failure<TResponse>(
                Error.Forbidden("Auth.NoPolicy", "This request has no authorization policy declared.")));
        }

        if (policy.Roles.Length > 0 && !policy.Roles.Any(currentUser.IsInRole))
        {
            return Task.FromResult(Denied(policy));
        }

        if (policy.PermissionKey is not null && !currentUser.HasPermission(policy.PermissionKey))
        {
            return Task.FromResult(Denied(policy));
        }

        return next();
    }

    private TResponse Denied(RequestPolicy policy)
    {
        logger.LogWarning(
            "Authorization denied for {Request}: user {UserId} lacks roles [{Roles}] / permission {Permission}",
            typeof(TRequest).Name,
            currentUser.UserId,
            string.Join(", ", policy.Roles),
            policy.PermissionKey);

        return ResultFactory.Failure<TResponse>(
            Error.Forbidden("Auth.Forbidden", "You are not allowed to perform this action."));
    }

    private sealed record RequestPolicy(bool AllowAnonymous, string[] Roles, string? PermissionKey);
}
