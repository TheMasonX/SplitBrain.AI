using System.Collections.Concurrent;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Infrastructure.Registry;

/// <summary>
/// Thread-safe in-memory model registry. Tracks model definitions and
/// per-node model availability reported by health check updates.
/// </summary>
public sealed class InMemoryModelRegistry : IModelRegistry
{
    private readonly ConcurrentDictionary<string, ModelDefinition> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _nodeModels = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ModelDefinition> GetAllModels() =>
        _models.Values.ToList();

    public ModelDefinition? GetModel(string modelId) =>
        _models.GetValueOrDefault(modelId);

    public IReadOnlyList<ModelDefinition> GetModelsForTask(TaskType taskType) =>
        _models.Values
            .Where(m => m.PrimaryCapability == taskType || m.SecondaryCapabilities.Contains(taskType))
            .ToList();

    public IReadOnlyList<ModelDefinition> GetModelsForNode(string nodeId) =>
        _models.Values
            .Where(m => m.PreferredNodeIds.Contains(nodeId, StringComparer.OrdinalIgnoreCase))
            .ToList();

    public void RegisterModel(ModelDefinition definition) =>
        _models[definition.ModelId] = definition;

    public void UpdateNodeModels(string nodeId, IReadOnlyList<string> availableModelIds) =>
        _nodeModels[nodeId] = availableModelIds;

    public IReadOnlyList<string> GetAvailableModels(string nodeId) =>
        _nodeModels.TryGetValue(nodeId, out var list) ? list : [];
}
