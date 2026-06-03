using FluentAssertions;
using NUnit.Framework;
using Orchestrator.Mcp.Idempotency;

namespace Orchestrator.Tests.Idempotency;

[TestFixture]
public class IdempotencyCacheTests
{
    private InMemoryIdempotencyCache _cache = null!;

    [SetUp]
    public void SetUp() => _cache = new InMemoryIdempotencyCache();

    private static IdempotencyEntry MakeEntry(string key, IdempotencyState state, TimeSpan? ttl = null) =>
        new()
        {
            Key = key,
            CreatedAt = DateTimeOffset.UtcNow,
            Ttl = ttl ?? TimeSpan.FromMinutes(5),
            State = state,
            Result = state == IdempotencyState.Completed ? "result" : null
        };

    [Test]
    public async Task GetAsync_WhenKeyNotFound_ReturnsNull()
    {
        var result = await _cache.GetAsync("missing");
        result.Should().BeNull();
    }

    [Test]
    public async Task SetAndGetAsync_ReturnsStoredEntry()
    {
        var entry = MakeEntry("k1", IdempotencyState.Completed);
        await _cache.SetAsync(entry);

        var result = await _cache.GetAsync("k1");

        result.Should().NotBeNull();
        result!.Key.Should().Be("k1");
        result.State.Should().Be(IdempotencyState.Completed);
    }

    [Test]
    public async Task GetAsync_WhenEntryExpired_ReturnsNull()
    {
        var expired = new IdempotencyEntry
        {
            Key = "expired",
            CreatedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10),
            Ttl = TimeSpan.FromMinutes(5),
            State = IdempotencyState.Completed
        };
        await _cache.SetAsync(expired);

        var result = await _cache.GetAsync("expired");

        result.Should().BeNull();
    }

    [Test]
    public async Task GetAsync_WhenEntryProcessing_ReturnsEntry()
    {
        var entry = MakeEntry("proc", IdempotencyState.Processing);
        await _cache.SetAsync(entry);

        var result = await _cache.GetAsync("proc");

        result.Should().NotBeNull();
        result!.State.Should().Be(IdempotencyState.Processing);
    }

    [Test]
    public async Task SetAsync_OverwritesExistingEntry()
    {
        var original = MakeEntry("k", IdempotencyState.Processing);
        var updated = MakeEntry("k", IdempotencyState.Completed);

        await _cache.SetAsync(original);
        await _cache.SetAsync(updated);

        var result = await _cache.GetAsync("k");
        result!.State.Should().Be(IdempotencyState.Completed);
    }

    [Test]
    public async Task RemoveExpiredAsync_CleansUpExpiredEntries()
    {
        var active = MakeEntry("active", IdempotencyState.Completed);
        var expired = new IdempotencyEntry
        {
            Key = "old",
            CreatedAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(2),
            Ttl = TimeSpan.FromMinutes(5),
            State = IdempotencyState.Completed
        };

        await _cache.SetAsync(active);
        await _cache.SetAsync(expired);
        await _cache.RemoveExpiredAsync();

        (await _cache.GetAsync("active")).Should().NotBeNull();
        (await _cache.GetAsync("old")).Should().BeNull();
    }
}
