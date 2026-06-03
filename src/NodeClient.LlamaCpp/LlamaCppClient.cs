using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Models;

namespace NodeClient.LlamaCpp;

/// <summary>
/// HTTP transport client for a running llama.cpp server (llama-server).
/// Uses the OpenAI-compatible /v1/chat/completions endpoint.
/// Timeout is set via ConfigureHttpClient at DI registration, not in this constructor.
/// </summary>
public sealed class LlamaCppClient : ILlamaCppClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<LlamaCppClient> _logger;
    private readonly string _chatEndpoint;
    private readonly string _healthEndpoint;
    private readonly string _modelsEndpoint;

    public LlamaCppClient(
        HttpClient http,
        IOptions<LlamaCppClientOptions> options,
        ILogger<LlamaCppClient> logger)
    {
        _http   = http;
        _logger = logger;
        var baseUrl      = options.Value.BaseUrl.TrimEnd('/');
        _chatEndpoint    = $"{baseUrl}/v1/chat/completions";
        _healthEndpoint  = $"{baseUrl}/health";
        _modelsEndpoint  = $"{baseUrl}/v1/models";
        // Timeout is configured via ConfigureHttpClient at registration (Program.cs),
        // not set here to align with IHttpClientFactory best practices.
    }

    // ── Non-streaming ─────────────────────────────────────────────────────────

    public async Task<string> ExecuteAsync(
        InferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var payload = BuildChatPayload(request, stream: false);
        var json    = JsonSerializer.Serialize(payload, LlamaCppJsonContext.Default.ChatCompletionRequest);
        using var content  = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(_chatEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body   = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize(body, LlamaCppJsonContext.Default.ChatCompletionResponse);
        return result?.Choices?[0]?.Message?.Content ?? string.Empty;
    }

    // ── Streaming (SSE) ───────────────────────────────────────────────────────

    public async IAsyncEnumerable<string> StreamChunksAsync(
        InferenceRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var payload = BuildChatPayload(request, stream: true);
        var json    = JsonSerializer.Serialize(payload, LlamaCppJsonContext.Default.ChatCompletionRequest);

        // Iterator disposal semantics: all 'using var' locals in an IAsyncEnumerable method
        // are disposed when the enumerator is disposed (fully consumed or abandoned),
        // NOT at the next yield point. content, reqMsg, response, stream, and reader
        // therefore stay alive for the full duration of enumeration — which is correct.
        // content and reqMsg carry no I/O after SendAsync completes; holding them is harmless.
        // HttpCompletionOption.ResponseHeadersRead keeps the TCP connection open so the
        // response body stream can be read line-by-line as SSE tokens arrive.
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var reqMsg  = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint)
        {
            Content = content
        };

        using var response = await _http.SendAsync(
            reqMsg,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        var doneSeen = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            // SSE: skip non-data lines (event:, id:, retry:) — log unknown event types
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                    _logger.LogDebug("llama.cpp SSE event line: {Line}", line);
                continue;
            }

            var data = line["data: ".Length..].Trim();

            if (data == "[DONE]")
            {
                doneSeen = true;
                yield break;
            }

            ChatCompletionChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize(data, LlamaCppJsonContext.Default.ChatCompletionChunk);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse SSE data line from llama.cpp: {Data}", data);
                continue;
            }

            var delta = chunk?.Choices?[0]?.Delta?.Content;
            if (delta is not null)
                yield return delta;

            // finish_reason is set on the final chunk (alongside an empty delta).
            // [DONE] follows in the next line. Both are checked to handle server variants.
            if (chunk?.Choices?[0]?.FinishReason is not null)
            {
                doneSeen = true;
                yield break;
            }
        }

        if (!doneSeen && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "llama.cpp stream ended without [DONE] sentinel — server may have crashed mid-stream. " +
                "Caller will receive partial accumulated text.");
        }
    }

    // ── Health ────────────────────────────────────────────────────────────────

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetServerStatusAsync(cancellationToken);
        return status == "ok";
    }

    public async Task<string?> GetServerStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(_healthEndpoint, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var body   = await response.Content.ReadAsStringAsync(cancellationToken);
            var health = JsonSerializer.Deserialize(body, LlamaCppJsonContext.Default.HealthResponse);
            return health?.Status;
        }
        catch
        {
            return null;
        }
    }

    // ── Models ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(_modelsEndpoint, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body   = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize(body, LlamaCppJsonContext.Default.ModelsResponse);
            return result?.Data?.Select(m => new ModelInfo { ModelId = m.Id }).ToList()
                   ?? (IReadOnlyList<ModelInfo>)[];
        }
        catch
        {
            return [];
        }
    }

    public void Dispose() => _http.Dispose();

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Wraps InferenceRequest.Prompt as a single user message.
    /// InferenceRequest is single-turn by design; role injection is the orchestrator's
    /// responsibility upstream. Prompt is treated as a rendered template string.
    /// </summary>
    private static ChatCompletionRequest BuildChatPayload(InferenceRequest request, bool stream) =>
        new()
        {
            // llama.cpp ignores the model field (one-model server), included for OAI spec compliance.
            Model    = "local",
            Messages = [new ChatMessage { Role = "user", Content = request.Prompt }],
            Stream   = stream
        };
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

internal sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]    public string        Model    { get; init; } = default!;
    [JsonPropertyName("messages")] public ChatMessage[] Messages { get; init; } = [];
    [JsonPropertyName("stream")]   public bool          Stream   { get; init; }
}

internal sealed class ChatMessage
{
    [JsonPropertyName("role")]    public string Role    { get; init; } = default!;
    [JsonPropertyName("content")] public string Content { get; init; } = default!;
}

/// <summary>Non-streaming response: choice carries a full Message.</summary>
internal sealed class ChatCompletionResponse
{
    [JsonPropertyName("choices")] public ChatCompletionChoice[]? Choices { get; init; }
}

internal sealed class ChatCompletionChoice
{
    /// <summary>Populated in non-streaming responses.</summary>
    [JsonPropertyName("message")]       public ChatMessage? Message      { get; init; }
    /// <summary>Populated in streaming chunks.</summary>
    [JsonPropertyName("delta")]         public ChatMessage? Delta        { get; init; }
    /// <summary>Set to "stop" or "length" on the final chunk; null on intermediate chunks.</summary>
    [JsonPropertyName("finish_reason")] public string?      FinishReason { get; init; }
}

/// <summary>Streaming chunk: choice carries a Delta (not Message).</summary>
internal sealed class ChatCompletionChunk
{
    [JsonPropertyName("choices")] public ChatCompletionChoice[]? Choices { get; init; }
}

internal sealed class HealthResponse
{
    /// <summary>Known values: "ok", "loading model", "error".</summary>
    [JsonPropertyName("status")] public string? Status { get; init; }
}

internal sealed class ModelsResponse
{
    [JsonPropertyName("data")] public ModelEntry[]? Data { get; init; }
}

internal sealed class ModelEntry
{
    [JsonPropertyName("id")] public string Id { get; init; } = default!;
}

[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(ChatCompletionChunk))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ModelsResponse))]
internal partial class LlamaCppJsonContext : JsonSerializerContext { }
