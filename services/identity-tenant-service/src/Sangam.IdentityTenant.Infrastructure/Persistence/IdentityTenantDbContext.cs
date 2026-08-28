using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Common;
using Sangam.IdentityTenant.Domain.Consents;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Infrastructure.Persistence;

public sealed class IdentityTenantDbContext(
    DbContextOptions<IdentityTenantDbContext> options,
    ITenantContext tenantContext)
    : DbContext(options)
{
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new(JsonSerializerDefaults.Web);

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Backing value for the global tenant query filter. Public because the
    /// filter expression reads it off this context instance, and EF
    /// re-evaluates it per query rather than baking it into the cached model.
    /// </summary>
    public Guid CurrentTenantId => tenantContext.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityTenantDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Applies the tenant filter to every entity implementing
    /// <see cref="ITenantScopedEntity"/>, by reflection rather than one call per
    /// entity (CLAUDE.md §6). Written once per service so adding a
    /// tenant-scoped entity later cannot forget it.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantScopedEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");

            var body = Expression.Equal(
                Expression.Property(parameter, nameof(ITenantScopedEntity.TenantId)),
                Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TenantWriteGuard.Verify(ChangeTracker, tenantContext);
        DrainDomainEventsToOutbox();

        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        TenantWriteGuard.Verify(ChangeTracker, tenantContext);
        DrainDomainEventsToOutbox();

        return base.SaveChanges();
    }

    /// <summary>
    /// Turns every event raised during this unit of work into an Outbox row.
    /// Runs inside SaveChanges so the rows and the state change share one
    /// transaction — that shared transaction is the whole point of the pattern.
    /// </summary>
    private void DrainDomainEventsToOutbox()
    {
        var aggregates = ChangeTracker
            .Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToArray();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    TenantId = domainEvent.TenantId,
                    Topic = domainEvent.Topic,
                    Type = domainEvent.GetType().FullName!,
                    Payload = JsonSerializer.Serialize(
                        domainEvent, domainEvent.GetType(), PayloadSerializerOptions),
                    OccurredAt = domainEvent.OccurredAt,
                });
            }

            aggregate.ClearDomainEvents();
        }
    }
}
