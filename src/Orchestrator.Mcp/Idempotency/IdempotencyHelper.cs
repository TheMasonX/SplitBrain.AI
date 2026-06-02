namespace Orchestrator.Mcp.Idempotency;

/// <summary>
/// Encapsulates the Processing -> Completed/Failed idempotency lifecycle
/// so individual tool methods stay concise.
/// </summary>
internal static class IdempotencyHelper
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    internal static async Task<string> ExecuteAsync(
        IIdempotencyCache cache,
        string? key,
        Func<Task<string>> execute,
        CancellationToken ct)
    {
        if (key is null)
            return await execute();

        if (!cache.TryReserve(key, DefaultTtl, out var existing))
        {
            if (existing?.State == IdempotencyState.Completed)
                return (string)existing.Result!;

            if (existing?.State == IdempotencyState.Processing)
                throw new InvalidOperationException($"A request with idempotency key '{key}' is already being processed.");

            if (existing?.State == IdempotencyState.Failed)
            {
                cache.TryRemove(key);

                if (!cache.TryReserve(key, DefaultTtl, out _))
                    throw new InvalidOperationException(
                        $"Could not reclaim failed idempotency slot for key '{key}'. " +
                        "A concurrent retry may already be in progress.");
            }
        }

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
