using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using Orchestrator.Infrastructure.Configuration;

namespace Orchestrator.Tests.Infrastructure;

public sealed class RoutingOptionsPersistenceTests
{
    [Test]
    public void SaveFallbackChainsAsync_WhenChainsAreNull_Throws()
    {
        var sut = new RoutingOptionsPersistence(Path.Combine(Path.GetTempPath(), $"routing-{Guid.NewGuid():N}.json"));

        Func<Task> act = async () => await sut.SaveFallbackChainsAsync(null!);

        act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Test]
    public async Task SaveFallbackChainsAsync_WritesRoutingSectionWithFallbackChains()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"routing-{Guid.NewGuid():N}.json");
        var sut = new RoutingOptionsPersistence(filePath);

        var chains = new Dictionary<string, List<string>>
        {
            ["B"] = ["C", "A"],
            ["C"] = ["A"]
        };

        await sut.SaveFallbackChainsAsync(chains);

        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("Routing", out var routing).Should().BeTrue();
        routing.TryGetProperty("FallbackChains", out var fallbackChains).Should().BeTrue();
        fallbackChains.GetProperty("B").EnumerateArray().Select(x => x.GetString()).Should().Equal("C", "A");
    }

    [Test]
    public async Task SaveFallbackChainsAsync_SanitizesSelfReferencesWhitespaceAndDuplicates()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"routing-{Guid.NewGuid():N}.json");
        var sut = new RoutingOptionsPersistence(filePath);

        var chains = new Dictionary<string, List<string>>
        {
            [" B "] = [" B ", " C ", "C", " ", "A"],
            [" "] = ["A"]
        };

        await sut.SaveFallbackChainsAsync(chains);

        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);

        var targets = doc.RootElement
            .GetProperty("Routing")
            .GetProperty("FallbackChains")
            .GetProperty("B")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToArray();

        targets.Should().Equal("C", "A");
    }

    [Test]
    public async Task SaveFallbackChainsAsync_WhenCalledTwice_ReplacesPreviousContent()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"routing-{Guid.NewGuid():N}.json");
        var sut = new RoutingOptionsPersistence(filePath);

        await sut.SaveFallbackChainsAsync(new Dictionary<string, List<string>>
        {
            ["B"] = ["A"]
        });

        await sut.SaveFallbackChainsAsync(new Dictionary<string, List<string>>
        {
            ["C"] = ["B"]
        });

        var json = await File.ReadAllTextAsync(filePath);
        using var doc = JsonDocument.Parse(json);

        var chains = doc.RootElement.GetProperty("Routing").GetProperty("FallbackChains");
        chains.TryGetProperty("B", out _).Should().BeFalse();
        chains.GetProperty("C").EnumerateArray().Select(x => x.GetString()).Should().Equal("B");
    }
}
