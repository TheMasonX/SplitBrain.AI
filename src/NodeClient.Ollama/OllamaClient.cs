using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Models;

namespace NodeClient.Ollama;

public sealed class OllamaClient : IOllamaClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _generateEndpoint;

    public OllamaClient(HttpClient http, IOptions<OllamaClientOptions> options)
    {
        _http = http;
        _baseUrl = options.Value.BaseUrl.TrimEnd('/');
        _generateEndpoint = $"{_baseUrl}/api/generate";
        _http.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
    }

    public async Task<string> ExecuteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new OllamaGenerateRequest
        {
            Model = request.Model,
            Prompt = request.Prompt,
            Stream = request.Stream
        };

        var json = JsonSerializer.Serialize(payload, OllamaJsonContext.Default.OllamaGenerateRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync(_generateEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (!request.Stream)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize(body, OllamaJsonContext.Default.OllamaGenerateResponse);
            return result?.Response ?? string.Empty;
        }

        var sb = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var chunk = JsonSerializer.Deserialize(line, OllamaJsonContext.Default.OllamaGenerateResponse);
            if (chunk?.Response is not null)
                sb.Append(chunk.Response);

            if (chunk?.Done == true) break;
        }

        return sb.ToString();
    }

    public void Dispose() => _http.Dispose();

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"{_baseUrl}/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = default!;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = default!;

    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = true;
}

internal sealed class OllamaGenerateResponse
{
    [JsonPropertyName("response")]
    public string? Response { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }
}

[JsonSerializable(typeof(OllamaGenerateRequest))]
[JsonSerializable(typeof(OllamaGenerateResponse))]
internal partial class OllamaJsonContext : JsonSerializerContext { }
