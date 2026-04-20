namespace NodeClient.Copilot;

/// <summary>
/// Configuration for the GitHub Copilot API inference node (Node C).
/// Bind from appsettings section "CopilotNode".
///
/// API key security — two supported sources (evaluated in order):
///   1. Azure Key Vault  — set <see cref="KeyVaultUri"/> to your vault URI and
///      ensure the host identity has "Key Vault Secrets User" role.
///      DefaultAzureCredential is used (managed identity, workload identity,
///      environment variables, VS/CLI — no secret ever stored in config files).
///   2. Environment variable — COPILOT_API_KEY (fallback when Key Vault is not configured).
///
/// Never put a raw API key value in any appsettings file.
/// </summary>
public sealed class CopilotClientOptions
{
    public const string Section = "CopilotNode";

    /// <summary>
    /// Azure Key Vault URI (e.g. https://my-vault.vault.azure.net/).
    /// When set, the secret named <see cref="KeyVaultSecretName"/> is fetched
    /// at startup using <c>DefaultAzureCredential</c>.
    /// </summary>
    public string? KeyVaultUri { get; set; }

    /// <summary>Name of the Key Vault secret that holds the GitHub token. Defaults to "CopilotApiKey".</summary>
    public string KeyVaultSecretName { get; set; } = "CopilotApiKey";

    /// <summary>
    /// GitHub Copilot API base URL.
    /// For GitHub Enterprise Cloud this is always https://api.githubcopilot.com.
    /// Enterprise Server customers may override with their GHES endpoint.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.githubcopilot.com";

    /// <summary>
    /// Chat-completion model to request from the Copilot API.
    /// Use "gpt-4o" for deep tasks or "gpt-4o-mini" for faster/cheaper calls.
    /// </summary>
    public string Model { get; set; } = "gpt-4o";

    /// <summary>Timeout in seconds for a single inference call. Default 60s.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Value for the required Copilot-Version header.
    /// Identifies this client to the Copilot API gateway.
    /// </summary>
    public string CopilotVersion { get; set; } = "2023-09-07";
}
