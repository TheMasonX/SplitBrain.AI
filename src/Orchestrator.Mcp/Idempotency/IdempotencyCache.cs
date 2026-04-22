using System.Collections.Concurrent;

namespace Orchestrator.Mcp.Idempotency;

public enum IdempotencyState
{
    Processing,
    Completed,
    Failed
}

public record IdempotencyEntry
{
    public required string Key { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required TimeSpan Ttl { get; init; }
    public IdempotencyState State { get; init; }
    public object? Result { get; init; }
}

/// <summary>
/// In-memory idempotency cache for MCP tool call deduplication.
/// - Key exists + Completed → return cached result (no re-execution)
/// - Key exists + Processing → caller should return 409 Conflict
/// - Key not found → mark Processing, execute, mark Completed/Failed
/// TTL expiration is lazy (on Get) plus periodic cleanup via RemoveExpiredAsync.
/// </summary>
public interface IIdempotencyCache
{
    Task<IdempotencyEntry?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(IdempotencyEntry entry, CancellationToken ct = default);
    Task RemoveExpiredAsync(CancellationToken ct = default);
}

public sealed class InMemoryIdempotencyCache : IIdempotencyCache
{
    private readonly ConcurrentDictionary<string, IdempotencyEntry> _cache = new();

    public Task<IdempotencyEntry?> GetAsync(string key, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (DateTimeOffset.UtcNow - entry.CreatedAt > entry.Ttl)
            {
                _cache.TryRemove(key, out _);
                return Task.FromResult<IdempotencyEntry?>(null);
            }
            return Task.FromResult<IdempotencyEntry?>(entry);
        }
        return Task.FromResult<IdempotencyEntry?>(null);
    }

    public Task SetAsync(IdempotencyEntry entry, CancellationToken ct = default)
    {
        _cache[entry.Key] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, entry) in _cache)
        {
            if (now - entry.CreatedAt > entry.Ttl)
                _cache.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }
}
