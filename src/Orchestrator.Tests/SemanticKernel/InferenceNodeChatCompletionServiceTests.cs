using FluentAssertions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NSubstitute;
using NUnit.Framework;
using Orchestrator.Agents.SemanticKernel;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Tests.SemanticKernel;

[TestFixture]
public class InferenceNodeChatCompletionServiceTests
{
    private IInferenceNode _node = null!;
    private InferenceNodeChatCompletionService _svc = null!;

    [SetUp]
    public void SetUp()
    {
        _node = Substitute.For<IInferenceNode>();
        _node.NodeId.Returns("test-node");
        _node.DisposeAsync().Returns(ValueTask.CompletedTask);
        _svc = new InferenceNodeChatCompletionService(_node, TaskType.Chat);
    }

    private void SetNodeResponse(string text) =>
        _node.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
             .Returns(new InferenceResult { Text = text, NodeId = "test-node" });

    [Test]
    public async Task GetChatMessageContentsAsync_ReturnsSingleAssistantMessage()
    {
        SetNodeResponse("Hello from AI!");
        var history = new ChatHistory("You are helpful.");
        history.AddUserMessage("Hi there");

        var result = await _svc.GetChatMessageContentsAsync(history);

        result.Should().ContainSingle();
        result[0].Role.Should().Be(AuthorRole.Assistant);
        result[0].Content.Should().Be("Hello from AI!");
    }

    [Test]
    public async Task GetChatMessageContentsAsync_PassesPromptContainingAllMessages()
    {
        SetNodeResponse("result");
        var history = new ChatHistory("System prompt");
        history.AddUserMessage("User question");

        await _svc.GetChatMessageContentsAsync(history);

        await _node.Received(1).ExecuteAsync(
            Arg.Is<InferenceRequest>(r =>
                r.Prompt.Contains("System prompt") &&
                r.Prompt.Contains("User question")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetStreamingChatMessageContentsAsync_YieldsOneChunk()
    {
        SetNodeResponse("streamed response");
        var history = new ChatHistory();
        history.AddUserMessage("stream test");

        var chunks = new List<StreamingChatMessageContent>();
        await foreach (var chunk in _svc.GetStreamingChatMessageContentsAsync(history))
            chunks.Add(chunk);

        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Be("streamed response");
    }

    [Test]
    public void Attributes_ContainsNodeIdAndTaskType()
    {
        _svc.Attributes.Should().ContainKey("nodeId").WhoseValue.Should().Be("test-node");
        _svc.Attributes.Should().ContainKey("taskType").WhoseValue.Should().Be("Chat");
    }

    [Test]
    public async Task GetChatMessageContentsAsync_RespectsCancellation()
    {
        _node.ExecuteAsync(Arg.Any<InferenceRequest>(), Arg.Any<CancellationToken>())
             .Returns<InferenceResult>(_ => throw new OperationCanceledException());

        var history = new ChatHistory();
        history.AddUserMessage("cancel me");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _svc.GetChatMessageContentsAsync(history, cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
