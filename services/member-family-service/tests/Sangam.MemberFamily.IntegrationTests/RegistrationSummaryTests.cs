using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Sangam.MemberFamily.Application.Members.Queries.GetRegistrationSummary;
using Sangam.MemberFamily.Domain.Members;
using Sangam.MemberFamily.Infrastructure.Persistence;
using Xunit;

namespace Sangam.MemberFamily.IntegrationTests;

/// <summary>
/// The admin dashboard's Reports &amp; Analytics screen: registrations in the
/// last 30 days against the 30 days before that.
/// </summary>
public sealed class RegistrationSummaryTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();

    private readonly string _run = Guid.NewGuid().ToString("N")[..6];

    public async Task InitializeAsync()
    {
        var now = DateTimeOffset.UtcNow;

        // Current 30-day window: two registrations.
        await SeedAsync(TenantId, now - TimeSpan.FromDays(5));
        await SeedAsync(TenantId, now - TimeSpan.FromDays(20));

        // Previous 30-day window: one registration.
        await SeedAsync(TenantId, now - TimeSpan.FromDays(45));

        // Outside either window - must not be counted.
        await SeedAsync(TenantId, now - TimeSpan.FromDays(90));

        // A different Samaaj entirely - must not be counted here either.
        await SeedAsync(OtherTenantId, now - TimeSpan.FromDays(5));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedAsync(Guid tenantId, DateTimeOffset createdAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        var id = Guid.NewGuid();
        var email = id.ToString("N") + "@example.com";

        var profile = MemberProfile.FromRegistration(
            id, tenantId, "Member " + _run, email, createdAt);

        db.MemberProfiles.Add(profile);
        await db.SaveChangesAsync();
    }

    private HttpClient AdminClient() =>
        factory.CreateClientAs(Guid.NewGuid(), TenantId, ["SamaajAdmin"], ["Members.Read"]);

    [Fact]
    public async Task Counts_only_this_tenants_registrations_in_each_window()
    {
        var response = await AdminClient().GetAsync("/v1/members/registration-summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await response.Content.ReadFromJsonAsync<RegistrationSummaryResponse>();

        summary!.CurrentPeriodCount.Should().Be(2);
        summary.PreviousPeriodCount.Should().Be(1);
        summary.GrowthPercentage.Should().BeApproximately(100.0, 0.01);
    }

    [Fact]
    public async Task Refuses_a_plain_member()
    {
        var member = factory.CreateClientAs(Guid.NewGuid(), TenantId, ["Member"], ["Members.Read"]);

        var response = await member.GetAsync("/v1/members/registration-summary");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
