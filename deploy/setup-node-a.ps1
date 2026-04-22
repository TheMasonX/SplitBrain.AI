<#
.SYNOPSIS
    Provisions Node A (RTX 5060 / Laptop) for SplitBrain.AI.
    Safe to run multiple times - idempotent throughout.

.DESCRIPTION
    1. Verifies / installs .NET 10 SDK + Runtime
    2. Verifies / installs Ollama
    3. Applies Node A Ollama environment variables (machine-scope, persistent)
    4. Pulls required models
    5. Installs the MCP server as a Windows service
    6. Configures log output to a predictable directory

.NOTES
    Run as Administrator.
    Node A role: Interactive + Orchestration (RTX 5060 8 GB, fast inference).
    Primary model: qcoder:latest (qwen2.5-coder 7B Q4_K_M)
#>

#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param (
    [string]$PublishPath   = "$PSScriptRoot\..\src\Orchestrator.Mcp\publish\node-a",
    [string]$ServiceName   = "SplitBrainMcpServer",
    [string]$ServiceDisplay = "SplitBrain.AI MCP Server (Node A)",
    [string]$LogDir        = "C:\ProgramData\SplitBrain.AI\logs\node-a",
    [string]$DotNetVersion = "10"
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
    Write-Ok "Env $Name = $Value"
}

# ---------------------------------------------------------------------------
# 1. .NET SDK
# ---------------------------------------------------------------------------
Write-Step ".NET $DotNetVersion SDK"
$sdkInstalled = (dotnet --list-sdks 2>$null) -match "^$DotNetVersion\."
if (-not $sdkInstalled) {
    Write-Host "    Downloading .NET $DotNetVersion SDK installer..."
    $installerUrl = "https://dot.net/v1/dotnet-install.ps1"
    $installerPath = "$env:TEMP\dotnet-install.ps1"
    Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath
    & $installerPath -Channel $DotNetVersion -InstallDir "C:\Program Files\dotnet" -NoPath
    # Add dotnet install dir to machine PATH if not already present
    $dotnetDir = "C:\Program Files\dotnet"
    $machinePath = [System.Environment]::GetEnvironmentVariable("PATH", "Machine")
    if ($machinePath -notlike "*$dotnetDir*") {
        [System.Environment]::SetEnvironmentVariable("PATH", "$machinePath;$dotnetDir", "Machine")
        $env:PATH = "$env:PATH;$dotnetDir"
        Write-Ok "Added $dotnetDir to machine PATH"
    }
    Write-Ok ".NET $DotNetVersion SDK installed"
} else {
    Write-Skip ".NET $DotNetVersion SDK already present"
}

# ---------------------------------------------------------------------------
# 2. Ollama
# ---------------------------------------------------------------------------
Write-Step "Ollama"
if (-not (Test-CommandExists "ollama")) {
    Write-Host "    Downloading Ollama installer..."
    $ollamaInstaller = "$env:TEMP\OllamaSetup.exe"
    Invoke-WebRequest -Uri "https://ollama.com/download/OllamaSetup.exe" -OutFile $ollamaInstaller
    Start-Process -FilePath $ollamaInstaller -ArgumentList "/S" -Wait
    Write-Ok "Ollama installed"
} else {
    Write-Skip "Ollama already installed"
}

# ---------------------------------------------------------------------------
# 3. Node A Ollama environment variables (from spec section 4.1)
# ---------------------------------------------------------------------------
Write-Step "Ollama environment variables (Node A)"
Set-PersistentEnv "OLLAMA_NUM_PARALLEL"      "2"
Set-PersistentEnv "OLLAMA_MAX_LOADED_MODELS" "1"
Set-PersistentEnv "OLLAMA_FLASH_ATTENTION"   "1"
Set-PersistentEnv "OLLAMA_KV_CACHE_TYPE"     "q8_0"
Set-PersistentEnv "OLLAMA_GPU_LAYERS"        "999"
Set-PersistentEnv "OLLAMA_HOST"              "0.0.0.0"

# ---------------------------------------------------------------------------
# 4. Pull required models
# ---------------------------------------------------------------------------
Write-Step "Pulling Ollama models for Node A"

$models = @(
    "qwen2.5-coder:7b"   # primary inference model (qwen2.5-coder 7B Q4_K_M)
)

# Start Ollama serve in background so pull works if not already running
$ollamaProcess = $null
$ollamaReady = $false
try {
    $resp = Invoke-WebRequest -Uri "http://127.0.0.1:11434" -TimeoutSec 2 -ErrorAction Stop
    $ollamaReady = $resp.StatusCode -eq 200
} catch { }

if (-not $ollamaReady) {
    Write-Host "    Starting Ollama serve temporarily for model pull..."
    $ollamaProcess = Start-Process "ollama" -ArgumentList "serve" -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 5
}

foreach ($model in $models) {
    Write-Host "    Pulling $model ..."
    ollama pull $model
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to pull model '$model' (exit code $LASTEXITCODE)"
    }
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
# 6. Install / update Windows service for MCP server
# ---------------------------------------------------------------------------
Write-Step "Windows service: $ServiceName"

$exePath = Join-Path $PublishPath "Orchestrator.Mcp.exe"

if (-not (Test-Path $exePath)) {
    Write-Warning "Publish output not found at: $exePath"
    Write-Warning "Run the following before registering the service:"
    Write-Warning "  dotnet publish src\Orchestrator.Mcp\Orchestrator.Mcp.csproj /p:PublishProfile=NodeA"
} else {
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

    if ($svc) {
        Write-Host "    Service already exists - stopping to update binaries..."
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        sc.exe config $ServiceName binPath= "`"$exePath`"" | Out-Null
        Write-Ok "Service binary path updated"
    } else {
        sc.exe create $ServiceName binPath= "`"$exePath`"" DisplayName= $ServiceDisplay start= auto | Out-Null
        Write-Ok "Service created: $ServiceName"
    }

    sc.exe description $ServiceName "SplitBrain.AI MCP Server - Node A (RTX 5060, fast inference)" | Out-Null
    Start-Service -Name $ServiceName
    Write-Ok "Service started"
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Host "`n[Node A provisioning complete]" -ForegroundColor Green
Write-Host "  Ollama env vars:  machine-scope (reboot or re-open shell to take effect)"
Write-Host "  Models pulled:    $($models -join ', ')"
Write-Host "  Logs:             $LogDir"
Write-Host "  Service:          $ServiceName"
