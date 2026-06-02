namespace NodeClient.LlamaCpp;

/// <summary>
/// Connection settings for a running llama.cpp server instance (llama-server).
/// Bind from appsettings section "LlamaCppNode" or override via environment variables.
/// </summary>
public sealed class LlamaCppClientOptions
{
    public const string Section = "LlamaCppNode";

    /// <summary>
    /// Base URL of the llama.cpp HTTP server.
    /// Default port 8080 — differs from Ollama's 11434.
    /// Example: http://192.168.1.20:8080
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>
    /// Timeout in seconds for a single inference call.
    /// llama.cpp may be slow on first token for large prompts; 180s is conservative.
    /// Configured via ConfigureHttpClient at registration, not set in LlamaCppClient ctor.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Human-readable label for the model loaded in this server instance.
    /// The llama.cpp server ignores the model field in requests (one model per server);
    /// this label is set at deploy time to match the --model GGUF name passed at startup.
    /// Used for InferenceResult.Model and observability only — not for routing.
    /// </summary>
    public string ModelLabel { get; set; } = "llama-cpp-default";

    /// <summary>
    /// NodeId reported in Capabilities and InferenceResult.
    /// Must match the NodeId in NodeTopologyConfig / NodeConfiguration.
    /// Defaults to "B" for the standard two-machine topology (Machine B = inference tower).
    /// </summary>
    public string NodeId { get; set; } = "B";
}
