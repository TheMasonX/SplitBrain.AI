<#
.SYNOPSIS
    Provisions Node B (GTX 1080 / Tower) for SplitBrain.AI.
    Safe to run multiple times — idempotent throughout.

.DESCRIPTION
    1. Verifies / installs .NET 10 Runtime (no SDK needed on Node B)
    2. Verifies / installs Ollama
    3. Applies Node B Ollama environment variables (machine-scope, persistent)
    4. Pulls required models for deep inference
    5. Installs the NodeWorker as a Windows service
    6. Configures log output to a predictable directory

.NOTES
    Run as Administrator on Node B (GTX 1080 8 GB, Pascal).
    Flash attention DISABLED for Pascal stability.
    Single-parallel enforced — lower context to avoid VRAM fragmentation.
    Node B role: Deep inference + validation.
#>

#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param (
    [string]$PublishPath    = "$PSScriptRoot\..\publish\node-b",
    [string]$ServiceName    = "SplitBrainNodeWorker",
    [string]$ServiceDisplay = "SplitBrain.AI Node Worker (Node B)",
    [string]$LogDir         = "C:\ProgramData\SplitBrain.AI\logs\node-b",
    [string]$DotNetVersion  = "10.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Write-Step { param([string]$Msg) Write-Host "`n==> $Msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Msg) Write-Host "    [OK] $Msg" -ForegroundColor Green }
function Write-Skip { param([string]$Msg) Write-Host "    [--] $Msg" -ForegroundColor DarkGray }

function Test-CommandExists([string]$Cmd) {
    $null -ne (Get-Command $Cmd -ErrorAction SilentlyContinue)
}

function Set-PersistentEnv([string]$Name, [string]$Value) {
    $current = [System.Environment]::GetEnvironmentVariable($Name, "Machine")
    if ($current -eq $Value) {
        Write-Skip "Env $Name already set to '$Value'"
        return
    }
    [System.Environment]::SetEnvironmentVariable($Name, $Value, "Machine")
    $env:($Name) = $Value
    Write-Ok "Env $Name = $Value"
}

# ---------------------------------------------------------------------------
# 1. .NET Runtime (runtime only — Node B does not need the SDK)
# ---------------------------------------------------------------------------
Write-Step ".NET $DotNetVersion Runtime"
$runtimeInstalled = (dotnet --list-runtimes 2>$null) -match "^Microsoft\.NETCore\.App $DotNetVersion\."
if (-not $runtimeInstalled) {
    Write-Host "    Downloading .NET $DotNetVersion Runtime installer..."
    $installerPath = "$env:TEMP\dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installerPath -UseBasicParsing
    & $installerPath -Channel $DotNetVersion -Runtime dotnet -InstallDir "C:\Program Files\dotnet" -NoPath
    Write-Ok ".NET $DotNetVersion Runtime installed"
} else {
    Write-Skip ".NET $DotNetVersion Runtime already present"
}

# ---------------------------------------------------------------------------
# 2. Ollama
# ---------------------------------------------------------------------------
Write-Step "Ollama"
if (-not (Test-CommandExists "ollama")) {
    Write-Host "    Downloading Ollama installer..."
    $ollamaInstaller = "$env:TEMP\OllamaSetup.exe"
    Invoke-WebRequest -Uri "https://ollama.com/download/OllamaSetup.exe" -OutFile $ollamaInstaller -UseBasicParsing
    Start-Process -FilePath $ollamaInstaller -ArgumentList "/S" -Wait
    Write-Ok "Ollama installed"
} else {
    Write-Skip "Ollama already installed"
}

# ---------------------------------------------------------------------------
# 3. Node B Ollama environment variables (from spec §4.2)
#    Flash attention DISABLED — Pascal (GTX 1080) instability.
#    Single parallel to prevent VRAM fragmentation.
# ---------------------------------------------------------------------------
Write-Step "Ollama environment variables (Node B)"
Set-PersistentEnv "OLLAMA_NUM_PARALLEL"      "1"
Set-PersistentEnv "OLLAMA_MAX_LOADED_MODELS" "1"
Set-PersistentEnv "OLLAMA_FLASH_ATTENTION"   "0"
Set-PersistentEnv "OLLAMA_KV_CACHE_TYPE"     "q8_0"
Set-PersistentEnv "OLLAMA_GPU_LAYERS"        "999"
Set-PersistentEnv "OLLAMA_HOST"              "0.0.0.0"

# ---------------------------------------------------------------------------
# 4. Pull required models
# ---------------------------------------------------------------------------
Write-Step "Pulling Ollama models for Node B"

$models = @(
    "qwen2.5-coder:7b-instruct-q5_K_M",   # primary deep-inference model
    "deepseek-coder:6.7b-instruct-q4_K_M"  # fallback model
)

$ollamaProcess = $null
if (-not (Test-NetConnection -ComputerName localhost -Port 11434 -InformationLevel Quiet -WarningAction SilentlyContinue)) {
    Write-Host "    Starting Ollama serve temporarily for model pull..."
    $ollamaProcess = Start-Process "ollama" -ArgumentList "serve" -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 5
}

foreach ($model in $models) {
    Write-Host "    Pulling $model ..."
    ollama pull $model
    Write-Ok "Pulled $model"
}

if ($ollamaProcess) {
    Stop-Process -Id $ollamaProcess.Id -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------
# 5. Ensure log directory exists
# ---------------------------------------------------------------------------
Write-Step "Log directory"
if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
    Write-Ok "Created $LogDir"
} else {
    Write-Skip "Log directory already exists: $LogDir"
}

# ---------------------------------------------------------------------------
# 6. Install / update Windows service for Node Worker
# ---------------------------------------------------------------------------
Write-Step "Windows service: $ServiceName"

$exePath = Join-Path $PublishPath "Orchestrator.NodeWorker.exe"

if (-not (Test-Path $exePath)) {
    Write-Warning "Publish output not found at: $exePath"
    Write-Warning "Run the following on Node A before copying to Node B:"
    Write-Warning "  dotnet publish src\Orchestrator.NodeWorker\Orchestrator.NodeWorker.csproj /p:PublishProfile=NodeB"
    Write-Warning "Then copy the publish\node-b\ folder to this machine and re-run this script."
} else {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if ($svc) {
        Write-Host "    Service already exists — stopping to update binaries..."
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        sc.exe config $ServiceName binPath= "`"$exePath`"" | Out-Null
        Write-Ok "Service binary path updated"
    } else {
        sc.exe create $ServiceName `
            binPath= "`"$exePath`"" `
            DisplayName= $ServiceDisplay `
            start= auto | Out-Null
        Write-Ok "Service created: $ServiceName"
    }

    sc.exe description $ServiceName "SplitBrain.AI Node Worker — Node B (GTX 1080, deep inference)" | Out-Null
    Start-Service -Name $ServiceName
    Write-Ok "Service started"
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Host "`n[Node B provisioning complete]" -ForegroundColor Green
Write-Host "  Ollama env vars:  machine-scope (reboot or re-open shell to take effect)"
Write-Host "  Models pulled:    $($models -join ', ')"
Write-Host "  Logs:             $LogDir"
Write-Host "  Service:          $ServiceName"
