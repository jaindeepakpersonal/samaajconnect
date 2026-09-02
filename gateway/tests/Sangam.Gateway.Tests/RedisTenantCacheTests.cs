using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sangam.Gateway.Tenancy;
using StackExchange.Redis;
using Xunit;

namespace Sangam.Gateway.Tests;

/// <summary>
/// The tenant cache, and the promise that it can fail without taking anything
/// with it.
/// </summary>
/// <remarks>
/// This class had no tests, and the claim in its own comments is the kind that
/// only holds if somebody checks: <b>"a cache failure degrades to a cache miss,
/// never to a failed request"</b>. Every request through the gateway resolves a
/// tenant, so a cache that threw instead of missing would turn a Redis blip
/// into a platform outage - which is precisely the second single point of
/// failure the null-object implementation beside it exists to avoid.
/// </remarks>
public sealed class RedisTenantCacheTests
{
    private static readonly Guid MahavirId = Guid.NewGuid();

    private static readonly ResolvedTenant Mahavir = new(
        MahavirId, "mahavir-samaj", "Active", ["pathshala"]);

    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly RedisTenantCache _cache;

    public RedisTenantCacheTests()
    {
        _redis.IsConnected.Returns(true);
        _redis.GetDatabase().Returns(_database);

        _cache = new RedisTenantCache(_redis, NullLogger<RedisTenantCache>.Instance);
    }

    private static string Key => $"gateway:tenant:{MahavirId}";

    // ---- Reading -----------------------------------------------------------

    [Fact]
    public async Task An_empty_key_is_a_miss_rather_than_a_negative_answer()
    {
        // The difference matters: a miss sends the resolver to identity-tenant
        // -service, and a cached negative tells it not to bother. Confusing the
        // two would make an unknown Samaaj permanently unknown for the life of
        // the entry.
        _database.StringGetAsync(Key, Arg.Any<CommandFlags>()).Returns(RedisValue.Null);

        (await _cache.GetAsync(MahavirId.ToString())).Should().BeNull();
    }

    [Fact]
    public async Task A_cached_Samaaj_comes_back_whole()
    {
        var payload = JsonSerializer.Serialize(Mahavir, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        _database.StringGetAsync(Key, Arg.Any<CommandFlags>()).Returns(payload);

        var found = await _cache.GetAsync(MahavirId.ToString());

        found.Should().NotBeNull();
        found!.Found.Should().BeTrue();

        // BeEquivalentTo, not Be. `ResolvedTenant` is a record whose
        // `EnabledModules` is an `IReadOnlyCollection<string>`, and a record's
        // generated equality compares that by reference - so a tenant that has
        // been through JSON is never `Be`-equal to the one it was serialised
        // from, however identical it looks. Worth knowing before writing the
        // next assertion like this one.
        found.Tenant.Should().BeEquivalentTo(Mahavir);
    }

    [Fact]
    public async Task A_cached_absence_is_reported_as_found_nothing()
    {
        _database.StringGetAsync(Key, Arg.Any<CommandFlags>()).Returns("-");

        var found = await _cache.GetAsync(MahavirId.ToString());

        found.Should().NotBeNull();
        found!.Found.Should().BeFalse();
        found.Tenant.Should().BeNull();
    }

    [Fact]
    public void A_Samaaj_can_never_be_mistaken_for_the_absence_marker()
    {
        // The marker is "-" because that is not valid JSON. If it were - say
        // "null", or an empty object - a serialised Samaaj could in principle
        // collide with it and a real Samaaj would read back as "no such
        // Samaaj", which the gateway answers as 404 for every request.
        var payload = JsonSerializer.Serialize(Mahavir, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        payload.Should().NotBe("-");

        var act = () => JsonSerializer.Deserialize<ResolvedTenant>("-");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public async Task A_read_that_throws_is_a_miss_and_not_an_error()
    {
        // The whole point. Redis is an optimisation; every request through the
        // gateway resolves a tenant, so a throw here is a platform outage.
        _database.StringGetAsync(Key, Arg.Any<CommandFlags>())
            .Returns<Task<RedisValue>>(_ => throw new RedisConnectionException(
                ConnectionFailureType.SocketFailure, "gone"));

        (await _cache.GetAsync(MahavirId.ToString())).Should().BeNull();
    }

    [Fact]
    public async Task Unreadable_cached_content_is_a_miss_rather_than_a_crash()
    {
        // A half-written value, or one left by an older shape of ResolvedTenant.
        // Falling back to asking identity-tenant-service is always safe.
        _database.StringGetAsync(Key, Arg.Any<CommandFlags>()).Returns("{ not json");

        (await _cache.GetAsync(MahavirId.ToString())).Should().BeNull();
    }

    [Fact]
    public async Task A_disconnected_Redis_is_not_asked_at_all()
    {
        _redis.IsConnected.Returns(false);

        (await _cache.GetAsync(MahavirId.ToString())).Should().BeNull();

        await _database.DidNotReceive().StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
    }

    // ---- Writing -----------------------------------------------------------

    [Fact]
    public async Task A_Samaaj_is_written_under_a_namespaced_key_with_the_ttl_it_was_given()
    {
        await _cache.SetAsync(MahavirId.ToString(), Mahavir, TimeSpan.FromSeconds(60));

        await _database.Received(1).StringSetAsync(
            Key,
            Arg.Is<RedisValue>(value => value.ToString().Contains("mahavir-samaj")),
            TimeSpan.FromSeconds(60),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task An_absent_Samaaj_is_written_as_the_marker()
    {
        await _cache.SetAsync(MahavirId.ToString(), null, TimeSpan.FromSeconds(60));

        await _database.Received(1).StringSetAsync(
            Key,
            "-",
            TimeSpan.FromSeconds(60),
            Arg.Any<bool>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task A_write_that_throws_does_not_fail_the_request()
    {
        // The request has already been resolved by the time anything is
        // cached. Failing here would throw away a correct answer over a
        // bookkeeping problem.
        _database.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
                Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns<Task<bool>>(_ => throw new RedisConnectionException(
                ConnectionFailureType.SocketFailure, "gone"));

        var act = async () => await _cache.SetAsync(MahavirId.ToString(), Mahavir, TimeSpan.FromSeconds(60));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_disconnected_Redis_is_not_written_to()
    {
        _redis.IsConnected.Returns(false);

        await _cache.SetAsync(MahavirId.ToString(), Mahavir, TimeSpan.FromSeconds(60));

        await _database.DidNotReceive().StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>());
    }
}

/// <summary>
/// The implementation used when Redis is not configured or was unreachable at
/// startup.
/// </summary>
public sealed class NullTenantCacheTests
{
    [Fact]
    public async Task Answers_every_read_as_a_miss_and_swallows_every_write()
    {
        // A null object rather than a nullable dependency, so no call site has
        // to remember that the cache might not be there.
        var cache = new NullTenantCache();

        (await cache.GetAsync("anything")).Should().BeNull();

        var act = async () => await cache.SetAsync("anything", null, TimeSpan.FromSeconds(60));

        await act.Should().NotThrowAsync();
    }
}
