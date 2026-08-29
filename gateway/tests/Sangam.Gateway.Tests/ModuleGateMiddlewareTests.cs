using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Sangam.Gateway.Tenancy;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

namespace Sangam.Gateway.Tests;

public sealed class ModuleGateMiddlewareTests
{
    private static readonly ResolvedTenant WithPathshala = new(
        Guid.NewGuid(), "mahavir-samaj", "Active", ["Pathshala", "Boli"]);

    private static readonly ResolvedTenant WithoutPathshala = new(
        Guid.NewGuid(), "pune-samaj", "Active", ["Boli"]);

    private static async Task<(int StatusCode, bool ReachedService)> InvokeAsync(
        string? moduleKey, ResolvedTenant? tenant, bool authenticated = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/pathshala/classes";
        context.Response.Body = new MemoryStream();

        if (authenticated)
        {
            // A ClaimsIdentity with an authentication type is authenticated and
            // one without is not. That single bit is what the gate branches on
            // when no Samaaj was resolved.
            context.User = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer"));
        }

        if (tenant is not null)
        {
            context.Items[TenantResolutionMiddleware.TenantItemKey] = tenant;
        }

        context.Features.Set<IReverseProxyFeature>(new FakeProxyFeature(moduleKey));

        var reached = false;

        var middleware = new ModuleGateMiddleware(
            _ =>
            {
                reached = true;
                return Task.CompletedTask;
            },
            NullLogger<ModuleGateMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        return (context.Response.StatusCode, reached);
    }

    [Fact]
    public async Task A_route_with_no_module_key_is_never_gated()
    {
        // Identity, audit and notifications are platform infrastructure: a
        // Samaaj cannot switch off the ability to log in.
        var (_, reached) = await InvokeAsync(moduleKey: null, WithoutPathshala);

        reached.Should().BeTrue();
    }

    [Fact]
    public async Task A_module_the_Samaaj_runs_is_passed_through()
    {
        var (_, reached) = await InvokeAsync("Pathshala", WithPathshala);

        reached.Should().BeTrue();
    }

    [Fact]
    public async Task A_module_the_Samaaj_has_switched_off_answers_404_rather_than_403()
    {
        var (statusCode, reached) = await InvokeAsync("Pathshala", WithoutPathshala);

        // A Samaaj without a Pathshala should be indistinguishable from a
        // platform that has no Pathshala feature at all.
        statusCode.Should().Be((int)HttpStatusCode.NotFound);
        reached.Should().BeFalse();
    }

    [Fact]
    public async Task Module_keys_are_matched_case_insensitively()
    {
        var (_, reached) = await InvokeAsync("pathshala", WithPathshala);

        reached.Should().BeTrue();
    }

    [Fact]
    public async Task An_authenticated_caller_with_no_Samaaj_is_refused()
    {
        // A Super Admin, who belongs to the platform rather than to a Samaaj,
        // and who has not named one with X-Tenant-Override-Id. Cannot be
        // checked, so it is not let through unchecked.
        var (statusCode, reached) = await InvokeAsync("Pathshala", tenant: null);

        statusCode.Should().Be((int)HttpStatusCode.NotFound);
        reached.Should().BeFalse();
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_told_to_authenticate_not_that_the_route_is_missing()
    {
        var (statusCode, reached) = await InvokeAsync(
            "Pathshala", tenant: null, authenticated: false);

        // Specifically not 404. Both portals renew an expired access token on a
        // 401 and retry the request; a 404 sails past that straight to the
        // screen. With 404 here, every module-gated screen printed "No such
        // endpoint." from fifteen minutes after sign-in and never recovered,
        // because nothing on those screens could produce the 401 that would
        // have renewed the token.
        statusCode.Should().Be((int)HttpStatusCode.Unauthorized);
        statusCode.Should().NotBe((int)HttpStatusCode.NotFound);
        reached.Should().BeFalse();
    }

    [Fact]
    public async Task The_401_says_the_same_thing_whichever_module_was_asked_for()
    {
        // The gate conceals no less than it did before: with no Samaaj resolved
        // there is nothing to tell apart, so an unauthenticated caller learns
        // only that the route needs a token - which is true of every route
        // behind the gateway.
        var one = await InvokeAsync("Boli", tenant: null, authenticated: false);
        var other = await InvokeAsync("Pathshala", tenant: null, authenticated: false);

        one.StatusCode.Should().Be(other.StatusCode);
    }

    private sealed class FakeProxyFeature(string? moduleKey) : IReverseProxyFeature
    {
        public RouteModel Route { get; } = new(
            new RouteConfig
            {
                RouteId = "test",
                ClusterId = "test",
                Metadata = moduleKey is null
                    ? null
                    : new Dictionary<string, string> { [ModuleGateMiddleware.ModuleMetadataKey] = moduleKey },
            },
            cluster: null,
            HttpTransformer.Default);

        public ClusterModel Cluster { get; set; } = null!;

        public IReadOnlyList<DestinationState> AllDestinations { get; set; } = [];

        public IReadOnlyList<DestinationState> AvailableDestinations { get; set; } = [];

        public DestinationState? ProxiedDestination { get; set; }
    }
}
