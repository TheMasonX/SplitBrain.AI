using Orchestrator.Core.Interfaces;
using Serilog.Core;
using Serilog.Events;

namespace SplitBrain.Dashboard.Logging;

/// <summary>
/// Custom Serilog sink that forwards log events to the dashboard via ILogEntryPublisher.
/// Accepts IServiceProvider and resolves ILogEntryPublisher lazily on first Emit to avoid
/// re-entrant DI resolution that causes infinite recursion during Serilog bootstrap.
/// </summary>
public sealed class SignalRLogSink : ILogEventSink
{
    private readonly IServiceProvider _services;
    private readonly IFormatProvider? _formatProvider;
    private ILogEntryPublisher? _publisher;

    public SignalRLogSink(IServiceProvider services, IFormatProvider? formatProvider = null)
    {
        _services = services;
        _formatProvider = formatProvider;
    }

    public void Emit(LogEvent logEvent)
    {
        _publisher ??= (ILogEntryPublisher?)_services.GetService(typeof(ILogEntryPublisher));
        if (_publisher is null)
            return;

        var message = logEvent.RenderMessage(_formatProvider);
        var level = logEvent.Level.ToString();

        // Fire-and-forget — don't block the Serilog pipeline
        _ = _publisher.PublishAsync(level, message, null, logEvent.Timestamp);
    }
}
