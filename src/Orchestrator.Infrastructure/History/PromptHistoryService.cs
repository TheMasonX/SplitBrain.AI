using System.Collections.Concurrent;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Infrastructure.History;

/// <summary>
/// In-process ring buffer holding the last <see cref="Capacity"/> prompts.
/// </summary>
public sealed class PromptHistoryService : IPromptHistory
{
    private const int Capacity = 50;

    private readonly ConcurrentQueue<PromptEntry> _ring = new();
    private readonly ConcurrentDictionary<string, PromptEntry> _index = new();

    public string Add(string prompt, TaskType taskType, string nodeId)
    {
        var entry = new PromptEntry
        {
            Prompt   = prompt,
            TaskType = taskType,
            NodeId   = nodeId
        };

        _ring.Enqueue(entry);
        _index[entry.Id] = entry;

        // Evict oldest when over capacity
        while (_ring.Count > Capacity)
        {
            if (_ring.TryDequeue(out var evicted))
                _index.TryRemove(evicted.Id, out _);
        }

        return entry.Id;
    }

    public void Complete(string id, string response, bool success)
    {
        if (_index.TryGetValue(id, out var entry))
        {
            entry.Response = response;
            entry.Success  = success;
        }
    }

    public IReadOnlyList<PromptEntry> GetRecent(int count = 50) =>
        _ring
            .OrderByDescending(e => e.CreatedAt)
            .Take(count)
            .ToList()
            .AsReadOnly();
}
