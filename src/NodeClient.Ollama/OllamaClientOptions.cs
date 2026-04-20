namespace NodeClient.Ollama;

/// <summary>
/// Per-node Ollama connection settings.
/// Bind from appsettings section "OllamaNode" or override via environment variables.
/// </summary>
public sealed class OllamaClientOptions
{
    public const string Section = "OllamaNode";

    /// <summary>Base URL of the Ollama HTTP API (e.g. http://192.168.1.20:11434).</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Timeout in seconds for a single inference call.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
