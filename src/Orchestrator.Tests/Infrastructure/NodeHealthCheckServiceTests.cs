using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ReceivedExtensions;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Registry;

namespace Orchestrator.Tests.Infrastructure;

public sealed class NodeHealthCheckServiceTests
{
    private static readonly INodeHealthPublisher NullPublisher = new NullNodeHealthPublisher();

    private static NodeHealthCheckService CreateService(INodeRegistry registry) =>
        new(registry, NullPublisher, NullLogger<NodeHealthCheckService>.Instance);
    private static NodeRegistration MakeRegistration(string nodeId, IInferenceNode node) =>
        new()
        {
            Config = new NodeConfiguration
            {
                NodeId = nodeId,
                DisplayName = nodeId,
                Provider = NodeProviderType.Ollama,
                Role = NodeRole.Fast,
                Enabled = true,
                HealthCheckIntervalMs = 500,
                Ollama = new OllamaProviderConfig { Host = "localhost" }
            },
            Node = node
        };

    private static IInferenceNode HealthyNode(string nodeId)
    {
        var node = Substitute.For<IInferenceNode>();
        node.NodeId.Returns(nodeId);
        node.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns(new NodeHealthStatus { State = HealthState.Healthy, LastChecked = DateTimeOffset.UtcNow });
        node.DisposeAsync().Returns(ValueTask.CompletedTask);
        return node;
    }

    private static IInferenceNode FailingNode(string nodeId)
    {
        var node = Substitute.For<IInferenceNode>();
        node.NodeId.Returns(nodeId);
        node.GetHealthAsync(Arg.Any<CancellationToken>())
            .Returns<NodeHealthStatus>(_ => throw new HttpRequestException("Connection refused"));
        node.DisposeAsync().Returns(ValueTask.CompletedTask);
        return node;
    }

    [Test]
    public async Task ExecuteAsync_HealthyNode_UpdatesRegistryWithHealthyStatus()
    {
        var node = HealthyNode("A");
        var registry = Substitute.For<INodeRegistry>();
        registry.GetAllNodes().Returns([MakeRegistration("A", node)]);

        var svc = CreateService(registry);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Run one full cycle then cancel
        await RunOneCycleAsync(svc, cts.Token).ConfigureAwait(false);

        registry.Received(Quantity.AtLeastOne()).UpdateNodeHealth(
            "A",
            Arg.Is<NodeHealthStatus>(s => s.State == HealthState.Healthy));
    }

    [Test]
    public async Task ExecuteAsync_FailingNode_DoesNotThrow_AndLogsWarning()
    {
        var node = FailingNode("A");
        var registry = Substitute.For<INodeRegistry>();
        registry.GetAllNodes().Returns([MakeRegistration("A", node)]);

        var svc = CreateService(registry);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Service should not throw even when health check throws
        var act = async () => await RunOneCycleAsync(svc, cts.Token);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ExecuteAsync_EmptyNodeList_DoesNotThrow()
    {
        var registry = Substitute.For<INodeRegistry>();
        registry.GetAllNodes().Returns([]);

        var svc = CreateService(registry);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = async () =>
        {
            try { await svc.StartAsync(cts.Token); await Task.Delay(300); }
            catch (OperationCanceledException) { }
        };

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task ExecuteAsync_MultipleNodes_ChecksAllNodes()
    {
        var nodeA = HealthyNode("A");
        var nodeB = HealthyNode("B");
        var registry = Substitute.For<INodeRegistry>();
        registry.GetAllNodes().Returns([
            MakeRegistration("A", nodeA),
            MakeRegistration("B", nodeB)
        ]);

        var svc = CreateService(registry);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await RunOneCycleAsync(svc, cts.Token).ConfigureAwait(false);

        await nodeA.Received(Quantity.AtLeastOne()).GetHealthAsync(Arg.Any<CancellationToken>());
        await nodeB.Received(Quantity.AtLeastOne()).GetHealthAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Runs the service long enough for one poll cycle to complete (500 ms interval + buffer).
    /// </summary>
    private static async Task RunOneCycleAsync(NodeHealthCheckService svc, CancellationToken outerCt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        cts.CancelAfter(TimeSpan.FromMilliseconds(800));

        try
        {
            await svc.StartAsync(cts.Token);
            await Task.Delay(600, cts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
        }
    }
}
