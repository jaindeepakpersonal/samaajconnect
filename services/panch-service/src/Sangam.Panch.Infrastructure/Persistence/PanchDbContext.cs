using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Domain.Common;
using Sangam.Panch.Domain.Elections;
using Sangam.Panch.Domain.Panel;

namespace Sangam.Panch.Infrastructure.Persistence;

public sealed class PanchDbContext(
    DbContextOptions<PanchDbContext> options,
    ITenantContext tenantContext)
    : DbContext(options)
{
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new(JsonSerializerDefaults.Web);

    public DbSet<PanchElection> Elections => Set<PanchElection>();

    public DbSet<Candidate> Candidates => Set<Candidate>();

    public DbSet<Ballot> Ballots => Set<Ballot>();

    public DbSet<Vote> Votes => Set<Vote>();

    public DbSet<PanelMember> PanelMembers => Set<PanelMember>();

    public DbSet<ElectionResult> Results => Set<ElectionResult>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>
    /// Backing value for the global tenant query filter. Public because the
    /// filter expression reads it off this context instance, and EF
    /// re-evaluates it per query rather than baking it into the cached model.
    /// </summary>
    public Guid CurrentTenantId => tenantContext.TenantId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PanchDbContext).Assembly);

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
