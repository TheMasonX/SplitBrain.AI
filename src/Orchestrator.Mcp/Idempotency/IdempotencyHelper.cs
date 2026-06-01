namespace Orchestrator.Mcp.Idempotency;

/// <summary>
/// Encapsulates the Processing -> Completed/Failed idempotency lifecycle
/// so individual tool methods stay concise.
/// </summary>
internal static class IdempotencyHelper
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// If <paramref name="key"/> is null, invokes <paramref name="execute"/> directly.
    /// Otherwise implements the full idempotency lifecycle using atomic reservation:
    ///   Completed  -> return cached result immediately
    ///   Processing -> throw InvalidOperationException (duplicate in-flight)
    ///   Missing    -> atomically reserve as Processing, execute, mark Completed or Failed
    /// </summary>
    internal static async Task<string> ExecuteAsync(
        IIdempotencyCache cache,
        string? key,
        Func<Task<string>> execute,
        CancellationToken ct)
    {
        if (key is null)
            return await execute();

        // Atomic reservation: either we claim the slot or get the existing entry.
        // This closes the race window where two concurrent requests could both
        // see null from GetAsync and both proceed to execute.
        if (!cache.TryReserve(key, DefaultTtl, out var existing))
        {
            // Someone else holds this key -- check what state they're in.
            if (existing?.State == IdempotencyState.Completed)
                return (string)existing.Result!;
            if (existing?.State == IdempotencyState.Processing)
                throw new InvalidOperationException($"A request with idempotency key '{key}' is already being processed.");

            // Failed state: allow retry by re-attempting reservation.
            // The failed entry may have expired or we can fall through to execute.
            // For safety, treat as a new execution attempt.
        }

        // We own the Processing reservation -- execute and update the entry.
        try
        {
            var result = await execute();
            await cache.UpdateAsync(new IdempotencyEntry
            {
                Key = key,
                CreatedAt = DateTimeOffset.UtcNow,
                Ttl = DefaultTtl,
                State = IdempotencyState.Completed,
                Result = result
            }, ct);
            return result;
        }
        catch
        {
            await cache.UpdateAsync(new IdempotencyEntry
            {
                Key = key,
                CreatedAt = DateTimeOffset.UtcNow,
                Ttl = DefaultTtl,
                State = IdempotencyState.Failed
            }, ct);
            throw;
        }
    }
}
