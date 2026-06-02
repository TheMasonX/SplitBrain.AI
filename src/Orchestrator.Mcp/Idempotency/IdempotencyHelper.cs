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

        // Read existing entry (if any)
        var existing = await cache.GetAsync(key, ct);
        if (existing is not null)
        {
            if (existing.State == IdempotencyState.Completed)
                return (string)existing.Result!;

            if (existing.State == IdempotencyState.Processing)
                throw new InvalidOperationException($"A request with idempotency key '{key}' is already being processed.");

            // If previous attempt failed, we'll attempt to reclaim the key by overwriting it below.
        }

        // Reserve slot by marking as Processing
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
