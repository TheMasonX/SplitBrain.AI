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
    ///   Failed     -> atomically reclaim slot and retry; throws if slot cannot be reclaimed
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

            if (existing?.State == IdempotencyState.Failed)
            {
                // Atomically remove the failed entry so we can re-reserve the slot.
                // If another concurrent retry removed it first, TryRemove returns false
                // and our subsequent TryReserve will see the slot the winner already
                // claimed, causing us to throw rather than execute twice.
                cache.TryRemove(key);

                if (!cache.TryReserve(key, DefaultTtl, out _))
                    throw new InvalidOperationException(
                        $"Could not reclaim failed idempotency slot for key '{key}'. " +
                        "A concurrent retry may already be in progress.");

                // Fall through: we now own the Processing reservation.
            }
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
