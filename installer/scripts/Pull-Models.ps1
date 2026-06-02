<#
.SYNOPSIS
    Pulls Ollama models required for the selected SplitBrain.AI role.

.DESCRIPTION
    Called by the Inno Setup [Run] section when the "Pull Ollama models" task is selected.
    Starts Ollama temporarily if not already running.

.PARAMETER Role
    "NodeA" or "NodeB"

.PARAMETER Backend
    "ollama" or "llamacpp" (Node B only). If llamacpp, skips Ollama model pull.
#>
param(
    [ValidateSet("NodeA","NodeB")] [string] $Role    = "NodeA",
    [ValidateSet("ollama","llamacpp")] [string] $Backend = "ollama"
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Msg) Write-Host "`n==> $Msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Msg) Write-Host "    [OK] $Msg" -ForegroundColor Green }

# Skip model pull if using llama.cpp backend on Node B
if ($Role -eq "NodeB" -and $Backend -eq "llamacpp") {
    Write-Host "[--] llama.cpp backend selected — Ollama models not required."
    Write-Host "     See data\Pages\guides\llamacpp-gtx1080-setup.md for GGUF model download instructions."
    exit 0
}

# Model sets per role
$modelSets = @{
    NodeA = @(
        @{ Name = "qwen2.5-coder:7b";           Desc = "Qwen 2.5 Coder 7B (primary — Node A fast inference)" }
    )
    NodeB = @(
        @{ Name = "qwen2.5-coder:7b-instruct-q5_K_M"; Desc = "Qwen 2.5 Coder 7B Q5 (Node B primary)" },
        @{ Name = "deepseek-coder:6.7b-instruct-q4_K_M"; Desc = "DeepSeek Coder 6.7B Q4 (Node B fallback)" }
    )
}

$models = $modelSets[$Role]

# Ensure Ollama is reachable — start temporarily if needed
Write-Step "Checking Ollama"
$ollamaProcess = $null
try {
    $resp = Invoke-WebRequest -Uri "http://127.0.0.1:11434" -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
    Write-Ok "Ollama is already running"
} catch {
    Write-Host "    Ollama not responding — starting temporarily..."
    $ollamaProcess = Start-Process "ollama" -ArgumentList "serve" -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 6

    # Verify it started
    $retries = 0
    $ready = $false
    while ($retries -lt 10 -and -not $ready) {
        try {
            $null = Invoke-WebRequest -Uri "http://127.0.0.1:11434" -TimeoutSec 2 -UseBasicParsing -ErrorAction Stop
            $ready = $true
        } catch {
            $retries++
            Start-Sleep -Seconds 2
        }
    }
    if (-not $ready) {
        Write-Warning "Ollama did not start in time. Pull models manually: ollama pull <model>"
        exit 2
    }
    Write-Ok "Ollama started"
}

# Pull models
Write-Step "Pulling models for $Role"
$failed = @()
foreach ($m in $models) {
    Write-Host "    Pulling $($m.Name) — $($m.Desc)"
    ollama pull $m.Name
    if ($LASTEXITCODE -eq 0) {
        Write-Ok "Pulled $($m.Name)"
    } else {
        $failed += $m.Name
        Write-Warning "Failed to pull $($m.Name) (exit $LASTEXITCODE) — retry manually: ollama pull $($m.Name)"
    }
}

# Clean up temporary Ollama process
if ($ollamaProcess) {
    Stop-Process -Id $ollamaProcess.Id -Force -ErrorAction SilentlyContinue
}

if ($failed.Count -gt 0) {
    Write-Warning "Some models failed to pull: $($failed -join ', ')"
    Write-Warning "Run these manually after installation:"
    $failed | ForEach-Object { Write-Host "    ollama pull $_" }
    exit 2
}

Write-Host ""
Write-Host "[Models ready]" -ForegroundColor Green
ollama list
