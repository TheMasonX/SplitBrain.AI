using NUnit.Framework;
using Orchestrator.Infrastructure.Configuration;

namespace Orchestrator.Tests.Infrastructure;

public sealed class FallbackGraphValidatorTests
{
    [Test]
    public void TryGetFirstCyclePath_WhenGraphIsAcyclic_ReturnsFalse()
    {
        var graph = new Dictionary<string, List<string>>
        {
            ["A"] = ["B"],
            ["B"] = ["C"],
            ["C"] = []
        };

        var hasCycle = FallbackGraphValidator.TryGetFirstCyclePath(graph, out var cyclePath);

        hasCycle.Should().BeFalse();
        cyclePath.Should().BeEmpty();
    }

    [Test]
    public void TryGetFirstCyclePath_WhenGraphHasDirectCycle_ReturnsPath()
    {
        var graph = new Dictionary<string, List<string>>
        {
            ["A"] = ["B"],
            ["B"] = ["A"]
        };

        var hasCycle = FallbackGraphValidator.TryGetFirstCyclePath(graph, out var cyclePath);

        hasCycle.Should().BeTrue();
        cyclePath.Should().Be("A -> B -> A");
    }

    [Test]
    public void TryGetFirstCyclePath_WhenGraphHasMultiNodeCycle_ReturnsPath()
    {
        var graph = new Dictionary<string, List<string>>
        {
            ["A"] = ["B"],
            ["B"] = ["C"],
            ["C"] = ["A"]
        };

        var hasCycle = FallbackGraphValidator.TryGetFirstCyclePath(graph, out var cyclePath);

        hasCycle.Should().BeTrue();
        cyclePath.Should().Be("A -> B -> C -> A");
    }
}
