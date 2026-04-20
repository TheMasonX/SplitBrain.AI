using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Models;

namespace NodeClient.Copilot;

/// <summary>
/// HTTP client for the GitHub Copilot API (OpenAI-compatible /v1/chat/completions).
/// Authentication token is injected at construction time — never read from config files.
/// </summary>
public sealed class CopilotClient : ICopilotClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _chatEndpoint;

    /// <param name="http">Pre-configured <see cref="HttpClient"/> with BaseAddress and Timeout set.</param>
    /// <param name="apiToken">GitHub token with Copilot access — sourced from Key Vault or env var.</param>
    /// <param name="options">Bound option values.</param>
    public CopilotClient(HttpClient http, string apiToken, IOptions<CopilotClientOptions> options)
    {
        _http = http;
        _model = options.Value.Model;

        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        _chatEndpoint = $"{baseUrl}/v1/chat/completions";

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiToken);
        _http.DefaultRequestHeaders.Add("Copilot-Integration-Id", "splitbrain-ai");
        _http.DefaultRequestHeaders.Add("Editor-Version", "splitbrain-ai/1.0");
        _http.DefaultRequestHeaders.Add("Copilot-Version", options.Value.CopilotVersion);
        _http.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
    }

    public async Task<string> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? _model : request.Model;

        var payload = new ChatCompletionRequest
        {
            Model = model,
            Messages =
            [
                new ChatMessage { Role = "user", Content = request.Prompt }
            ],
            Stream = request.Stream
        };

        var json = JsonSerializer.Serialize(payload, CopilotJsonContext.Default.ChatCompletionRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(_chatEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (!request.Stream)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize(body, CopilotJsonContext.Default.ChatCompletionResponse);
            return result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        }

        return await ReadStreamingResponseAsync(response, cancellationToken);
    }

    private static async Task<string> ReadStreamingResponseAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            // SSE format: "data: {...}" or "data: [DONE]"
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]") break;

            var chunk = JsonSerializer.Deserialize(data, CopilotJsonContext.Default.ChatCompletionStreamChunk);
            var delta = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
            if (delta is not null)
                sb.Append(delta);
        }

        return sb.ToString();
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Lightweight probe: list available models
            var baseUrl = _chatEndpoint[.._chatEndpoint.LastIndexOf('/')];
            using var response = await _http.GetAsync($"{baseUrl}/models", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}

// ---------------------------------------------------------------------------
// Internal request / response DTOs
// ---------------------------------------------------------------------------

internal sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = default!;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

internal sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = default!;

    [JsonPropertyName("content")]
    public string Content { get; set; } = default!;
}

internal sealed class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<ChatChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public ChatUsage? Usage { get; set; }
}

internal sealed class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }

    [JsonPropertyName("delta")]
    public ChatMessage? Delta { get; set; }
}

internal sealed class ChatCompletionStreamChunk
{
    [JsonPropertyName("choices")]
    public List<ChatChoice>? Choices { get; set; }
}

internal sealed class ChatUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }
}

[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(ChatCompletionStreamChunk))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class CopilotJsonContext : JsonSerializerContext { }
