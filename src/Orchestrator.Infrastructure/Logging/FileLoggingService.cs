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

        await AppendToFileAsync(filePath, json + Environment.NewLine, cancellationToken);
    }

    private async Task WriteLogEntryAsync(LogEntry entry, string category, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
        var fileName = $"{category}-{DateTimeOffset.UtcNow:yyyy-MM-dd}.jsonl";
        var filePath = Path.Combine(_options.LogDirectory, fileName);
        await AppendToFileAsync(filePath, json + Environment.NewLine, cancellationToken);
    }

    private async Task AppendToFileAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        // Ensure directory exists
        Directory.CreateDirectory(_options.LogDirectory);

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var existed = File.Exists(filePath);
            await File.AppendAllTextAsync(filePath, content, cancellationToken);

            // If we created a new file, write an event to the file-events log (but don't recursive-log file-events)
            if (!existed && !Path.GetFileName(filePath).StartsWith("file-events", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var evt = new
                    {
                        Event = "FileCreated",
                        FilePath = filePath,
                        Timestamp = DateTimeOffset.UtcNow
                    };

                    var evtJson = JsonSerializer.Serialize(evt, new JsonSerializerOptions { WriteIndented = true });
                    var eventsFile = Path.Combine(_options.LogDirectory, $"file-events-{DateTimeOffset.UtcNow:yyyy-MM-dd}.jsonl");
                    await File.AppendAllTextAsync(eventsFile, evtJson + Environment.NewLine, cancellationToken);
                    _logger.LogInformation("Created log file: {FilePath}", filePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to write file creation event for {FilePath}", filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append to {FilePath}", filePath);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task LogChatMessageAsync(string conversationId, string role, string senderId, string message, DateTimeOffset timestamp, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;

        var chatEntry = new
        {
            ConversationId = conversationId,
            Role = role,
            SenderId = senderId,
            Message = message,
            Timestamp = timestamp
        };

        var json = JsonSerializer.Serialize(chatEntry, new JsonSerializerOptions { WriteIndented = true });
        var fileName = $"chats-{DateTimeOffset.UtcNow:yyyy-MM-dd}.jsonl";
        var filePath = Path.Combine(_options.LogDirectory, fileName);

        await AppendToFileAsync(filePath, json + Environment.NewLine, cancellationToken);
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
