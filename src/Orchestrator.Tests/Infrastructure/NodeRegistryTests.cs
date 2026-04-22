using Microsoft.Extensions.Options;
using Orchestrator.Core.Configuration;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Registry;

namespace Orchestrator.Tests.Infrastructure;

public sealed class NodeRegistryTests
{
    private static NodeConfiguration MakeConfig(string id, NodeRole role = NodeRole.Fast, bool enabled = true) =>
        new()
        {
            NodeId = id,
            DisplayName = $"Node {id}",
            Provider = NodeProviderType.Ollama,
            Role = role,
            Enabled = enabled,
            Tags = ["test"],
            Ollama = new OllamaProviderConfig { Host = "localhost" }
        };

    private static NodeTopologyConfig TopologyWith(params NodeConfiguration[] configs) =>
        new() { Nodes = [.. configs] };

    private static (NodeRegistry registry, IInferenceNodeFactory factory, IOptionsMonitor<NodeTopologyConfig> monitor) CreateRegistry(
        NodeTopologyConfig? initialConfig = null)
    {
        initialConfig ??= TopologyWith(MakeConfig("A"));

        var factory = Substitute.For<IInferenceNodeFactory>();
        factory.Create(Arg.Any<NodeConfiguration>())
               .Returns(ci => CreateFakeNode(ci.Arg<NodeConfiguration>().NodeId));

        var monitor = Substitute.For<IOptionsMonitor<NodeTopologyConfig>>();
        monitor.CurrentValue.Returns(initialConfig);

        // Capture the OnChange callback so tests can trigger hot-reload
        Action<NodeTopologyConfig, string?>? changeCallback = null;
        monitor.OnChange(Arg.Do<Action<NodeTopologyConfig, string?>>(cb => changeCallback = cb))
               .Returns(Substitute.For<IDisposable>());

        var registry = new NodeRegistry(factory, monitor);
        return (registry, factory, monitor);
    }

    private static IInferenceNode CreateFakeNode(string nodeId)
    {
        var node = Substitute.For<IInferenceNode>();
        node.NodeId.Returns(nodeId);
        node.DisposeAsync().Returns(ValueTask.CompletedTask);
        return node;
    }

    [Test]
    public void Constructor_RegistersEnabledNodes()
    {
        var config = TopologyWith(MakeConfig("A"), MakeConfig("B"));
        var (registry, _, _) = CreateRegistry(config);

        registry.GetAllNodes().Should().HaveCount(2);
        registry.GetNode("A").Should().NotBeNull();
        registry.GetNode("B").Should().NotBeNull();
    }

    [Test]
    public void Constructor_SkipsDisabledNodes()
    {
        var config = TopologyWith(MakeConfig("A"), MakeConfig("B", enabled: false));
        var (registry, _, _) = CreateRegistry(config);

        registry.GetAllNodes().Should().HaveCount(1);
        registry.GetNode("B").Should().BeNull();
    }

    [Test]
    public void RegisterNode_AddsNodeToRegistry()
    {
        var (registry, factory, _) = CreateRegistry(TopologyWith());

        // factory.Create is already configured via CreateRegistry to return fakes
        registry.RegisterNode(MakeConfig("X"));

        registry.GetNode("X").Should().NotBeNull();
    }

    [Test]
    public void DeregisterNode_RemovesNodeFromRegistry()
    {
        var (registry, _, _) = CreateRegistry(TopologyWith(MakeConfig("A")));

        registry.DeregisterNode("A");

        registry.GetNode("A").Should().BeNull();
    }

    [Test]
    public void DeregisterNode_UnknownId_DoesNotThrow()
    {
        var (registry, _, _) = CreateRegistry();

        var act = () => registry.DeregisterNode("nonexistent");

        act.Should().NotThrow();
    }

    [Test]
    public void GetNodesByRole_FiltersCorrectly()
    {
        var config = TopologyWith(
            MakeConfig("A", NodeRole.Fast),
            MakeConfig("B", NodeRole.Deep),
            MakeConfig("C", NodeRole.Fast));
        var (registry, _, _) = CreateRegistry(config);

        var fastNodes = registry.GetNodesByRole(NodeRole.Fast);

        fastNodes.Should().HaveCount(2);
        fastNodes.Select(n => n.Config.NodeId).Should().BeEquivalentTo(["A", "C"]);
    }

    [Test]
    public void GetNodesByTag_FiltersCorrectly()
    {
        var configA = MakeConfig("A") with { Tags = ["fast", "local"] };
        var configB = MakeConfig("B") with { Tags = ["deep"] };
        var (registry, factory, _) = CreateRegistry(TopologyWith(configA, configB));

        var localNodes = registry.GetNodesByTag("local");

        localNodes.Should().HaveCount(1);
        localNodes[0].Config.NodeId.Should().Be("A");
    }

    [Test]
    public void GetHealthyNodes_ReturnsOnlyHealthyNodes()
    {
        var (registry, _, _) = CreateRegistry(TopologyWith(MakeConfig("A"), MakeConfig("B")));

        registry.UpdateNodeHealth("A", new NodeHealthStatus { State = HealthState.Healthy, LastChecked = DateTimeOffset.UtcNow });
        registry.UpdateNodeHealth("B", new NodeHealthStatus { State = HealthState.Unavailable, LastChecked = DateTimeOffset.UtcNow });

        var healthy = registry.GetHealthyNodes();

        healthy.Should().HaveCount(1);
        healthy[0].Config.NodeId.Should().Be("A");
    }

    [Test]
    public void UpdateNodeHealth_SetsLastHealthOnRegistration()
    {
        var (registry, _, _) = CreateRegistry();

        var status = new NodeHealthStatus { State = HealthState.Degraded, LastChecked = DateTimeOffset.UtcNow };
        registry.UpdateNodeHealth("A", status);

        registry.GetNode("A")!.LastHealth.Should().Be(status);
    }

    [Test]
    public void UpdateNodeHealth_UnknownNodeId_DoesNotThrow()
    {
        var (registry, _, _) = CreateRegistry();

        var act = () => registry.UpdateNodeHealth("nonexistent",
            new NodeHealthStatus { State = HealthState.Healthy, LastChecked = DateTimeOffset.UtcNow });

        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_DoesNotThrow()
    {
        var (registry, _, _) = CreateRegistry();

        var act = () => registry.Dispose();

        act.Should().NotThrow();
    }
}
