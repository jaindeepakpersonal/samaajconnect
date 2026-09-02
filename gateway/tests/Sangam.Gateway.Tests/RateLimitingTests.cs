using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sangam.Gateway.Tenancy;
using Xunit;

/// <summary>
/// The per-source limits on the endpoints an attacker guesses against.
/// </summary>
/// <remarks>
/// <para>
/// `scripts/smoke-through-gateway.sh` proves the limit fires against a running
/// stack, which is worth having and is also a four-hundred-request check that
/// says only "something refused eventually". These are the properties that
/// decide whether it refuses the right callers: that the two policies count
/// separately, that one source's attempts do not spend another's budget, and
/// that a refusal says nothing about how to pace the next one.
/// </para>
/// <para>
/// The limits are set for carrier-grade NAT - very large numbers of Indian
/// mobile subscribers share one address, and a Samaaj hall running a
/// registration drive shares one WiFi connection - so the numbers here are
/// small on purpose. What is under test is the partitioning, not the size of
/// the window.
/// </para>
/// </remarks>
public sealed class RateLimitingTests : IAsyncLifetime
{
    private const int CredentialLimit = 3;
    private const int RegistrationLimit = 2;

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
                        CredentialAttemptsPerWindow = CredentialLimit,
                        RegistrationsPerWindow = RegistrationLimit,
                    });

                    services.AddRouting();
                })
                .Configure(app =>
                {
                    // Stands in for the connection the real gateway sees. The
                    // limiter partitions on the remote address, so the tests
                    // set it per request.
                    app.Use(async (context, next) =>
                    {
                        if (context.Request.Headers.TryGetValue("X-Test-Ip", out var ip))
                        {
                            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip!);
                        }

                        await next();
                    });

                    app.UseRouting();
                    app.UseRateLimiter();

                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/login", () => Results.Ok("in"))
                            .RequireRateLimiting(RateLimiting.CredentialPolicy);

                        endpoints.MapGet("/register", () => Results.Ok("made"))
                            .RequireRateLimiting(RateLimiting.RegistrationPolicy);

                        endpoints.MapGet("/open", () => Results.Ok("always"));
                    });
                }))
            .StartAsync();
    }

    public async Task DisposeAsync() => await _host.StopAsync();

    private async Task<HttpResponseMessage> Get(string path, string ip)
    {
        var client = _host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, path);

        request.Headers.Add("X-Test-Ip", ip);

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Attempts_up_to_the_limit_are_allowed()
    {
        for (var attempt = 0; attempt < CredentialLimit; attempt++)
        {
            (await Get("/login", "203.0.113.1")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task The_attempt_past_the_limit_is_refused()
    {
        for (var attempt = 0; attempt < CredentialLimit; attempt++)
        {
            await Get("/login", "203.0.113.2");
        }

        (await Get("/login", "203.0.113.2")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_refusal_says_nothing_about_how_to_pace_the_next_attempt()
    {
        // No body, and no Retry-After. Telling a caller how long to wait, or
        // which limit they hit, is telling a script how to stay under it.
        for (var attempt = 0; attempt < CredentialLimit; attempt++)
        {
            await Get("/login", "203.0.113.3");
        }

        var refused = await Get("/login", "203.0.113.3");

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await refused.Content.ReadAsStringAsync()).Should().BeEmpty();
        refused.Headers.Contains("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task One_source_cannot_spend_another_source_s_budget()
    {
        // The partition is the whole design. If it were global, one script
        // would lock every member of every Samaaj out of signing in - which is
        // a denial of service handed to the attacker rather than taken from
        // them.
        for (var attempt = 0; attempt < CredentialLimit + 2; attempt++)
        {
            await Get("/login", "203.0.113.4");
        }

        (await Get("/login", "203.0.113.5")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_in_does_not_spend_the_registration_budget()
    {
        // Two policies, two counters. Sharing one would mean a Samaaj hall
        // signing its members up could be stopped by the sign-in attempts of
        // the people already registered on the same WiFi.
        for (var attempt = 0; attempt < CredentialLimit + 2; attempt++)
        {
            await Get("/login", "203.0.113.6");
        }

        (await Get("/register", "203.0.113.6")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Registration_has_its_own_lower_limit()
    {
        for (var attempt = 0; attempt < RegistrationLimit; attempt++)
        {
            (await Get("/register", "203.0.113.7")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await Get("/register", "203.0.113.7")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_route_with_no_policy_is_not_limited_at_all()
    {
        // The burst against sign-in must not take the rest of the platform
        // down with it - the same property the smoke script checks end to end.
        for (var attempt = 0; attempt < CredentialLimit + 5; attempt++)
        {
            await Get("/login", "203.0.113.8");
        }

        for (var attempt = 0; attempt < CredentialLimit + 5; attempt++)
        {
            (await Get("/open", "203.0.113.8")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
