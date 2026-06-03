<#
.SYNOPSIS
    Writes appsettings.json for SplitBrain.AI after installation.

.DESCRIPTION
    Called by the Inno Setup installer [Run] section.
    Merges installer-collected values into the appropriate appsettings.json.

.PARAMETER InstallDir
    Root installation directory (e.g. C:\Program Files\SplitBrain.AI).

.PARAMETER Role
    "Orchestrator", "Worker", or "Full" (both on same machine).

.PARAMETER PeerIp
    IP address of the peer machine. For "Full" installs this is automatically
    set to localhost; for "Worker" it is the Orchestrator's address.

.PARAMETER PeerPort
    Port of the peer node worker (default 5050).

.PARAMETER McpPort
    Port the MCP server listens on (Node A only, default 5100).

.PARAMETER WorkerPort
    Port the NodeWorker listens on (Node B only, default 5050).

.PARAMETER OllamaUrl
    Ollama base URL (default http://localhost:11434).

.PARAMETER NodeBBackend
    "ollama" or "llamacpp" (Node B only, default "ollama").

.PARAMETER LlamaCppUrl
    llama.cpp server URL (Node B llama.cpp backend, default http://localhost:8080).

.PARAMETER CopilotToken
    Optional GitHub Copilot API token (Node A / Node C).

.PARAMETER GitHubToken
    Alias for CopilotToken.
#>
param(
    [Parameter(Mandatory)] [string] $InstallDir,
    [Parameter(Mandatory)] [ValidateSet("Orchestrator","Worker","Full")] [string] $Role,
    [string] $PeerIp       = "localhost",
    [int]    $PeerPort     = 5050,
    [int]    $McpPort      = 5100,
    [int]    $WorkerPort   = 5050,
    [string] $OllamaUrl    = "http://localhost:11434",
    [string] $NodeBBackend = "ollama",
    [string] $LlamaCppUrl  = "http://localhost:8080",
    [string] $CopilotToken = "",
    [string] $GitHubToken  = ""
)

if ($GitHubToken -and -not $CopilotToken) { $CopilotToken = $GitHubToken }

function ConvertTo-FormattedJson($obj) {
    $obj | ConvertTo-Json -Depth 10
}

# ── Orchestrator: configure Orchestrator.Mcp (Orchestrator or Full) ──────────
if ($Role -in @("Orchestrator","Full")) {
    $mcpConfig = @{
        Logging = @{
            LogLevel = @{ Default = "Information"; "Microsoft.Hosting.Lifetime" = "Warning" }
        }
        Urls = "http://0.0.0.0:$McpPort"
        OllamaNode = @{
            BaseUrl        = $OllamaUrl
            TimeoutSeconds = 120
        }
        NodeWorker = @{
            # Full: NodeWorker is on this machine, connect to localhost
            BaseUrl        = if ($Role -eq "Full") { "http://localhost:${WorkerPort}" } else { "http://${PeerIp}:${PeerPort}" }
            TimeoutSeconds = 30
        }
        CopilotNode = @{
            Model          = "gpt-4o"
            TimeoutSeconds = 60
        }
        Routing = @{
            Nodes = @()
        }
    }

    $mcpAppsettings = Join-Path $InstallDir "node-a\mcp\appsettings.json"
    ConvertTo-FormattedJson $mcpConfig | Set-Content -Path $mcpAppsettings -Encoding UTF8
    Write-Host "[OK] Wrote $mcpAppsettings"

    # Store Copilot token as User-scope env var if provided
    if ($CopilotToken) {
        [System.Environment]::SetEnvironmentVariable("COPILOT_API_KEY", $CopilotToken, "User")
        Write-Host "[OK] Set COPILOT_API_KEY (User scope)"
    }

    # Dashboard appsettings (minimal — points at MCP server)
    $dashConfig = @{
        Logging = @{
            LogLevel = @{ Default = "Information"; "Microsoft.Hosting.Lifetime" = "Warning" }
        }
        Urls = "http://0.0.0.0:5000"
        McpServer = @{ BaseUrl = "http://localhost:$McpPort" }
    }
    $dashAppsettings = Join-Path $InstallDir "node-a\dashboard\appsettings.json"
    if (Test-Path (Split-Path $dashAppsettings)) {
        ConvertTo-FormattedJson $dashConfig | Set-Content -Path $dashAppsettings -Encoding UTF8
        Write-Host "[OK] Wrote $dashAppsettings"
    }
}

# ── Worker: configure Orchestrator.NodeWorker (Worker or Full) ───────────────
if ($Role -in @("Worker","Full")) {
    $workerConfig = @{
        Logging = @{
            LogLevel = @{ Default = "Information"; "Microsoft.Hosting.Lifetime" = "Warning" }
        }
        Urls = "http://0.0.0.0:$WorkerPort"
        OllamaNode = @{
            BaseUrl        = $OllamaUrl
            TimeoutSeconds = 120
        }
        LlamaCppNode = @{
            BaseUrl        = $LlamaCppUrl
            TimeoutSeconds = 180
            ModelLabel     = "qwen3-coder-30b-a3b"
            NodeId         = "B"
            VramMb         = 8192
        }
    }

    # Write base appsettings
    $workerAppsettings = Join-Path $InstallDir "node-b\worker\appsettings.json"
    ConvertTo-FormattedJson $workerConfig | Set-Content -Path $workerAppsettings -Encoding UTF8
    Write-Host "[OK] Wrote $workerAppsettings"

    # Write NodeB-specific appsettings (same content, environment-override file)
    $workerAppsettingsNodeB = Join-Path $InstallDir "node-b\worker\appsettings.NodeB.json"
    ConvertTo-FormattedJson $workerConfig | Set-Content -Path $workerAppsettingsNodeB -Encoding UTF8
    Write-Host "[OK] Wrote $workerAppsettingsNodeB"

    # Set NODE_B_BACKEND environment variable
    if ($NodeBBackend -eq "llamacpp") {
        [System.Environment]::SetEnvironmentVariable("NODE_B_BACKEND", "llamacpp", "Machine")
        Write-Host "[OK] Set NODE_B_BACKEND=llamacpp (Machine scope)"
    } else {
        [System.Environment]::SetEnvironmentVariable("NODE_B_BACKEND", "ollama", "Machine")
        Write-Host "[OK] Set NODE_B_BACKEND=ollama (Machine scope)"
    }
}

$summary = switch ($Role) {
    "Orchestrator" { "MCP Server → remote Worker at ${PeerIp}:${PeerPort}" }
    "Worker"       { "NodeWorker on port $WorkerPort, Orchestrator at ${PeerIp}:${McpPort}" }
    "Full"         { "MCP Server on port $McpPort + NodeWorker on port $WorkerPort (both local)" }
}
Write-Host "[Config complete] Role=$Role | $summary"
