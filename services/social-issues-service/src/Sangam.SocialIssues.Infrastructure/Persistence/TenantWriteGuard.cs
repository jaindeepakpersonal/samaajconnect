using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Domain.Common;

namespace Sangam.SocialIssues.Infrastructure.Persistence;

/// <summary>
/// Refuses, at save time, any write that would put a row in a Samaaj other than
/// the one the request resolved to.
/// </summary>
/// <remarks>
/// SECURITY-CHECKLIST.md asks every write handler to re-validate the target's
/// <c>TenantId</c> against <see cref="ITenantContext"/> rather than trusting
/// the query filter. Most of them do. The trouble with leaving it there is the
/// failure mode: the check is invisible when it is missing, so a new handler
/// that forgets it looks exactly like one that does not need it, and nothing
/// fails until the day it matters.
///
/// This is the same rule enforced once, where it cannot be forgotten. Handlers
/// keep their own checks - a 404 explaining that no such member exists in this
/// Samaaj is a far better answer than an exception - and this catches what they
/// miss.
///
/// <b>It is deliberately silent when no tenant is resolved.</b> A Kafka
/// consumer has no request and therefore no tenant, and registration resolves a
/// Samaaj from a slug before any tenant exists on the request; refusing those
/// would break correct code. That is not a hole: those paths reach the database
/// through repository methods that bypass the query filter on purpose, and each
/// one is individually justified where it is declared.
///
/// A Super Admin override populates the same <see cref="ITenantContext"/>, so
/// an overridden request is checked against the Samaaj being administered -
/// which is exactly right, and is why there is no separate admin bypass here.
/// </remarks>
internal static class TenantWriteGuard
{
    public static void Verify(ChangeTracker changeTracker, ITenantContext tenantContext)
    {
        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return;
        }

        foreach (var entry in changeTracker.Entries<ITenantScopedEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entry.Entity.TenantId == tenantId)
            {
                continue;
            }

            // Not a Result: a handler returning a failure here would be
            // reporting a business outcome, and this is not one. Reaching this
            // line means a bug in a write path, and the pipeline's
            // UnhandledExceptionBehavior turns it into a generic failure at the
            // boundary while the log keeps the detail.
            throw new InvalidOperationException(
                $"Refusing to save {entry.Entity.GetType().Name} belonging to Samaaj "
                + $"{entry.Entity.TenantId} on a request resolved to Samaaj {tenantId}. "
                + "A write path is missing its tenant check.");
        }
    }
}
