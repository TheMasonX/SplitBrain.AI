namespace Orchestrator.Core.Enums;

public enum TaskType
{
    Autocomplete,
    Chat,
    Review,
    Refactor,
    TestGeneration,
    AgentStep
}

public enum NodeStatus
{
    Healthy,
    Degraded,
    Unavailable
}
