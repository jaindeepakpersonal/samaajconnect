using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.SocialIssues.Application.Security;
using Sangam.SocialIssues.Domain.Panel;
using Xunit;

namespace Sangam.SocialIssues.IntegrationTests;

/// <summary>
/// An Area Admin's SocialIssues.Approve reaches their own locality's issues
/// only - while it reaches anything, which is until a Panch is seated. A Panch
/// seat is Samaaj-wide and is never narrowed by locality.
/// </summary>
public sealed class AreaAdminIssueTests(SocialIssuesApiFactory factory)
    : IClassFixture<SocialIssuesApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Hiran = Guid.NewGuid();
    private static readonly Guid Sector14 = Guid.NewGuid();
    private static readonly Guid AreaAdminId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member() => factory.CreateClientAs(
        Guid.NewGuid(), TenantId, [Roles.Member], [PermissionKeys.MembersRead]);

    private HttpClient AreaAdmin() => factory.CreateClientAs(
        AreaAdminId, TenantId, [Roles.Member, Roles.AreaAdmin],
        [PermissionKeys.MembersRead, PermissionKeys.SocialIssuesApprove], Hiran);

    private async Task<Guid> SubmitAsync(Guid? locality)
    {
        var response = await Member().PostAsJsonAsync("/v1/social-issues", new
        {
            title = "Streetlight out by the temple",
            description = "It has been dark for a week.",
            category = "Safety",
            locality = (string?)null,
            submitNow = true,
            localityId = locality?.ToString(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> ApproveAsync(Guid id) =>
        AreaAdmin().PostAsJsonAsync(
            $"/v1/social-issues/{id}/status", new { status = "DecisionPending", reason = (string?)null });

    [Fact]
    public async Task An_Area_Admin_s_queue_is_their_own_locality_s_issues()
    {
        var own = await SubmitAsync(Hiran);
        var elsewhere = await SubmitAsync(Sector14);
        var noLocality = await SubmitAsync(null);

        var queue = (await AreaAdmin().GetFromJsonAsync<JsonElement>("/v1/social-issues/approval-queue"))
            .EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        queue.Should().Contain(own);
        queue.Should().NotContain(elsewhere);
        queue.Should().NotContain(noLocality);
    }

    [Fact]
    public async Task With_no_Panch_an_Area_Admin_reviews_their_own_locality_and_nowhere_else()
    {
        var own = await SubmitAsync(Hiran);
        var elsewhere = await SubmitAsync(Sector14);

        (await ApproveAsync(own)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Not yet public, and not theirs to review: not found, not forbidden.
        (await ApproveAsync(elsewhere)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AreaAdmin().GetAsync($"/v1/social-issues/{elsewhere}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_Area_Admin_on_the_Panch_judges_for_the_whole_Samaaj()
    {
        await factory.WithDbContextAsync(async db =>
        {
            db.PanelMembers.Add(new PanelMember(TenantId, AreaAdminId));
            await db.SaveChangesAsync();

            return 0;
        });

        var elsewhere = await SubmitAsync(Sector14);

        (await ApproveAsync(elsewhere)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
