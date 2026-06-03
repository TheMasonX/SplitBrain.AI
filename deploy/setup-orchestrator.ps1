#Requires -Version 7.0
<#
.SYNOPSIS
    Installs and configures the SplitBrain.AI Orchestrator MCP host as a Windows Service.

.DESCRIPTION
    - Publishes Orchestrator.Mcp in Release mode
    - Creates a Windows Service (if not already present) via sc.exe
    - Generates a starter nodes.json beside the executable
    - Optionally installs Ollama if not already installed

.PARAMETER InstallDir
    Target directory for the published binaries. Defaults to C:\SplitBrain\Orchestrator.

.PARAMETER ServiceName
    Windows Service name. Defaults to SplitBrainOrchestrator.

.PARAMETER ServiceDisplayName
    Human-readable service name shown in services.msc.

.PARAMETER OllamaNodeAUrl
    Base URL for the primary Ollama node (Node A). Defaults to http://localhost:11434.

.PARAMETER OllamaNodeBUrl
    Base URL for the secondary Ollama node (Node B). Leave empty to skip Node B.

.PARAMETER SkipOllamaInstall
    Skip the Ollama installation check.

.EXAMPLE
    .\setup-orchestrator.ps1 -OllamaNodeAUrl http://gpu-host-a:11434 -OllamaNodeBUrl http://gpu-host-b:11434
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]  $InstallDir         = 'C:\SplitBrain\Orchestrator',
    [string]  $ServiceName        = 'SplitBrainOrchestrator',
    [string]  $ServiceDisplayName = 'SplitBrain AI Orchestrator',
    [string]  $OllamaNodeAUrl     = 'http://localhost:11434',
    [string]  $OllamaNodeBUrl     = '',
    [switch]  $SkipOllamaInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot  = Split-Path $PSScriptRoot -Parent
$McpProject = Join-Path $RepoRoot 'src\Orchestrator.Mcp\Orchestrator.Mcp.csproj'

function Write-Step([string]$msg) { Write-Host "  ==> $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  [!!] $msg" -ForegroundColor Yellow }

# ─────────────────────────────────────────────────────────────
# 1. Prerequisites
# ─────────────────────────────────────────────────────────────
Write-Step "Checking prerequisites..."

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install from https://dot.net/download"
}

$dotnetVersion = dotnet --version
Write-OK ".NET SDK $dotnetVersion found."

if (-not $SkipOllamaInstall) {
    if (-not (Get-Command ollama -ErrorAction SilentlyContinue)) {
        Write-Warn "Ollama not found. Attempting installation via winget..."
        if (Get-Command winget -ErrorAction SilentlyContinue) {
            winget install Ollama.Ollama --accept-source-agreements --accept-package-agreements
            Write-OK "Ollama installed."
        } else {
            Write-Warn "winget not available. Install Ollama manually from https://ollama.com/download"
        }
    } else {
        Write-OK "Ollama already installed."
    }
}

# ─────────────────────────────────────────────────────────────
# 2. Build & Publish
# ─────────────────────────────────────────────────────────────
Write-Step "Publishing Orchestrator.Mcp → $InstallDir ..."

if ($PSCmdlet.ShouldProcess($McpProject, "dotnet publish")) {
    dotnet publish $McpProject `
        --configuration Release `
        --output $InstallDir `
        --self-contained false `
        --verbosity minimal

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
    Write-OK "Published to $InstallDir"
}

# ─────────────────────────────────────────────────────────────
# 3. Generate nodes.json
# ─────────────────────────────────────────────────────────────
Write-Step "Generating nodes.json ..."

$nodes = @(
    @{
        NodeId               = "A"
        DisplayName          = "Node A (Primary)"
        Provider             = "Ollama"
        Role                 = "Fast"
        Enabled              = $true
        HealthCheckIntervalMs = 5000
        Ollama               = @{
            BaseUrl        = $OllamaNodeAUrl
            TimeoutSeconds = 120
        }
    }
)

if ($OllamaNodeBUrl) {
    $nodes += @{
        NodeId               = "B"
        DisplayName          = "Node B (Secondary)"
        Provider             = "Ollama"
        Role                 = "Deep"
        Enabled              = $true
        HealthCheckIntervalMs = 5000
        Ollama               = @{
            BaseUrl        = $OllamaNodeBUrl
            TimeoutSeconds = 180
        }
    }
}

$topology = @{ NodeTopology = @{ Nodes = $nodes } }
$nodesJson = Join-Path $InstallDir 'nodes.json'

if ($PSCmdlet.ShouldProcess($nodesJson, "Write nodes.json")) {
    $topology | ConvertTo-Json -Depth 10 | Set-Content $nodesJson -Encoding UTF8
    Write-OK "nodes.json written to $nodesJson"
}

# ─────────────────────────────────────────────────────────────
# 4. Install Windows Service
# ─────────────────────────────────────────────────────────────
Write-Step "Configuring Windows Service '$ServiceName' ..."

$exePath = Join-Path $InstallDir 'Orchestrator.Mcp.exe'

if (-not (Test-Path $exePath)) {
    throw "Executable not found at $exePath — publish may have failed."
}

$existingSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($existingSvc) {
    Write-Warn "Service '$ServiceName' already exists. Stopping and reconfiguring..."
    if ($PSCmdlet.ShouldProcess($ServiceName, "Stop service")) {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    }
    if ($PSCmdlet.ShouldProcess($ServiceName, "sc.exe config")) {
        sc.exe config $ServiceName binPath= "`"$exePath`"" | Out-Null
    }
} else {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Create service")) {
        sc.exe create $ServiceName `
            binPath= "`"$exePath`"" `
            DisplayName= "`"$ServiceDisplayName`"" `
            start= auto | Out-Null
        Write-OK "Service '$ServiceName' created."
    }
}

if ($PSCmdlet.ShouldProcess($ServiceName, "Start service")) {
    Start-Service -Name $ServiceName
    Write-OK "Service '$ServiceName' started."
}

# ─────────────────────────────────────────────────────────────
# 5. Summary
# ─────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  SplitBrain Orchestrator is running!   " -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Service : $ServiceName"
Write-Host "  Install : $InstallDir"
Write-Host "  Node A  : $OllamaNodeAUrl"
if ($OllamaNodeBUrl) { Write-Host "  Node B  : $OllamaNodeBUrl" }
Write-Host ""
Write-Host "  MCP endpoint: http://localhost:5000/mcp" -ForegroundColor White
Write-Host ""
