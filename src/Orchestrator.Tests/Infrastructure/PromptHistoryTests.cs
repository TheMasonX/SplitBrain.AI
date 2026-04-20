using Orchestrator.Core.Enums;
using Orchestrator.Infrastructure.History;

namespace Orchestrator.Tests.Infrastructure;

public sealed class PromptHistoryTests
{
    private readonly PromptHistoryService _sut = new();

    [Fact]
    public void Add_ReturnsNonEmptyId()
    {
        var id = _sut.Add("hello", TaskType.Chat, "A");
        id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetRecent_ReturnsAddedEntry()
    {
        _sut.Add("test prompt", TaskType.Review, "B");

        var recent = _sut.GetRecent(10);

        recent.Should().ContainSingle(e => e.Prompt == "test prompt" && e.NodeId == "B");
    }

    [Fact]
    public void Complete_SetsResponseAndSuccess()
    {
        var id = _sut.Add("prompt", TaskType.Refactor, "A");

        _sut.Complete(id, "my response", success: true);

        var entry = _sut.GetRecent(1).Single();
        entry.Response.Should().Be("my response");
        entry.Success.Should().BeTrue();
    }

    [Fact]
    public void Complete_WithUnknownId_DoesNotThrow()
    {
        var act = () => _sut.Complete("nonexistent-id", "response", success: false);
        act.Should().NotThrow();
    }

    [Fact]
    public void GetRecent_RespectsCountLimit()
    {
        for (var i = 0; i < 10; i++)
            _sut.Add($"prompt {i}", TaskType.Chat, "A");

        var recent = _sut.GetRecent(3);

        recent.Should().HaveCount(3);
    }

    [Fact]
    public void Add_RingBuffer_EvictsOldestWhenOverCapacity()
    {
        // Add 51 entries (capacity is 50)
        for (var i = 0; i < 51; i++)
            _sut.Add($"prompt {i}", TaskType.Chat, "A");

        var recent = _sut.GetRecent(100);

        recent.Should().HaveCount(50);
        // The evicted first entry should not be present
        recent.Should().NotContain(e => e.Prompt == "prompt 0");
    }

    [Fact]
    public void GetRecent_ReturnsEntriesOrderedByMostRecentFirst()
    {
        _sut.Add("first", TaskType.Chat, "A");
        _sut.Add("second", TaskType.Chat, "A");
        _sut.Add("third", TaskType.Chat, "A");

        var recent = _sut.GetRecent(3);

        recent[0].Prompt.Should().Be("third");
        recent[2].Prompt.Should().Be("first");
    }
}
