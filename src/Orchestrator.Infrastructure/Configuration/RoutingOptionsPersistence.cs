using System.Text.Json;
using System.Text.Json.Serialization;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Infrastructure.Configuration;

/// <summary>
/// Persists routing settings to a json file consumable by configuration binding.
/// </summary>
public sealed class RoutingOptionsPersistence : IRoutingOptionsPersistence
{
    private readonly string _filePath;

    public RoutingOptionsPersistence(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppContext.BaseDirectory, "routing.json");
    }

    /// <summary>
    /// Saves fallback chains under the "Routing" section.
    /// </summary>
    public async Task SaveFallbackChainsAsync(
        IReadOnlyDictionary<string, List<string>> fallbackChains,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fallbackChains);

        var sanitized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, chain) in fallbackChains)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var normalizedSource = source.Trim();
            var normalizedTargets = chain
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .Where(t => !string.Equals(t, normalizedSource, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            sanitized[normalizedSource] = normalizedTargets;
        }

        var payload = new RoutingSettingsFile
        {
            Routing = new RoutingOptions
            {
                FallbackChains = sanitized
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        await AtomicJsonFileWriter.WriteAsync(_filePath, json, cancellationToken).ConfigureAwait(false);
    }

    private sealed class RoutingSettingsFile
    {
        public RoutingOptions Routing { get; init; } = new();
    }
}
