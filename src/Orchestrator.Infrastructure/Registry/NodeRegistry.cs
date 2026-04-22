using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Infrastructure.Registry;

/// <summary>
/// Thread-safe node registry with hot-reload via IOptionsMonitor.
/// Uses Interlocked.Exchange for atomic topology swaps — no Clear()+re-add race windows.
/// </summary>
public sealed class NodeRegistry : INodeRegistry, IDisposable
{
    private ConcurrentDictionary<string, NodeRegistration> _nodes = new();
    private readonly IInferenceNodeFactory _factory;
    private readonly IDisposable? _changeListener;
    private readonly string _configFilePath;

    /// <param name="configFilePath">Absolute path to nodes.json. Defaults to nodes.json beside the executable.</param>
    public NodeRegistry(
        IInferenceNodeFactory factory,
        IOptionsMonitor<NodeTopologyConfig> optionsMonitor,
        string? configFilePath = null)
    {
        _factory = factory;
        _configFilePath = configFilePath
            ?? Path.Combine(AppContext.BaseDirectory, "nodes.json");

        RebuildTopology(optionsMonitor.CurrentValue);

        _changeListener = optionsMonitor.OnChange((cfg, _) => RebuildTopology(cfg));
    }

    private void RebuildTopology(NodeTopologyConfig config)
    {
        var newDict = new ConcurrentDictionary<string, NodeRegistration>();

        foreach (var nodeConfig in config.Nodes.Where(n => n.Enabled))
        {
            // Reuse existing instance if config unchanged
            if (_nodes.TryGetValue(nodeConfig.NodeId, out var existing) && existing.Config == nodeConfig)
            {
                newDict[nodeConfig.NodeId] = existing;
            }
            else
            {
                // Dispose old node if replacing
                if (_nodes.TryGetValue(nodeConfig.NodeId, out var old))
                    _ = old.Node.DisposeAsync();

                var node = _factory.Create(nodeConfig);
                newDict[nodeConfig.NodeId] = new NodeRegistration { Config = nodeConfig, Node = node };
            }
        }

        // Atomic swap — no race window
        var oldDict = Interlocked.Exchange(ref _nodes, newDict);

        // Dispose removed nodes
        foreach (var (id, reg) in oldDict)
        {
            if (!newDict.ContainsKey(id))
                _ = reg.Node.DisposeAsync();
        }
    }

    public IReadOnlyList<NodeRegistration> GetAllNodes() => _nodes.Values.ToList();

    public IReadOnlyList<NodeRegistration> GetHealthyNodes() =>
        _nodes.Values.Where(n => n.LastHealth?.State == HealthState.Healthy).ToList();

    public IReadOnlyList<NodeRegistration> GetNodesByRole(NodeRole role) =>
        _nodes.Values.Where(n => n.Config.Role == role).ToList();

    public IReadOnlyList<NodeRegistration> GetNodesByTag(string tag) =>
        _nodes.Values.Where(n => n.Config.Tags.Contains(tag)).ToList();

    public NodeRegistration? GetNode(string nodeId) =>
        _nodes.TryGetValue(nodeId, out var reg) ? reg : null;

    public void RegisterNode(NodeConfiguration config)
    {
        var node = _factory.Create(config);
        var registration = new NodeRegistration { Config = config, Node = node };
        if (_nodes.TryGetValue(config.NodeId, out var old))
            _ = old.Node.DisposeAsync();
        _nodes[config.NodeId] = registration;
    }

    public void DeregisterNode(string nodeId)
    {
        if (_nodes.TryRemove(nodeId, out var reg))
            _ = reg.Node.DisposeAsync();
    }

    public void UpdateNodeHealth(string nodeId, NodeHealthStatus status)
    {
        if (_nodes.TryGetValue(nodeId, out var reg))
            reg.LastHealth = status;
    }

    public async Task SaveTopologyAsync(CancellationToken ct = default)
    {
        var config = new NodeTopologyConfig
        {
            Nodes = _nodes.Values.Select(r => r.Config).ToList()
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        });

        await File.WriteAllTextAsync(_configFilePath, json, ct);
        // IOptionsMonitor picks up the change via reloadOnChange: true
    }

    public void Dispose()
    {
        _changeListener?.Dispose();
        foreach (var reg in _nodes.Values)
            _ = reg.Node.DisposeAsync();
    }
}
