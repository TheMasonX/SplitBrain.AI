namespace Orchestrator.Core.Interfaces;

public interface ILoggingService
{
    Task LogRequestAsync<TRequest>(string operation, TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class;

    Task LogResponseAsync<TResponse>(string operation, TResponse response, long elapsedMs, CancellationToken cancellationToken = default)
        where TResponse : class;

    Task LogErrorAsync(string operation, Exception exception, CancellationToken cancellationToken = default);

    Task LogInferenceAsync(string taskId, string prompt, string response, string model, string nodeId, long latencyMs, CancellationToken cancellationToken = default);
}
