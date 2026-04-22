using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Orchestrator.Core.Enums;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Agents.SemanticKernel;

/// <summary>
/// Bridges Semantic Kernel's <see cref="IChatCompletionService"/> to the SplitBrain
/// <see cref="IInferenceNode"/> abstraction, allowing SK agents and planners to run
/// inference through the existing routing and node pipeline.
/// </summary>
public sealed class InferenceNodeChatCompletionService : IChatCompletionService
{
    private readonly IInferenceNode _node;
    private readonly TaskType _taskType;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Attributes { get; }

    public InferenceNodeChatCompletionService(IInferenceNode node, TaskType taskType = TaskType.Chat)
    {
        _node     = node;
        _taskType = taskType;
        Attributes = new Dictionary<string, object?>
        {
            ["nodeId"]   = node.NodeId,
            ["taskType"] = taskType.ToString()
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(chatHistory);
        var request = new InferenceRequest { Prompt = prompt };

        var response = await _node.ExecuteAsync(request, cancellationToken);

        return [new ChatMessageContent(AuthorRole.Assistant, response.Text)];
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // IInferenceNode does not support streaming — fall back to non-streaming
        var results = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
        foreach (var msg in results)
            yield return new StreamingChatMessageContent(msg.Role, msg.Content);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string BuildPrompt(ChatHistory history)
    {
        var sb = new StringBuilder();
        foreach (var msg in history)
        {
            var role = msg.Role == AuthorRole.System   ? "SYSTEM"
                     : msg.Role == AuthorRole.User     ? "USER"
                     : msg.Role == AuthorRole.Assistant ? "ASSISTANT"
                     : msg.Role.Label.ToUpperInvariant();

            sb.AppendLine($"[{role}]");
            sb.AppendLine(msg.Content);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
