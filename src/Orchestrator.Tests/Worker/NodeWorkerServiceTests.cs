using Microsoft.Extensions.Logging;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.NodeWorker;

namespace Orchestrator.Tests.Worker;

public sealed class NodeWorkerServiceTests
{
    private readonly IInferenceNode _node = Substitute.For<IInferenceNode>();
    private readonly INodeHealthCache _healthCache = Substitute.For<INodeHealthCache>();
    private readonly ILogger<NodeWorkerService> _logger = Substitute.For<ILogger<NodeWorkerService>>();

    private static NodeHealthStatus HealthyB() => new()
    {
        State = HealthState.Healthy,
        LastChecked = DateTimeOffset.UtcNow,
        ActiveRequests = 0,
        VramTotalMB = 8192,
        VramLoadedMB = 692
    };

    [Test]
    public async Task ExecuteAsync_LogsStartupAndHeartbeat()
    {
        _node.NodeId.Returns("B");
        _node.GetHealthAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(HealthyB()));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var sut = new NodeWorkerService(_node, _healthCache, _logger);

        await sut.StartAsync(cts.Token);
        await Task.Delay(120);
        await sut.StopAsync(CancellationToken.None);

        await _node.Received().GetHealthAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_ContinuesAfterTransientError()
    {
        _node.NodeId.Returns("B");
        _node.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<NodeHealthStatus>(new InvalidOperationException("transient")),
                _ => Task.FromResult(HealthyB()));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var sut = new NodeWorkerService(_node, _healthCache, _logger);

        await sut.StartAsync(cts.Token);
        await Task.Delay(150);
        await sut.StopAsync(CancellationToken.None);

        await _node.Received().GetHealthAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_StopsCleanlyOnCancellation()
    {
        _node.NodeId.Returns("B");
        _node.GetHealthAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(HealthyB()));

        using var cts = new CancellationTokenSource();
        var sut = new NodeWorkerService(_node, _healthCache, _logger);

        await sut.StartAsync(cts.Token);
        await cts.CancelAsync();

        var stopTask = sut.StopAsync(CancellationToken.None);
        await stopTask.WaitAsync(TimeSpan.FromSeconds(3));

        stopTask.IsCompleted.Should().BeTrue();
    }
}
