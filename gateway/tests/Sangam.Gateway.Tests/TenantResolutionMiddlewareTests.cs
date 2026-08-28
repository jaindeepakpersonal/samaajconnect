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
    private static readonly Guid MahavirId = Guid.NewGuid();

    private static readonly ResolvedTenant Mahavir = new(
        MahavirId, "mahavir-samaj", "Active", ["Pathshala"]);

    private readonly ITenantResolver _resolver = Substitute.For<ITenantResolver>();
    private IHost _host = null!;

    private ClaimsPrincipal _user = new(new ClaimsIdentity());

    public async Task InitializeAsync()
    {
        _resolver.ResolveAsync(MahavirId, Arg.Any<CancellationToken>()).Returns(Mahavir);

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
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

    private HttpClient Client() => _host.GetTestClient();

    /// <summary>Signs the caller in with a tenant claim, as a real token would carry.</summary>
    private void SignInTo(Guid? tenantId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };

        if (tenantId is { } id)
        {
            claims.Add(new Claim(TenantResolutionMiddleware.TenantClaimType, id.ToString()));
        }

        claims.AddRange(roles.Select(role => new Claim("role", role)));

        _user = new ClaimsPrincipal(new ClaimsIdentity(
            claims, authenticationType: "Test", nameType: ClaimTypes.NameIdentifier, roleType: "role"));
    }

    [Fact]
    public async Task A_token_naming_a_Samaaj_becomes_a_tenant_header_for_the_service_behind_it()
    {
        SignInTo(MahavirId, "Member");

        var body = await Client().GetStringAsync("/v1/members");

        body.Should().Contain(MahavirId.ToString());
        body.Should().Contain("mahavir-samaj");
    }

    [Fact]
    public async Task An_anonymous_request_reaches_the_service_with_no_tenant()
    {
        // Login, registration and the Samaaj directory all live here.
        var body = await Client().GetStringAsync("/v1/identity/login");

        body.Should().Contain("\"tenantId\":\"\"");
    }

    [Fact]
    public async Task A_Super_Admin_with_no_tenant_claim_is_not_forced_into_one()
    {
        // A platform account belongs to no Samaaj.
        SignInTo(tenantId: null, "SuperAdmin");

        var body = await Client().GetStringAsync("/v1/identity/tenants");

        body.Should().Contain("\"tenantId\":\"\"");
    }

    [Fact]
    public async Task A_client_supplied_tenant_header_is_stripped_and_replaced()
    {
        SignInTo(MahavirId, "Member");

        var client = Client();
        var forged = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TenantResolutionMiddleware.TenantHeader, forged.ToString());

        var body = await client.GetStringAsync("/v1/members");

        // Services treat this header as a gateway-issued fact, so a caller must
        // never be able to choose their own Samaaj with it.
        body.Should().NotContain(forged.ToString());
        body.Should().Contain(MahavirId.ToString());
    }

    [Fact]
    public async Task A_client_supplied_tenant_header_is_stripped_on_an_anonymous_request_too()
    {
        var client = Client();
        var forged = Guid.NewGuid();
        client.DefaultRequestHeaders.Add(TenantResolutionMiddleware.TenantHeader, forged.ToString());

        var body = await client.GetStringAsync("/v1/identity/login");

        body.Should().NotContain(forged.ToString());
    }

    [Fact]
    public async Task A_token_for_a_Samaaj_that_no_longer_exists_is_refused()
    {
        var gone = Guid.NewGuid();
        _resolver.ResolveAsync(gone, Arg.Any<CancellationToken>()).Returns((ResolvedTenant?)null);

        SignInTo(gone, "Member");

        var response = await Client().GetAsync("/v1/members");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_outliving_a_deactivation_is_refused()
    {
        var dormant = Guid.NewGuid();
        _resolver.ResolveAsync(dormant, Arg.Any<CancellationToken>())
            .Returns(new ResolvedTenant(dormant, "dormant", "Inactive", []));

        SignInTo(dormant, "Member");

        var response = await Client().GetAsync("/v1/members");

        // 403, not 404: the caller holds a valid token, so this is "your Samaaj
        // is not available", not "no such address".
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Identity_being_unreachable_is_a_502_and_not_a_403()
    {
        // Otherwise one service being down would look like every member's
        // Samaaj having been deactivated.
        _resolver.ResolveAsync(MahavirId, Arg.Any<CancellationToken>())
            .Returns<ResolvedTenant?>(_ => throw new HttpRequestException("identity is down"));

        SignInTo(MahavirId, "Member");

        var response = await Client().GetAsync("/v1/members");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task A_tenant_override_from_a_caller_who_is_not_a_Super_Admin_is_refused()
    {
        SignInTo(MahavirId, "SamaajAdmin");

        var client = Client();
        client.DefaultRequestHeaders.Add(
            TenantResolutionMiddleware.TenantOverrideHeader, Guid.NewGuid().ToString());

        var response = await client.GetAsync("/v1/members");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_override_from_an_anonymous_caller_is_refused()
    {
        var client = Client();
        client.DefaultRequestHeaders.Add(
            TenantResolutionMiddleware.TenantOverrideHeader, MahavirId.ToString());

        var response = await client.GetAsync("/v1/members");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Super_Admin_override_is_forwarded_as_an_override_not_as_a_plain_tenant()
    {
        SignInTo(tenantId: null, "SuperAdmin");

        var client = Client();
        client.DefaultRequestHeaders.Add(
            TenantResolutionMiddleware.TenantOverrideHeader, MahavirId.ToString());

        var body = await client.GetStringAsync("/v1/members");

        // Services treat it like a normal tenant, but the distinct header is
        // what lets them log that it happened.
        body.Should().Contain($"\"overrideId\":\"{MahavirId}\"");
        body.Should().Contain("\"tenantId\":\"\"");
    }

    [Fact]
    public async Task An_override_naming_a_Samaaj_that_does_not_exist_is_refused()
    {
        var gone = Guid.NewGuid();
        _resolver.ResolveAsync(gone, Arg.Any<CancellationToken>()).Returns((ResolvedTenant?)null);

        SignInTo(tenantId: null, "SuperAdmin");

        var client = Client();
        client.DefaultRequestHeaders.Add(TenantResolutionMiddleware.TenantOverrideHeader, gone.ToString());

        (await client.GetAsync("/v1/members")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_malformed_override_is_refused_rather_than_ignored()
    {
        SignInTo(tenantId: null, "SuperAdmin");

        var client = Client();
        client.DefaultRequestHeaders.Add(TenantResolutionMiddleware.TenantOverrideHeader, "not-a-guid");

        (await client.GetAsync("/v1/members")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
