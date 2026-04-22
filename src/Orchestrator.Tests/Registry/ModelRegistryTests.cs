using FluentAssertions;
using NUnit.Framework;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Models;
using Orchestrator.Infrastructure.Registry;

namespace Orchestrator.Tests.Registry;

[TestFixture]
public class ModelRegistryTests
{
    private InMemoryModelRegistry _registry = null!;

    [SetUp]
    public void SetUp() => _registry = new InMemoryModelRegistry();

    private static ModelDefinition MakeModel(string id, TaskType primary, params string[] nodes) =>
        new()
        {
            ModelId = id,
            DisplayName = id,
            Family = ModelFamily.Qwen,
            PrimaryCapability = primary,
            PreferredNodeIds = nodes.ToList()
        };

    [Test]
    public void GetAllModels_WhenEmpty_ReturnsEmpty()
    {
        _registry.GetAllModels().Should().BeEmpty();
    }

    [Test]
    public void RegisterAndGetModel_ReturnsRegisteredModel()
    {
        var model = MakeModel("m1", TaskType.Chat);
        _registry.RegisterModel(model);

        _registry.GetModel("m1").Should().NotBeNull();
        _registry.GetModel("m1")!.ModelId.Should().Be("m1");
    }

    [Test]
    public void GetModel_CaseInsensitive_ReturnsModel()
    {
        _registry.RegisterModel(MakeModel("ModelA", TaskType.Chat));
        _registry.GetModel("modela").Should().NotBeNull();
    }

    [Test]
    public void GetModel_WhenNotFound_ReturnsNull()
    {
        _registry.GetModel("missing").Should().BeNull();
    }

    [Test]
    public void GetModelsForTask_MatchesPrimaryCapability()
    {
        _registry.RegisterModel(MakeModel("chat1", TaskType.Chat));
        _registry.RegisterModel(MakeModel("refactor1", TaskType.Refactor));

        var results = _registry.GetModelsForTask(TaskType.Chat);

        results.Should().ContainSingle(m => m.ModelId == "chat1");
    }

    [Test]
    public void GetModelsForTask_MatchesSecondaryCapability()
    {
        var model = MakeModel("multi", TaskType.Chat) with
        {
            SecondaryCapabilities = [TaskType.Refactor]
        };
        _registry.RegisterModel(model);

        var results = _registry.GetModelsForTask(TaskType.Refactor);

        results.Should().ContainSingle(m => m.ModelId == "multi");
    }

    [Test]
    public void GetModelsForNode_ReturnsMatchingModels()
    {
        _registry.RegisterModel(MakeModel("m-nodeA", TaskType.Chat, "A"));
        _registry.RegisterModel(MakeModel("m-nodeB", TaskType.Chat, "B"));

        var results = _registry.GetModelsForNode("A");

        results.Should().ContainSingle(m => m.ModelId == "m-nodeA");
    }

    [Test]
    public void UpdateNodeModels_AndGetAvailableModels_ReturnsUpdatedList()
    {
        _registry.UpdateNodeModels("A", ["m1", "m2"]);

        var available = _registry.GetAvailableModels("A");
        available.Should().BeEquivalentTo(["m1", "m2"]);
    }

    [Test]
    public void GetAvailableModels_WhenNodeUnknown_ReturnsEmpty()
    {
        _registry.GetAvailableModels("unknown").Should().BeEmpty();
    }

    [Test]
    public void RegisterModel_OverwritesExistingModel()
    {
        _registry.RegisterModel(MakeModel("m1", TaskType.Chat));
        _registry.RegisterModel(MakeModel("m1", TaskType.Refactor));

        _registry.GetModel("m1")!.PrimaryCapability.Should().Be(TaskType.Refactor);
    }

    [Test]
    public void GetAllModels_ReturnsAllRegistered()
    {
        _registry.RegisterModel(MakeModel("a", TaskType.Chat));
        _registry.RegisterModel(MakeModel("b", TaskType.Refactor));

        _registry.GetAllModels().Should().HaveCount(2);
    }
}
