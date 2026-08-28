using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Sangam.Gateway.Tenancy;
using Xunit;

namespace Sangam.Gateway.Tests;

public sealed class CachedTenantResolverTests
{
    private static readonly ResolvedTenant Mahavir = new(
        Guid.NewGuid(), "mahavir-samaj", "Active", ["Pathshala"]);

    private readonly ITenantCache _cache = Substitute.For<ITenantCache>();
    private readonly StubHandler _handler = new();
    private readonly CachedTenantResolver _resolver;

    public CachedTenantResolverTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();

        factory.CreateClient(CachedTenantResolver.HttpClientName).Returns(_ =>
            new HttpClient(_handler) { BaseAddress = new Uri("http://identity") });

        _resolver = new CachedTenantResolver(
            factory, _cache, Options.Create(new GatewayOptions { TenantCacheSeconds = 60 }));
    }

    [Fact]
    public async Task A_cache_hit_does_not_call_the_identity_service()
    {
        _cache.GetAsync("mahavir-samaj").Returns(new CachedTenantLookup(true, Mahavir));

        var tenant = await _resolver.ResolveAsync("mahavir-samaj");

        tenant.Should().Be(Mahavir);
        _handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_cached_negative_result_is_honoured_without_calling_identity()
    {
        // Otherwise a probed subdomain becomes an unthrottled stream of lookups.
        _cache.GetAsync("ghost").Returns(new CachedTenantLookup(false, null));

        (await _resolver.ResolveAsync("ghost")).Should().BeNull();
        _handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task A_cache_miss_fetches_from_identity_and_caches_the_result()
    {
        _cache.GetAsync("mahavir-samaj").Returns((CachedTenantLookup?)null);
        _handler.Respond(HttpStatusCode.OK, $$"""
            {"id":"{{Mahavir.Id}}","slug":"mahavir-samaj","status":"Active","enabledModules":["Pathshala"]}
            """);

        var tenant = await _resolver.ResolveAsync("mahavir-samaj");

        tenant.Should().NotBeNull();
        tenant!.Slug.Should().Be("mahavir-samaj");
        tenant.EnabledModules.Should().ContainSingle().Which.Should().Be("Pathshala");

        await _cache.Received(1).SetAsync("mahavir-samaj", Arg.Any<ResolvedTenant>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task A_404_from_identity_is_a_missing_Samaaj_and_is_cached_as_such()
    {
        _cache.GetAsync("ghost").Returns((CachedTenantLookup?)null);
        _handler.Respond(HttpStatusCode.NotFound, "{}");

        (await _resolver.ResolveAsync("ghost")).Should().BeNull();

        await _cache.Received(1).SetAsync("ghost", null, Arg.Any<TimeSpan>());
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Identity_being_unavailable_throws_rather_than_reporting_no_such_Samaaj(
        HttpStatusCode statusCode)
    {
        // "We could not check" must not be cached, or reported, as
        // "this Samaaj does not exist".
        _cache.GetAsync("mahavir-samaj").Returns((CachedTenantLookup?)null);
        _handler.Respond(statusCode, "{}");

        var act = async () => await _resolver.ResolveAsync("mahavir-samaj");

        await act.Should().ThrowAsync<HttpRequestException>();
        await _cache.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<ResolvedTenant?>(), Arg.Any<TimeSpan>());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private string _body = "{}";

        public int Calls { get; private set; }

        public void Respond(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
