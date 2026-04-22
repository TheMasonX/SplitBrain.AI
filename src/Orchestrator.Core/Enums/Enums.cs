namespace Orchestrator.Core.Enums;

public enum TaskType
{
    Autocomplete,
    Chat,
    Review,
    Refactor,
    TestGeneration,
    AgentStep,
    Embedding
}

public enum NodeStatus
{
    Healthy,
    Degraded,
    Unavailable
}
