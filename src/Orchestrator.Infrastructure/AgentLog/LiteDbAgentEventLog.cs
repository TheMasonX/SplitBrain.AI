using System.Runtime.CompilerServices;
using LiteDB;
using Orchestrator.Core.Interfaces;

namespace Orchestrator.Infrastructure.AgentLog;

/// <summary>
/// LiteDB-backed append-only agent event log.
/// Each task's steps are stored as documents keyed by TaskId + StepIndex.
/// The database file defaults to %TEMP%\splitbrain-agent-events.db.
/// </summary>
public sealed class LiteDbAgentEventLog : IAgentEventLog, IDisposable
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<AgentStepDocument> _col;

    public LiteDbAgentEventLog(string? dbPath = null)
    {
        var path = dbPath ?? Path.Combine(Path.GetTempPath(), "splitbrain-agent-events.db");
        _db = new LiteDatabase(path);
        _col = _db.GetCollection<AgentStepDocument>("agent_steps");
        _col.EnsureIndex(x => x.TaskId);
    }

    public Task AppendAsync(AgentStepEvent step, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _col.Insert(AgentStepDocument.FromEvent(step));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<AgentStepEvent> ReplayAsync(
        string taskId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var docs = _col.Find(x => x.TaskId == taskId)
                       .OrderBy(x => x.StepIndex)
                       .ToList();

        foreach (var doc in docs)
        {
            ct.ThrowIfCancellationRequested();
            yield return doc.ToEvent();
            await Task.Yield();
        }
    }

    public Task<int> GetTotalTokensAsync(string taskId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var total = _col.Find(x => x.TaskId == taskId)
                        .Sum(x => x.TokensConsumed ?? 0);
        return Task.FromResult(total);
    }

    public void Dispose() => _db.Dispose();

    // -------------------------------------------------------------------------
    // Internal document model (LiteDB cannot serialize records with required init)
    // -------------------------------------------------------------------------

    private sealed class AgentStepDocument
    {
        public ObjectId Id { get; set; } = ObjectId.NewObjectId();
        public string TaskId { get; set; } = string.Empty;
        public int StepIndex { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string StepType { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string? ModelId { get; set; }
        public string? NodeId { get; set; }
        public int? TokensConsumed { get; set; }
        public double? LatencyMs { get; set; }

        public static AgentStepDocument FromEvent(AgentStepEvent e) => new()
        {
            TaskId = e.TaskId,
            StepIndex = e.StepIndex,
            Timestamp = e.Timestamp,
            StepType = e.StepType.ToString(),
            Summary = e.Summary,
            ModelId = e.ModelId,
            NodeId = e.NodeId,
            TokensConsumed = e.TokensConsumed,
            LatencyMs = e.LatencyMs
        };

        public AgentStepEvent ToEvent() => new()
        {
            TaskId = TaskId,
            StepIndex = StepIndex,
            Timestamp = Timestamp,
            StepType = Enum.Parse<AgentStepType>(StepType),
            Summary = Summary,
            ModelId = ModelId,
            NodeId = NodeId,
            TokensConsumed = TokensConsumed,
            LatencyMs = LatencyMs
        };
    }
}
