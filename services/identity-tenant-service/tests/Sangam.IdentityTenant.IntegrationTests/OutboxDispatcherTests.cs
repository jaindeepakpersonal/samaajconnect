using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Security;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Exercises the dispatcher against a real Postgres. The interesting behavior
/// here is transactional, so an in-memory provider would prove nothing.
/// </summary>
public sealed class OutboxDispatcherTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedOneTenantAsync()
    {
        var client = factory.CreateClientWith(PermissionKeys.TenantManage);

        var response = await client.PostAsJsonAsync("/v1/identity/tenants", new
        {
            name = "Pune Samaaj",
            slug = "pune-samaaj",
            enabledModules = Array.Empty<string>(),
        });

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Publishes_pending_rows_and_marks_them_processed()
    {
        await SeedOneTenantAsync();

        var dispatched = await factory.DispatchOutboxAsync();

        dispatched.Should().Be(1);

        factory.Publisher.Published.Should().ContainSingle()
            .Which.Topic.Should().Be("identity.tenant.created.v1");

        var pending = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().CountAsync(m => m.ProcessedAt == null));

        pending.Should().Be(0);
    }

    [Fact]
    public async Task Partitions_by_tenant_id_so_one_Samaaj_events_stay_ordered()
    {
        await SeedOneTenantAsync();

        await factory.DispatchOutboxAsync();

        var tenantId = await factory.WithDbContextAsync(db =>
            db.Tenants.AsNoTracking().Select(t => t.Id).SingleAsync());

        factory.Publisher.Published.Single().Key.Should().Be(tenantId.ToString());
    }

    [Fact]
    public async Task Does_not_republish_a_row_that_was_already_sent()
    {
        await SeedOneTenantAsync();

        await factory.DispatchOutboxAsync();
        var secondPass = await factory.DispatchOutboxAsync();

        secondPass.Should().Be(0);
        factory.Publisher.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task Records_the_failure_and_keeps_the_row_pending_when_the_broker_rejects_it()
    {
        await SeedOneTenantAsync();

        factory.Publisher.ShouldFail = true;

        try
        {
            var dispatched = await factory.DispatchOutboxAsync();

            dispatched.Should().Be(0);
        }
        finally
        {
            factory.Publisher.ShouldFail = false;
        }

        var message = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().SingleAsync());

        message.ProcessedAt.Should().BeNull();
        message.Attempts.Should().Be(1);
        message.Error.Should().Contain("Simulated broker failure");

        // And it is picked up again once the broker recovers.
        (await factory.DispatchOutboxAsync()).Should().Be(1);
    }
}
