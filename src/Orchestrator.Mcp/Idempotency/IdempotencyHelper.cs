namespace Orchestrator.Mcp.Idempotency;

/// <summary>
/// Encapsulates the Processing → Completed/Failed idempotency lifecycle
/// so individual tool methods stay concise.
/// </summary>
internal static class IdempotencyHelper
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// If <paramref name="key"/> is null, invokes <paramref name="execute"/> directly.
    /// Otherwise implements the full idempotency lifecycle:
    ///   Completed  → return cached result immediately
    ///   Processing → throw InvalidOperationException (duplicate in-flight)
    ///   Missing    → mark Processing, execute, mark Completed or Failed
    /// </summary>
    internal static async Task<string> ExecuteAsync(
        IIdempotencyCache cache,
        string? key,
        Func<Task<string>> execute,
        CancellationToken ct)
    {
        if (key is null)
            return await execute();

        var existing = await cache.GetAsync(key, ct);
        if (existing?.State == IdempotencyState.Completed)
            return (string)existing.Result!;
        if (existing?.State == IdempotencyState.Processing)
            throw new InvalidOperationException($"A request with idempotency key '{key}' is already being processed.");

        await cache.SetAsync(new IdempotencyEntry
        {
            Key = key,
            CreatedAt = DateTimeOffset.UtcNow,
            Ttl = DefaultTtl,
            State = IdempotencyState.Processing
        }, ct);

        try
        {
            var result = await execute();
            await cache.SetAsync(new IdempotencyEntry
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
            await cache.SetAsync(new IdempotencyEntry
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
