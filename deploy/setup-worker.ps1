#Requires -Version 7.0
<#
.SYNOPSIS
    Installs and configures a SplitBrain.AI Node Worker on a GPU host.

.DESCRIPTION
    - Publishes Orchestrator.NodeWorker in Release mode
    - Creates a Windows Service (if not already present)
    - Installs and starts Ollama if not already present
    - Pulls recommended models for the worker role

.PARAMETER InstallDir
    Target directory for the published binaries. Defaults to C:\SplitBrain\NodeWorker.

.PARAMETER ServiceName
    Windows Service name. Defaults to SplitBrainNodeWorker.

.PARAMETER NodeId
    Logical node ID used by the orchestrator (e.g. A, B). Defaults to A.

.PARAMETER OllamaBaseUrl
    Ollama API URL this worker exposes. Defaults to http://localhost:11434.

.PARAMETER OrchestratorUrl
    URL of the orchestrator MCP host. Used to register this worker. Defaults to http://localhost:5000.

.PARAMETER Models
    Comma-separated list of Ollama model tags to pull after setup.
    Defaults to "qwen2.5-coder:7b,nomic-embed-text".

.PARAMETER SkipOllamaInstall
    Skip the Ollama installation/model-pull step.

.EXAMPLE
    .\setup-worker.ps1 -NodeId B -Models "qwen2.5-coder:14b,deepseek-r1:8b"
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]  $InstallDir        = 'C:\SplitBrain\NodeWorker',
    [string]  $ServiceName       = 'SplitBrainNodeWorker',
    [string]  $NodeId            = 'A',
    [string]  $OllamaBaseUrl     = 'http://localhost:11434',
    [string]  $OrchestratorUrl   = 'http://localhost:5000',
    [string]  $Models            = 'qwen2.5-coder:7b,nomic-embed-text',
    [switch]  $SkipOllamaInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot       = Split-Path $PSScriptRoot -Parent
$WorkerProject  = Join-Path $RepoRoot 'src\Orchestrator.NodeWorker\Orchestrator.NodeWorker.csproj'

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

Write-OK ".NET SDK $(dotnet --version) found."

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
        Write-OK "Ollama already installed: $(ollama --version)"
    }

    # Ensure Ollama service is running before pulling models
    Write-Step "Starting Ollama service..."
    Start-Process ollama -ArgumentList 'serve' -WindowStyle Hidden -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3

    # Pull requested models
    $modelList = $Models -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    foreach ($model in $modelList) {
        Write-Step "Pulling model: $model ..."
        if ($PSCmdlet.ShouldProcess($model, "ollama pull")) {
            ollama pull $model
            if ($LASTEXITCODE -eq 0) { Write-OK "Model '$model' ready." }
            else { Write-Warn "Pull for '$model' returned exit code $LASTEXITCODE — continuing." }
        }
    }
}

# ─────────────────────────────────────────────────────────────
# 2. Build & Publish
# ─────────────────────────────────────────────────────────────
Write-Step "Publishing Orchestrator.NodeWorker → $InstallDir ..."

if ($PSCmdlet.ShouldProcess($WorkerProject, "dotnet publish")) {
    dotnet publish $WorkerProject `
        --configuration Release `
        --output $InstallDir `
        --self-contained false `
        --verbosity minimal

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
    Write-OK "Published to $InstallDir"
}

# ─────────────────────────────────────────────────────────────
# 3. Generate appsettings.Production.json
# ─────────────────────────────────────────────────────────────
Write-Step "Writing worker configuration..."

$config = @{
    NodeWorker = @{
        NodeId            = $NodeId
        OllamaBaseUrl     = $OllamaBaseUrl
        OrchestratorUrl   = $OrchestratorUrl
    }
    Logging = @{
        LogLevel = @{
            Default   = "Information"
            Microsoft = "Warning"
        }
    }
}

$configPath = Join-Path $InstallDir 'appsettings.Production.json'
if ($PSCmdlet.ShouldProcess($configPath, "Write config")) {
    $config | ConvertTo-Json -Depth 10 | Set-Content $configPath -Encoding UTF8
    Write-OK "Config written to $configPath"
}

# ─────────────────────────────────────────────────────────────
# 4. Install Windows Service
# ─────────────────────────────────────────────────────────────
Write-Step "Configuring Windows Service '$ServiceName' ..."

$exePath = Join-Path $InstallDir 'Orchestrator.NodeWorker.exe'

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
            DisplayName= "`"SplitBrain Node Worker ($NodeId)`"" `
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
Write-Host "  SplitBrain Node Worker ($NodeId) ready!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Service       : $ServiceName"
Write-Host "  Install       : $InstallDir"
Write-Host "  Ollama URL    : $OllamaBaseUrl"
Write-Host "  Orchestrator  : $OrchestratorUrl"
if (-not $SkipOllamaInstall) {
    Write-Host "  Models        : $Models"
}
Write-Host ""
