using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orchestrator.Core.Interfaces;
using Orchestrator.Core.Models;

namespace Orchestrator.Infrastructure.Logging;

public sealed class FileLoggingService : ILoggingService, IDisposable
{
    private readonly ILogger<FileLoggingService> _logger;
    private readonly FileLoggingOptions _options;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    public FileLoggingService(ILogger<FileLoggingService> logger, IOptions<FileLoggingOptions> options)
    {
        _logger = logger;
        _options = options.Value;

        if (_options.Enabled)
        {
            Directory.CreateDirectory(_options.LogDirectory);
            _logger.LogInformation("File logging enabled. Directory: {LogDirectory}", _options.LogDirectory);
        }
    }

    public async Task LogRequestAsync<TRequest>(string operation, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
    {
        if (!_options.Enabled) return;

        var entry = new LogEntry
        {
            Operation = operation,
            Type = "Request",
            Data = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true })
        };

        await WriteLogEntryAsync(entry, "requests", cancellationToken);
    }

    public async Task LogResponseAsync<TResponse>(string operation, TResponse response, long elapsedMs, CancellationToken cancellationToken = default)
        where TResponse : class
    {
        if (!_options.Enabled) return;

        var entry = new LogEntry
        {
            Operation = operation,
            Type = "Response",
            Data = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }),
            ElapsedMs = elapsedMs
        };

        await WriteLogEntryAsync(entry, "responses", cancellationToken);
    }

    public async Task LogErrorAsync(string operation, Exception exception, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;

        var entry = new LogEntry
        {
            Operation = operation,
            Type = "Error",
            ErrorMessage = exception.Message,
            StackTrace = exception.StackTrace
        };

        await WriteLogEntryAsync(entry, "errors", cancellationToken);
    }

    public async Task LogInferenceAsync(string taskId, string prompt, string response, string model, string nodeId, long latencyMs, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;

        var entry = new InferenceLogEntry
        {
            TaskId = taskId,
            Model = model,
            NodeId = nodeId,
            Prompt = prompt,
            Response = response,
            LatencyMs = latencyMs,
            PromptLength = prompt.Length,
            ResponseLength = response.Length
        };

        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        var fileName = $"inference-{DateTimeOffset.UtcNow:yyyy-MM-dd}.jsonl";
        var filePath = Path.Combine(_options.LogDirectory, fileName);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(filePath, json + Environment.NewLine, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write inference log to {FilePath}", filePath);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task WriteLogEntryAsync(LogEntry entry, string category, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        var fileName = $"{category}-{DateTimeOffset.UtcNow:yyyy-MM-dd}.jsonl";
        var filePath = Path.Combine(_options.LogDirectory, fileName);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(filePath, json + Environment.NewLine, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write log entry to {FilePath}", filePath);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _semaphore.Dispose();
        _disposed = true;
    }
}

public sealed class FileLoggingOptions
{
    public const string Section = "FileLogging";

    public bool Enabled { get; init; } = true;
    public string LogDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "splitbrain-logs");
}
