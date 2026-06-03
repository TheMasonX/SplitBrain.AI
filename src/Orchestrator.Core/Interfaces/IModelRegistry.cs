using Orchestrator.Core.Enums;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

/// <summary>
/// Tracks model definitions and their availability per node.
/// </summary>
public interface IModelRegistry
{
    /// <summary>Returns all registered model definitions.</summary>
    IReadOnlyList<ModelDefinition> GetAllModels();

    /// <summary>Returns the definition for a specific model, or null.</summary>
    ModelDefinition? GetModel(string modelId);

    /// <summary>Returns models that support the given task type (primary or secondary).</summary>
    IReadOnlyList<ModelDefinition> GetModelsForTask(TaskType taskType);

    /// <summary>Returns models with an affinity for the given node.</summary>
    IReadOnlyList<ModelDefinition> GetModelsForNode(string nodeId);

    /// <summary>Registers or updates a model definition.</summary>
    void RegisterModel(ModelDefinition definition);

    /// <summary>Records which model IDs are currently available on a node.</summary>
    void UpdateNodeModels(string nodeId, IReadOnlyList<string> availableModelIds);

    /// <summary>Returns model IDs known to be available on a specific node.</summary>
    IReadOnlyList<string> GetAvailableModels(string nodeId);
}
