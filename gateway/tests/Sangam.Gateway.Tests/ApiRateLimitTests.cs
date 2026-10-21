using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sangam.Gateway.Tenancy;
using Xunit;

/// <summary>
/// The general API limit: per signed-in account, per address only when nobody
/// is signed in, and never on the portal's own pages.
/// </summary>
/// <remarks>
/// The numbers are tiny so the window can be crossed in a test; what is under
/// test is who shares a budget. An account is identified the way the gateway
/// identifies one - the <c>sub</c> of a token authentication already validated -
/// stood in for here by a header the test sets.
/// </remarks>
public sealed class ApiRateLimitTests : IAsyncLifetime
{
    private const int PerAccount = 4;
    private const int PerAddress = 2;

    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddGatewayRateLimiting(new GatewayRateLimitOptions
                    {
                        WindowSeconds = 60,
                        ApiRequestsPerAccountPerWindow = PerAccount,
                        ApiRequestsPerAddressPerWindow = PerAddress,
                    });
                })
                .Configure(app =>
                {
                    // Stands in for the connection and for UseAuthentication.
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress =
                            IPAddress.Parse(context.Request.Headers["X-Test-Ip"].FirstOrDefault() ?? "198.51.100.1");

                        if (context.Request.Headers.TryGetValue("X-Test-User", out var user))
                        {
                            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user!)], "test"));
                        }

                        await next();
                    });

                    app.UseRateLimiter();
                    app.Run(context => context.Response.WriteAsync("ok"));
                }))
            .StartAsync();
    }

    public async Task DisposeAsync() => await _host.StopAsync();

    private async Task<HttpStatusCode> Get(string path, string ip, string? user = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Ip", ip);

        if (user is not null)
        {
            request.Headers.Add("X-Test-User", user);
        }

        return (await _host.GetTestClient().SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task A_signed_in_account_has_its_own_budget_and_the_one_past_it_is_refused()
    {
        for (var i = 0; i < PerAccount; i++)
        {
            (await Get("/v1/members", "198.51.100.10", "member-a")).Should().Be(HttpStatusCode.OK);
        }

        (await Get("/v1/members", "198.51.100.10", "member-a")).Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Members_behind_one_address_do_not_share_a_budget()
    {
        // Carrier-grade NAT, or a Samaaj hall's WiFi: one address, many people.
        for (var i = 0; i < PerAccount + 2; i++)
        {
            await Get("/v1/members", "198.51.100.20", "member-b");
        }

        (await Get("/v1/members", "198.51.100.20", "member-c")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_calls_are_counted_by_address_and_do_not_spend_an_accounts_budget()
    {
        for (var i = 0; i < PerAddress; i++)
        {
            (await Get("/v1/identity/tenants/directory", "198.51.100.30")).Should().Be(HttpStatusCode.OK);
        }

        (await Get("/v1/identity/tenants/directory", "198.51.100.30")).Should().Be(HttpStatusCode.TooManyRequests);
        (await Get("/v1/members", "198.51.100.30", "member-d")).Should().Be(HttpStatusCode.OK,
            "signing in moves a caller onto their own account's budget");
    }

    [Fact]
    public async Task The_portals_own_pages_and_assets_are_never_counted()
    {
        for (var i = 0; i < PerAddress + 5; i++)
        {
            (await Get("/timeline", "198.51.100.40")).Should().Be(HttpStatusCode.OK);
            (await Get("/main-ABC123.js", "198.51.100.40")).Should().Be(HttpStatusCode.OK);
        }
    }
}
