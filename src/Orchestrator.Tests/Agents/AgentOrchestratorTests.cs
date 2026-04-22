using Microsoft.Extensions.Logging;
using Orchestrator.Agents;
using Orchestrator.Agents.Models;
using Orchestrator.Agents.Sandbox;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Tests.Agents;

public sealed class AgentOrchestratorTests
{
    private readonly IRoutingService _routing = Substitute.For<IRoutingService>();
    private readonly ICodeSandbox _sandbox = Substitute.For<ICodeSandbox>();
    private readonly IAgentEventLog _eventLog = Substitute.For<IAgentEventLog>();
    private readonly ILogger<AgentOrchestrator> _logger = Substitute.For<ILogger<AgentOrchestrator>>();
    private readonly AgentOrchestrator _sut;

    public AgentOrchestratorTests()
    {
        _sut = new AgentOrchestrator(_routing, _sandbox, _eventLog, _logger);
    }

    // -----------------------------------------------------------------------
    // Happy path: full PLAN → IMPLEMENT → REVIEW → TEST → DONE cycle
    // -----------------------------------------------------------------------

    [Test]
    public async Task RunAsync_AllStepsSucceed_ReturnsDone()
    {
        ArrangeFullSuccess();

        var result = await _sut.RunAsync(new AgentRequest { Goal = "add null check" });

        result.Success.Should().BeTrue();
        result.FinalState.Should().Be(AgentState.Done);
    }

    [Test]
    public async Task RunAsync_AllStepsSucceed_SetsNonEmptyDiff()
    {
        ArrangeFullSuccess();

        var result = await _sut.RunAsync(new AgentRequest { Goal = "add null check" });

        result.Diff.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task RunAsync_AllStepsSucceed_RecordsSteps()
    {
        ArrangeFullSuccess();

        var result = await _sut.RunAsync(new AgentRequest { Goal = "add null check" });

        result.Steps.Should().NotBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Abort: routing always throws → consecutive failures trigger abort
    // -----------------------------------------------------------------------

    [Test]
    public async Task RunAsync_RoutingAlwaysFails_ReturnsNotSuccessful()
    {
        _routing.RouteAsync(Arg.Any<TaskType>(), Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns<InferenceResult>(_ => throw new InvalidOperationException("node unavailable"));

        var result = await _sut.RunAsync(new AgentRequest { Goal = "failing goal" });

        result.Success.Should().BeFalse();
    }

    [Test]
    public async Task RunAsync_RoutingAlwaysFails_AbortReasonMentionsFailure()
    {
        _routing.RouteAsync(Arg.Any<TaskType>(), Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns<InferenceResult>(_ => throw new InvalidOperationException("node unavailable"));

        var result = await _sut.RunAsync(new AgentRequest { Goal = "failing goal" });

        result.AbortReason.Should().NotBeNullOrWhiteSpace();
    }

    // -----------------------------------------------------------------------
    // Abort: cancellation token respected
    // -----------------------------------------------------------------------

    [Test]
    public async Task RunAsync_CancelledToken_ReturnsCancelledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _sut.RunAsync(new AgentRequest { Goal = "cancelled" }, cts.Token);

        result.Success.Should().BeFalse();
        result.AbortReason.Should().Contain("Cancel");
    }

    // -----------------------------------------------------------------------
    // Abort: max iterations
    // -----------------------------------------------------------------------

    [Test]
    public async Task RunAsync_ReviewAlwaysRejects_HitsMaxIterationsAndAborts()
    {
        // Plan succeeds, Implement produces a diff, Review always rejects
        _routing.RouteAsync(TaskType.Chat, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = "1. plan step", NodeId = "A", Model = "m" });
        _routing.RouteAsync(TaskType.Refactor, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult
                {
                    Text = "--- a/file.cs\n+++ b/file.cs\n@@ -1 +1 @@\n+// fix",
                    NodeId = "A", Model = "m"
                });
        _routing.RouteAsync(TaskType.Review, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = "REJECTED: needs more work", NodeId = "B", Model = "m" });

        var result = await _sut.RunAsync(new AgentRequest { Goal = "loop forever" });

        result.Success.Should().BeFalse();
        result.AbortReason.Should().NotBeNullOrWhiteSpace();
    }

    // -----------------------------------------------------------------------
    // Abort: no diff produced
    // -----------------------------------------------------------------------

    [Test]
    public async Task RunAsync_ImplementReturnsEmptyDiff_AbortsWithNoDiffMessage()
    {
        // Plan succeeds, Implement returns text that contains no diff markers
        _routing.RouteAsync(TaskType.Chat, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = "1. plan step", NodeId = "A", Model = "m" });
        _routing.RouteAsync(TaskType.Refactor, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = string.Empty, NodeId = "A", Model = "m" });
        _routing.RouteAsync(TaskType.Review, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = "APPROVED", NodeId = "B", Model = "m" });
        _routing.RouteAsync(TaskType.TestGeneration, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = "[Fact] void Test() {}", NodeId = "B", Model = "m" });

        _sandbox.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SandboxResult { ExitCode = 0, Output = "passed", TimedOut = false });

        var result = await _sut.RunAsync(new AgentRequest { Goal = "empty diff" });

        // Empty diff should abort (no diff produced) or loop until max iterations
        result.Success.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Events are logged to IAgentEventLog
    // -----------------------------------------------------------------------

    [Test]
    public async Task RunAsync_AlwaysAppendsInitEvent()
    {
        ArrangeFullSuccess();

        await _sut.RunAsync(new AgentRequest { Goal = "emit events" });

        await _eventLog.Received()
            .AppendAsync(Arg.Is<AgentStepEvent>(e => e.StepType == AgentStepType.Init),
                         Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAsync_SuccessfulRun_AppendsDoneEvent()
    {
        ArrangeFullSuccess();

        await _sut.RunAsync(new AgentRequest { Goal = "emit done" });

        await _eventLog.Received()
            .AppendAsync(Arg.Is<AgentStepEvent>(e => e.StepType == AgentStepType.Done),
                         Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Token budget
    // -----------------------------------------------------------------------

    [Test]
    public async Task RunAsync_HugePromptResponse_ExceedsTokenBudgetAndAborts()
    {
        // Return a massive response so tokens add up quickly (~12 000 token limit)
        var bigText = new string('x', 12_000 * 4 + 100); // >> 12 000 tokens
        _routing.RouteAsync(Arg.Any<TaskType>(), Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = bigText, NodeId = "A", Model = "m" });

        var result = await _sut.RunAsync(new AgentRequest { Goal = "exhaust budget" });

        result.Success.Should().BeFalse();
        result.AbortReason.Should().Contain("oken");
    }

    // -----------------------------------------------------------------------
    // Sandbox failure causes loop-back to Implement
    // -----------------------------------------------------------------------

    [Test]
    public async Task RunAsync_SandboxFails_LoopsBackAndEventuallyAborts()
    {
        _routing.RouteAsync(TaskType.Chat, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = "plan", NodeId = "A", Model = "m" });
        _routing.RouteAsync(TaskType.Refactor, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult
                {
                    Text = "--- a/f.cs\n+++ b/f.cs\n@@ -1 +1 @@\n+// fix",
                    NodeId = "A", Model = "m"
                });
        _routing.RouteAsync(TaskType.Review, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = "APPROVED", NodeId = "B", Model = "m" });
        _routing.RouteAsync(TaskType.TestGeneration, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = "tests", NodeId = "B", Model = "m" });

        _sandbox.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SandboxResult { ExitCode = 1, Output = "FAILED", TimedOut = false });

        var result = await _sut.RunAsync(new AgentRequest
        {
            Goal = "sandbox always fails",
            WorkingDirectory = "C:\\fake"
        });

        result.Success.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void ArrangeFullSuccess()
    {
        _routing.RouteAsync(TaskType.Chat, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = "1. plan step", NodeId = "A", Model = "m" });
        _routing.RouteAsync(TaskType.Refactor, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult
                {
                    Text = "--- a/file.cs\n+++ b/file.cs\n@@ -1 +1 @@\n+// null check",
                    NodeId = "A", Model = "m"
                });
        _routing.RouteAsync(TaskType.Review, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = "APPROVED", NodeId = "B", Model = "m" });
        _routing.RouteAsync(TaskType.TestGeneration, Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
                .Returns(new InferenceResult { Text = "[Fact] void Test() {}", NodeId = "B", Model = "m" });

        _sandbox.RunAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SandboxResult { ExitCode = 0, Output = "passed", TimedOut = false });
    }
}
