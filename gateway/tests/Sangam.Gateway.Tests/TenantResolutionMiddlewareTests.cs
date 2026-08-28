using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Sangam.Gateway.Tenancy;
using Xunit;

namespace Sangam.Gateway.Tests;

/// <summary>
/// Exercises the middleware against a terminal endpoint that echoes the headers
/// it received, which is exactly what a downstream service would see.
/// </summary>
public sealed class TenantResolutionMiddlewareTests : IAsyncLifetime
{
    private static readonly ResolvedTenant Mahavir = new(
        Guid.NewGuid(), "mahavir-samaj", "Active", ["Pathshala"]);

    private readonly ITenantResolver _resolver = Substitute.For<ITenantResolver>();
    private IHost _host = null!;

    private ClaimsPrincipal _user = new(new ClaimsIdentity());

    public async Task InitializeAsync()
    {
        _resolver.ResolveAsync("mahavir-samaj", Arg.Any<CancellationToken>()).Returns(Mahavir);

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.Configure<GatewayOptions>(o =>
                    {
                        o.ApexHosts = ["samaajconnect.com", "localhost"];
                        o.AdminHost = "admin.samaajconnect.com";
                    });
                    services.AddSingleton<HostSlugExtractor>();
                    services.AddSingleton(_resolver);
                    services.AddLogging();
                })
                .Configure(app =>
                {
                    // Stands in for the real authentication middleware.
                    app.Use(async (context, next) =>
                    {
                        context.User = _user;
                        await next();
                    });

                    app.UseMiddleware<TenantResolutionMiddleware>();

                    app.Run(context => context.Response.WriteAsJsonAsync(new
                    {
                        tenantId = context.Request.Headers[TenantResolutionMiddleware.TenantHeader].ToString(),
                        tenantSlug = context.Request.Headers[TenantResolutionMiddleware.TenantSlugHeader].ToString(),
                        overrideId = context.Request.Headers[TenantResolutionMiddleware.TenantOverrideHeader].ToString(),
                    }));
                }))
            .StartAsync();
    }

    public async Task DisposeAsync() => await _host.StopAsync();

    private HttpClient Client(string host)
    {
        var client = _host.GetTestClient();
        client.BaseAddress = new Uri("http://" + host);

        return client;
    }

    private void SignInAs(params string[] roles) =>
        _user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                .. roles.Select(role => new Claim("role", role)),
            ],
            authenticationType: "Test",
            nameType: ClaimTypes.NameIdentifier,
            roleType: "role"));

    [Fact]
    public async Task A_Samaaj_subdomain_becomes_a_tenant_header_for_the_service_behind_it()
    {
        var body = await Client("mahavir-samaj.samaajconnect.com").GetStringAsync("/v1/identity/me");

        body.Should().Contain(Mahavir.Id.ToString());
        body.Should().Contain("mahavir-samaj");
    }

    [Fact]
    public async Task A_client_supplied_tenant_header_is_stripped_and_replaced()
    {
        var client = Client("mahavir-samaj.samaajconnect.com");
        var forged = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TenantResolutionMiddleware.TenantHeader, forged.ToString());

        var body = await client.GetStringAsync("/v1/identity/me");

        // Downstream services treat this header as a gateway-issued fact, so a
        // caller must never be able to choose their own Samaaj with it.
        body.Should().NotContain(forged.ToString());
        body.Should().Contain(Mahavir.Id.ToString());
    }

    [Fact]
    public async Task A_client_supplied_tenant_header_is_stripped_even_on_an_apex_host()
    {
        var client = Client("samaajconnect.com");
        var forged = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TenantResolutionMiddleware.TenantHeader, forged.ToString());

        var body = await client.GetStringAsync("/v1/identity/login");

        body.Should().NotContain(forged.ToString());
    }

    [Fact]
    public async Task An_apex_host_reaches_the_service_with_no_tenant_at_all()
    {
        var body = await Client("samaajconnect.com").GetStringAsync("/v1/identity/register");

        body.Should().Contain("\"tenantId\":\"\"");
    }

    [Fact]
    public async Task An_unknown_Samaaj_is_refused_before_any_service_sees_the_request()
    {
        _resolver.ResolveAsync("ghost", Arg.Any<CancellationToken>()).Returns((ResolvedTenant?)null);

        var response = await Client("ghost.samaajconnect.com").GetAsync("/v1/identity/me");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_inactive_Samaaj_is_also_a_404_rather_than_a_distinguishable_403()
    {
        _resolver.ResolveAsync("dormant", Arg.Any<CancellationToken>())
            .Returns(new ResolvedTenant(Guid.NewGuid(), "dormant", "Inactive", []));

        var response = await Client("dormant.samaajconnect.com").GetAsync("/v1/identity/me");

        // Probing subdomains must not reveal which Samaaj exist but are off.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Identity_being_unreachable_is_a_502_and_not_a_404()
    {
        _resolver.ResolveAsync("mahavir-samaj", Arg.Any<CancellationToken>())
            .Returns<ResolvedTenant?>(_ => throw new HttpRequestException("identity is down"));

        var response = await Client("mahavir-samaj.samaajconnect.com").GetAsync("/v1/identity/me");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task A_tenant_override_from_a_Samaaj_subdomain_is_refused()
    {
        SignInAs("SuperAdmin");

        var client = Client("mahavir-samaj.samaajconnect.com");
        client.DefaultRequestHeaders.Add(
            TenantResolutionMiddleware.TenantOverrideHeader, Guid.NewGuid().ToString());

        var response = await client.GetAsync("/v1/identity/me");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_tenant_override_from_a_caller_who_is_not_a_Super_Admin_is_refused()
    {
        SignInAs("SamaajAdmin");

        var client = Client("admin.samaajconnect.com");
        client.DefaultRequestHeaders.Add(
            TenantResolutionMiddleware.TenantOverrideHeader, Guid.NewGuid().ToString());

        var response = await client.GetAsync("/v1/identity/me");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Super_Admin_override_from_the_admin_console_is_passed_through()
    {
        SignInAs("SuperAdmin");

        var target = Guid.NewGuid();
        var client = Client("admin.samaajconnect.com");
        client.DefaultRequestHeaders.Add(TenantResolutionMiddleware.TenantOverrideHeader, target.ToString());

        var body = await client.GetStringAsync("/v1/identity/me");

        body.Should().Contain(target.ToString());
    }

    [Fact]
    public async Task A_malformed_override_is_rejected_rather_than_ignored()
    {
        SignInAs("SuperAdmin");

        var client = Client("admin.samaajconnect.com");
        client.DefaultRequestHeaders.Add(TenantResolutionMiddleware.TenantOverrideHeader, "not-a-guid");

        var response = await client.GetAsync("/v1/identity/me");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
