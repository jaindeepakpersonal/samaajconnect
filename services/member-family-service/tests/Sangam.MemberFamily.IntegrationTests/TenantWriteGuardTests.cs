using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Domain.Members;
using Sangam.MemberFamily.Infrastructure.Persistence;
using Xunit;

namespace Sangam.MemberFamily.IntegrationTests;

/// <summary>
/// The last line of the tenant isolation defence: a write that would put a row
/// in the wrong Samaaj is refused at save time, whatever the handler did.
/// </summary>
/// <remarks>
/// SECURITY-CHECKLIST.md asks each write handler to re-check the target's
/// tenant. They do - but a handler that forgets looks exactly like one that
/// does not need to, so the rule is also enforced once where it cannot be
/// skipped. These tests are what say the enforcement is real; without them the
/// guard is a comment.
/// </remarks>
public sealed class TenantWriteGuardTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>
{
    /// <summary>
    /// A scope whose <see cref="ITenantContext"/> reports one fixed Samaaj, as
    /// a real request's would.
    /// </summary>
    private async Task WithTenantAsync(Guid tenantId, Func<MemberFamilyDbContext, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var db = new MemberFamilyDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<MemberFamilyDbContext>>(),
            new FixedTenantContext(tenantId));

        await action(db);
    }

    [Fact]
    public async Task A_write_into_another_Samaaj_is_refused_at_save_time()
    {
        var acting = Guid.NewGuid();
        var somewhereElse = Guid.NewGuid();

        await WithTenantAsync(acting, async db =>
        {
            // Exactly what a handler that forgot its tenant check would do:
            // build an entity whose tenant came from somewhere other than the
            // resolved request.
            db.MemberProfiles.Add(MemberProfile.FromRegistration(
                Guid.NewGuid(), somewhereElse, "Ravi Shah", "ravi@example.com",
                DateTimeOffset.UtcNow));

            var save = async () => await db.SaveChangesAsync();

            await save.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*missing its tenant check*");
        });
    }

    [Fact]
    public async Task Modifying_a_row_into_another_Samaaj_is_refused_too()
    {
        var acting = Guid.NewGuid();
        var profileId = Guid.NewGuid();

        await WithTenantAsync(acting, async db =>
        {
            db.MemberProfiles.Add(MemberProfile.FromRegistration(
                profileId, acting, "Ravi Shah", "ravi@example.com", DateTimeOffset.UtcNow));

            await db.SaveChangesAsync();
        });

        await WithTenantAsync(acting, async db =>
        {
            var profile = await db.MemberProfiles.SingleAsync(p => p.Id == profileId);

            // TenantId has a private setter, so nothing in the domain can do
            // this; EF can, which is the point of checking the tracked state
            // rather than trusting the aggregate.
            db.Entry(profile).Property(nameof(MemberProfile.TenantId)).CurrentValue = Guid.NewGuid();

            var save = async () => await db.SaveChangesAsync();

            await save.Should().ThrowAsync<InvalidOperationException>();
        });
    }

    [Fact]
    public async Task A_write_into_the_resolved_Samaaj_is_allowed()
    {
        var acting = Guid.NewGuid();

        await WithTenantAsync(acting, async db =>
        {
            db.MemberProfiles.Add(MemberProfile.FromRegistration(
                Guid.NewGuid(), acting, "Meera Shah", "meera@example.com", DateTimeOffset.UtcNow));

            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task A_consumer_with_no_resolved_tenant_is_not_blocked()
    {
        // A Kafka consumer has no request and so no tenant, and the tenant it
        // writes comes from the event. Refusing that would break every
        // cross-service flow on the platform.
        await using var scope = factory.Services.CreateAsyncScope();

        var db = new MemberFamilyDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<MemberFamilyDbContext>>(),
            new FixedTenantContext(null));

        db.MemberProfiles.Add(MemberProfile.FromRegistration(
            Guid.NewGuid(), Guid.NewGuid(), "Aarav Shah", "aarav@example.com",
            DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();
    }

    private sealed class FixedTenantContext(Guid? tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;

        public bool IsOverride => false;

        public bool HasTenantConflict => false;

        public Guid RequireTenantId() =>
            tenantId ?? throw new InvalidOperationException("No tenant on this request.");
    }
}
