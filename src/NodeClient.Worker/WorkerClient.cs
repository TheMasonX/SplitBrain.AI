using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Models;

namespace NodeClient.Worker;

/// <summary>
/// HTTP client that forwards inference requests to a remote Orchestrator.NodeWorker instance.
/// The worker exposes POST /inference, GET /health, GET /models.
/// </summary>
public sealed class WorkerClient : IWorkerClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public WorkerClient(HttpClient http, IOptions<WorkerClientOptions> options)
    {
        _http = http;
        _baseUrl = options.Value.BaseUrl.TrimEnd('/');
        _http.Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
    }

    public async Task<InferenceResult> ExecuteAsync(
        InferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var json = JsonSerializer.Serialize(request, WorkerJsonContext.Default.InferenceRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync($"{_baseUrl}/inference", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync(
            WorkerJsonContext.Default.InferenceResult,
            cancellationToken);

        return result ?? throw new InvalidOperationException("Worker returned null inference result.");
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"{_baseUrl}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"{_baseUrl}/models", cancellationToken);
            response.EnsureSuccessStatusCode();

            var models = await response.Content.ReadFromJsonAsync(
                WorkerJsonContext.Default.ListModelInfo,
                cancellationToken);

            return models ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Dispose() => _http.Dispose();
}

[JsonSerializable(typeof(InferenceRequest))]
[JsonSerializable(typeof(InferenceResult))]
[JsonSerializable(typeof(List<ModelInfo>))]
internal partial class WorkerJsonContext : JsonSerializerContext { }
