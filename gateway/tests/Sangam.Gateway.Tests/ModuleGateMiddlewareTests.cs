using System.Net;
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
        string? moduleKey, ResolvedTenant? tenant)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/v1/pathshala/classes";
        context.Response.Body = new MemoryStream();

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
    public async Task A_module_route_reached_without_a_resolved_Samaaj_is_refused()
    {
        // Cannot be checked, so it is not let through unchecked.
        var (statusCode, reached) = await InvokeAsync("Pathshala", tenant: null);

        statusCode.Should().Be((int)HttpStatusCode.NotFound);
        reached.Should().BeFalse();
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
