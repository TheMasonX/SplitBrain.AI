using FluentAssertions;
using NUnit.Framework;
using Orchestrator.Core.Interfaces;
using Orchestrator.Infrastructure.AgentLog;

namespace Orchestrator.Tests.AgentLog;

[TestFixture]
public class LiteDbAgentEventLogTests
{
    private string _dbPath = null!;
    private LiteDbAgentEventLog _log = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"splitbrain-test-{Guid.NewGuid():N}.db");
        _log = new LiteDbAgentEventLog(_dbPath);
    }

    [TearDown]
    public void TearDown()
    {
        _log.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static AgentStepEvent MakeEvent(string taskId, int stepIndex, AgentStepType type, int? tokens = null) =>
        new()
        {
            TaskId         = taskId,
            StepIndex      = stepIndex,
            Timestamp      = DateTimeOffset.UtcNow,
            StepType       = type,
            Summary        = $"Step {stepIndex}: {type}",
            TokensConsumed = tokens
        };

    [Test]
    public async Task AppendAndReplay_ReturnsSameEvent()
    {
        var ev = MakeEvent("task1", 0, AgentStepType.Init);
        await _log.AppendAsync(ev);

        var replayed = new List<AgentStepEvent>();
        await foreach (var e in _log.ReplayAsync("task1"))
            replayed.Add(e);

        replayed.Should().ContainSingle();
        replayed[0].TaskId.Should().Be("task1");
        replayed[0].StepType.Should().Be(AgentStepType.Init);
    }

    [Test]
    public async Task ReplayAsync_ReturnsStepsInStepIndexOrder()
    {
        await _log.AppendAsync(MakeEvent("t", 2, AgentStepType.Review));
        await _log.AppendAsync(MakeEvent("t", 0, AgentStepType.Init));
        await _log.AppendAsync(MakeEvent("t", 1, AgentStepType.Plan));

        var replayed = new List<AgentStepEvent>();
        await foreach (var e in _log.ReplayAsync("t"))
            replayed.Add(e);

        replayed.Select(e => e.StepIndex).Should().BeInAscendingOrder();
    }

    [Test]
    public async Task ReplayAsync_IsolatesTaskIds()
    {
        await _log.AppendAsync(MakeEvent("task-A", 0, AgentStepType.Init));
        await _log.AppendAsync(MakeEvent("task-B", 0, AgentStepType.Init));

        var taskASteps = new List<AgentStepEvent>();
        await foreach (var e in _log.ReplayAsync("task-A"))
            taskASteps.Add(e);

        taskASteps.Should().ContainSingle(e => e.TaskId == "task-A");
    }

    [Test]
    public async Task GetTotalTokensAsync_SumsTokensConsumed()
    {
        await _log.AppendAsync(MakeEvent("t", 0, AgentStepType.Plan, tokens: 100));
        await _log.AppendAsync(MakeEvent("t", 1, AgentStepType.Implement, tokens: 250));
        await _log.AppendAsync(MakeEvent("t", 2, AgentStepType.Review, tokens: null));

        var total = await _log.GetTotalTokensAsync("t");

        total.Should().Be(350);
    }

    [Test]
    public async Task GetTotalTokensAsync_WhenNoEvents_ReturnsZero()
    {
        var total = await _log.GetTotalTokensAsync("nonexistent");
        total.Should().Be(0);
    }

    [Test]
    public async Task ReplayAsync_WhenNoEvents_ReturnsEmpty()
    {
        var replayed = new List<AgentStepEvent>();
        await foreach (var e in _log.ReplayAsync("empty"))
            replayed.Add(e);

        replayed.Should().BeEmpty();
    }

    [Test]
    public async Task AppendAsync_CanStoreAllStepTypes()
    {
        var types = Enum.GetValues<AgentStepType>();
        for (var i = 0; i < types.Length; i++)
            await _log.AppendAsync(MakeEvent("all-types", i, types[i]));

        var replayed = new List<AgentStepEvent>();
        await foreach (var e in _log.ReplayAsync("all-types"))
            replayed.Add(e);

        replayed.Should().HaveCount(types.Length);
        replayed.Select(e => e.StepType).Should().BeEquivalentTo(types);
    }

    [Test]
    public async Task AppendAsync_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _log.AppendAsync(MakeEvent("t", 0, AgentStepType.Init), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
