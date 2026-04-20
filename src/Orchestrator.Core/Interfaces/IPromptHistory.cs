using Orchestrator.Core.Enums;
using Orchestrator.Core.Models;

namespace Orchestrator.Core.Interfaces;

/// <summary>
/// Stores the last N prompts (per §11.2).
/// Thread-safe ring buffer — oldest entries evicted once capacity is reached.
/// </summary>
public interface IPromptHistory
{
    /// <summary>Adds a new prompt to the history and returns its assigned <see cref="PromptEntry.Id"/>.</summary>
    string Add(string prompt, TaskType taskType, string nodeId);

    /// <summary>
    /// Completes an existing entry — called after the inference response arrives.
    /// </summary>
    void Complete(string id, string response, bool success);

    /// <summary>Returns entries in reverse-chronological order (newest first).</summary>
    IReadOnlyList<PromptEntry> GetRecent(int count = 50);
}
