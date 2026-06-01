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
/// - Key exists + Completed -> return cached result (no re-execution)
/// - Key exists + Processing -> caller should return 409 Conflict
/// - Key not found -> mark Processing, execute, mark Completed/Failed
/// TTL expiration is lazy (on Get/TryReserve) plus periodic cleanup via RemoveExpiredAsync.
/// </summary>
public interface IIdempotencyCache
{
    Task<IdempotencyEntry?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(IdempotencyEntry entry, CancellationToken ct = default);
    Task RemoveExpiredAsync(CancellationToken ct = default);

    /// <summary>
    /// Atomically reserves an idempotency slot. Returns true if this caller
    /// claimed the key (no prior entry or prior entry was expired). Returns
    /// false if the key is already held by another request, with the existing
    /// entry in <paramref name="existing"/>.
    /// </summary>
    bool TryReserve(string key, TimeSpan ttl, out IdempotencyEntry? existing);

    /// <summary>
    /// Updates an existing entry in-place (e.g. Processing -> Completed/Failed).
    /// Only succeeds if the key currently exists.
    /// </summary>
    Task UpdateAsync(IdempotencyEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes the entry for <paramref name="key"/> if it exists.
    /// Used to atomically clear a <see cref="IdempotencyState.Failed"/> entry
    /// before re-reserving the slot for a retry attempt.
    /// Returns true if the entry was present and removed.
    /// </summary>
    bool TryRemove(string key);
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

    /// <inheritdoc />
    public bool TryReserve(string key, TimeSpan ttl, out IdempotencyEntry? existing)
    {
        var now = DateTimeOffset.UtcNow;
        var newEntry = new IdempotencyEntry
        {
            Key = key,
            CreatedAt = now,
            Ttl = ttl,
            State = IdempotencyState.Processing
        };

        // Atomic: either we add a brand-new entry, or the key already exists.
        if (_cache.TryAdd(key, newEntry))
        {
            existing = null;
            return true; // This caller claimed the slot.
        }

        // Key already present -- check if it has expired.
        if (_cache.TryGetValue(key, out var current))
        {
            if (now - current.CreatedAt > current.Ttl)
            {
                // Expired entry: attempt to atomically replace it with our new reservation.
                // TryUpdate only succeeds if nobody else replaced it in the meantime.
                if (_cache.TryUpdate(key, newEntry, current))
                {
                    existing = null;
                    return true; // Reclaimed expired slot.
                }
                // Another thread reclaimed it first -- fall through to return the new value.
                _cache.TryGetValue(key, out current);
            }

            existing = current;
            return false; // Someone else holds this key.
        }

        // Extremely rare: key was removed between TryAdd and TryGetValue.
        // Retry the add once.
        if (_cache.TryAdd(key, newEntry))
        {
            existing = null;
            return true;
        }

        _cache.TryGetValue(key, out var raceEntry);
        existing = raceEntry;
        return false;
    }

    public Task SetAsync(IdempotencyEntry entry, CancellationToken ct = default)
    {
        _cache[entry.Key] = entry;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateAsync(IdempotencyEntry entry, CancellationToken ct = default)
    {
        // Only update if the key already exists (don't create orphan entries).
        _cache.AddOrUpdate(entry.Key, entry, (_, _) => entry);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool TryRemove(string key) => _cache.TryRemove(key, out _);

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
