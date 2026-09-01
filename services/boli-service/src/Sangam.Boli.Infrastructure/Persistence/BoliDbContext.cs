using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sangam.Boli.Application.Abstractions;
using Sangam.Boli.Domain.Common;
using Sangam.Boli.Domain.Auctions;

namespace Sangam.Boli.Infrastructure.Persistence;

public sealed class BoliDbContext(
    DbContextOptions<BoliDbContext> options,
    ITenantContext tenantContext)
    : DbContext(options)
{
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new(JsonSerializerDefaults.Web);

    public DbSet<BoliOccasion> Occasions => Set<BoliOccasion>();

    public DbSet<BoliType> Types => Set<BoliType>();

    public DbSet<Domain.Auctions.Boli> Boli => Set<Domain.Auctions.Boli>();

    public DbSet<Bid> Bids => Set<Bid>();

    public DbSet<BoliResult> Results => Set<BoliResult>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Backing value for the global tenant query filter. Public because the
    /// filter expression reads it off this context instance, and EF
    /// re-evaluates it per query rather than baking it into the cached model.
    /// </summary>
    public Guid CurrentTenantId => tenantContext.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BoliDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Applies the tenant filter to every entity implementing
    /// <see cref="ITenantScopedEntity"/>, by reflection rather than one call per
    /// entity (CLAUDE.md section 6).
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
