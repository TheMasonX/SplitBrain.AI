<#
.SYNOPSIS
    Sets up the llama.cpp inference server for SplitBrain.AI.
    Supports both native Windows binary (no Docker) and Docker-based deployments.

.DESCRIPTION
    Called by the Inno Setup [Run] section when the llama.cpp backend is selected.

    Native mode:
      - Downloads llama-server.exe (CUDA 12.4 build) from GitHub Releases
      - Extracts to {InstallDir}\llama.cpp\
      - Creates launch scripts: Start-LlamaCpp.ps1 and start-llamacpp.bat
      - Optionally registers a Windows Scheduled Task to auto-start at login

    Docker mode:
      - Verifies Docker Desktop + NVIDIA Container Toolkit are accessible
      - Creates a PowerShell launch script wrapping the Docker run command
      - Optionally registers a Windows Scheduled Task to auto-start at login

    In both modes, the script does NOT start the server immediately — it only
    prepares launch artifacts. The user (or scheduled task) runs them separately.

    SplitBrain.AI connects to: http://localhost:{LlamaCppPort}/v1/chat/completions
    The C# NodeClient does not care whether the endpoint is native or Docker.

.PARAMETER InstallDir
    SplitBrain.AI root installation directory.

.PARAMETER Mode
    "native" — download and run llama-server.exe directly (no Docker required)
    "docker" — use the ghcr.io/ggml-org/llama.cpp:server-cuda container

.PARAMETER LlamaCppPort
    Port llama-server listens on (default 8080).

.PARAMETER ModelPath
    Path to the GGUF model file (used in the generated launch script).
    Native: local Windows path, e.g. C:\Models\qwen3-coder-30b-a3b.gguf
    Docker: path on host — will be mounted into the container at /models/

.PARAMETER NcpuMoe
    --n-cpu-moe value for MoE offloading (default 35 for GTX 1080 6 GB baseline;
    reduce to ~25 for GTX 1080 8 GB).

.PARAMETER GpuLayers
    --n-gpu-layers value (default 999 = offload all non-MoE layers to GPU).

.PARAMETER ExtraFlags
    Additional llama-server flags appended verbatim to the launch command.

.PARAMETER RegisterStartup
    If set, registers a Windows Scheduled Task to launch llama-server at login.
    Requires Administrator.

.PARAMETER CudaBuild
    Which CUDA build to download for native mode.
    "12.4" (default, broadest driver support) or "13.3" (newest drivers only).
#>

#Requires -Version 5.1
param(
    [Parameter(Mandatory)] [string] $InstallDir,
    [ValidateSet("native","docker")] [string] $Mode = "native",
    [int]    $LlamaCppPort   = 8080,
    [string] $ModelPath      = "C:\\Models\\qwen3-coder-30b-a3b.gguf",
    [int]    $NcpuMoe        = 35,
    [int]    $GpuLayers      = 999,
    [string] $ExtraFlags     = "",
    [switch] $RegisterStartup,
    [ValidateSet("12.4","13.3")] [string] $CudaBuild = "12.4"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step { param([string]$Msg) Write-Host "`n==> $Msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Msg) Write-Host "    [OK] $Msg" -ForegroundColor Green }
function Write-Skip { param([string]$Msg) Write-Host "    [--] $Msg" -ForegroundColor DarkGray }
function Write-Warn { param([string]$Msg) Write-Host "    [!]  $Msg" -ForegroundColor Yellow }

$LlamaCppDir   = Join-Path $InstallDir "llama.cpp"
$ScriptsOutDir = Join-Path $InstallDir "llama.cpp-launch"

# Standard flags for GTX 1080 (Pascal) with Qwen3 30B A3B
$StandardFlags = "--n-gpu-layers $GpuLayers --n-cpu-moe $NcpuMoe --no-mmap --mlock --cache-type-k turbo4 --cache-type-v turbo3 --host 0.0.0.0 --port $LlamaCppPort"
if ($ExtraFlags) { $StandardFlags += " $ExtraFlags" }

New-Item -ItemType Directory -Force -Path $ScriptsOutDir | Out-Null

# ═══════════════════════════════════════════════════════════════════════════════
#  NATIVE mode — download llama-server.exe for Windows
# ═══════════════════════════════════════════════════════════════════════════════
if ($Mode -eq "native") {
    Write-Step "Setting up llama.cpp (native Windows binary, CUDA $CudaBuild)"

    $ZipName    = "cudart-llama-bin-win-cuda-${CudaBuild}-x64.zip"
    $ReleasesUrl = "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest"

    # Resolve download URL from latest release
    Write-Host "    Fetching latest llama.cpp release info..."
    try {
        $releaseJson = Invoke-RestMethod -Uri $ReleasesUrl -Headers @{ "User-Agent" = "SplitBrainAI-Installer" } -TimeoutSec 20
        $asset = $releaseJson.assets | Where-Object { $_.name -like "cudart-llama-bin-win-cuda-${CudaBuild}-x64.zip" } | Select-Object -First 1

        if (-not $asset) {
            # Fallback: try alternative naming
            $asset = $releaseJson.assets | Where-Object { $_.name -like "*win*cuda*${CudaBuild}*x64*.zip" } | Select-Object -First 1
        }

        if (-not $asset) {
            throw "Could not find $ZipName in latest release. Available assets: $($releaseJson.assets.name -join ', ')"
        }

        $downloadUrl = $asset.browser_download_url
        Write-Ok "Found: $($asset.name) ($([math]::Round($asset.size/1MB,0)) MB)"
    } catch {
        # Fallback to a pinned known-good release URL
        $Tag = "b5695"   # approximate pinned tag — user should update if stale
        $downloadUrl = "https://github.com/ggml-org/llama.cpp/releases/download/$Tag/$ZipName"
        Write-Warn "Failed to fetch release API ($($_.Exception.Message)). Using fallback URL."
        Write-Warn "If download fails, manually download $ZipName from:"
        Write-Warn "  https://github.com/ggml-org/llama.cpp/releases"
    }

    # Download
    $zipPath = Join-Path $env:TEMP $ZipName
    Write-Host "    Downloading $ZipName (~373 MB)..."
    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -TimeoutSec 600 -UseBasicParsing
        Write-Ok "Downloaded to $zipPath"
    } catch {
        Write-Warn "Download failed: $($_.Exception.Message)"
        Write-Warn "Please manually download $ZipName and extract to $LlamaCppDir"
        Write-Warn "Then run Start-LlamaCpp.ps1 from $ScriptsOutDir"
        # Still write launch scripts so user knows the flags
        $extractOk = $false
    }

    # Extract
    $extractOk = $true
    if (Test-Path $zipPath) {
        New-Item -ItemType Directory -Force -Path $LlamaCppDir | Out-Null
        Write-Host "    Extracting to $LlamaCppDir ..."
        try {
            Expand-Archive -Path $zipPath -DestinationPath $LlamaCppDir -Force
            Write-Ok "Extracted"
        } catch {
            Write-Warn "Extraction failed: $($_.Exception.Message)"
            $extractOk = $false
        }
        Remove-Item $zipPath -ErrorAction SilentlyContinue
    }

    # Locate llama-server.exe
    $serverExe = Get-ChildItem -Path $LlamaCppDir -Filter "llama-server.exe" -Recurse |
                 Select-Object -First 1
    if ($serverExe) {
        $ServerPath = $serverExe.FullName
        Write-Ok "llama-server.exe: $ServerPath"
    } else {
        $ServerPath = Join-Path $LlamaCppDir "llama-server.exe"
        Write-Warn "llama-server.exe not found at $LlamaCppDir — expected after manual extraction."
    }

    # ── Verify flags are supported ─────────────────────────────────────────────
    if (Test-Path $ServerPath) {
        Write-Host "    Checking --n-cpu-moe support..."
        $helpOutput = & $ServerPath --help 2>&1 | Out-String
        if ($helpOutput -notmatch 'n-cpu-moe') {
            Write-Warn "--n-cpu-moe not found in this build. MoE offloading may not be available."
            Write-Warn "Try a newer llama.cpp release. Removing --n-cpu-moe from launch script."
            $StandardFlags = $StandardFlags -replace "--n-cpu-moe $NcpuMoe", ""
        }
        foreach ($flag in @("cache-type-k","cache-type-v")) {
            if ($helpOutput -notmatch $flag) {
                Write-Warn "--$flag not found — TurboQuant KV cache not in this build."
                $StandardFlags = $StandardFlags -replace "--$flag [a-z0-9]+", ""
            }
        }
        Write-Ok "Flag check complete"
    }

    # ── Write native launch scripts ────────────────────────────────────────────
    $launchPs1 = Join-Path $ScriptsOutDir "Start-LlamaCpp.ps1"
    $launchBat = Join-Path $ScriptsOutDir "start-llamacpp.bat"

    @"
<#
.SYNOPSIS
    Starts the llama.cpp inference server (native Windows binary).
    Edit `ModelPath` to point at your GGUF file.
    Edit `NcpuMoe` to tune MoE offloading for your VRAM.

    For GTX 1080 8 GB with Qwen3-Coder 30B A3B:
      - Start with: --n-cpu-moe 25   (lower = more experts on GPU = faster but more VRAM)
      - Monitor:   watch nvidia-smi VRAM; reduce --n-cpu-moe until OOM, then add 2-3 back

    Server listens at: http://localhost:$LlamaCppPort
    SplitBrain.AI connects to: http://localhost:$LlamaCppPort/v1/chat/completions
#>

`$ServerExe  = '$ServerPath'
`$ModelPath  = '$ModelPath'
`$LlamaCppPort = $LlamaCppPort
`$NcpuMoe   = $NcpuMoe       # reduce to ~25 for GTX 1080 8 GB; 35 is for 6 GB
`$GpuLayers = $GpuLayers

# Verify the server binary exists
if (-not (Test-Path `$ServerExe)) {
    Write-Error "llama-server.exe not found at `$ServerExe. Ensure the CUDA zip was extracted correctly."
    Read-Host "Press Enter to exit"
    exit 1
}

# Verify the model file exists
if (-not (Test-Path `$ModelPath)) {
    Write-Warning "Model file not found: `$ModelPath"
    Write-Warning "Download your GGUF model and update the `$ModelPath variable in this script."
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Starting llama.cpp server..." -ForegroundColor Cyan
Write-Host "  Server:  `$ServerExe" -ForegroundColor Gray
Write-Host "  Model:   `$ModelPath" -ForegroundColor Gray
Write-Host "  Port:    `$LlamaCppPort" -ForegroundColor Gray
Write-Host ""

& `$ServerExe ``
    --model   "`$ModelPath" ``
    --n-gpu-layers `$GpuLayers ``
    --n-cpu-moe    `$NcpuMoe ``
    --no-mmap ``
    --mlock ``
    --cache-type-k turbo4 ``
    --cache-type-v turbo3 ``
    --host 0.0.0.0 ``
    --port `$LlamaCppPort

# If the server exits, pause so the user can see error output
if (`$LASTEXITCODE -ne 0) {
    Write-Error "llama-server exited with code `$LASTEXITCODE"
    Read-Host "Press Enter to exit"
}
"@ | Set-Content -Path $launchPs1 -Encoding UTF8
    Write-Ok "Created: $launchPs1"

    @"
@echo off
REM SplitBrain.AI — llama.cpp native server launcher
REM Edit ModelPath and flags as needed for your hardware
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0Start-LlamaCpp.ps1"
pause
"@ | Set-Content -Path $launchBat -Encoding ASCII
    Write-Ok "Created: $launchBat"
}

# ═══════════════════════════════════════════════════════════════════════════════
#  DOCKER mode — NVIDIA Container Toolkit + Docker Desktop
# ═══════════════════════════════════════════════════════════════════════════════
if ($Mode -eq "docker") {
    Write-Step "Setting up llama.cpp (Docker mode)"

    # ── Verify Docker is available ─────────────────────────────────────────────
    $dockerCmd = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $dockerCmd) {
        Write-Warn "Docker not found in PATH. Install Docker Desktop from https://docs.docker.com/desktop/windows/"
        Write-Warn "After installing Docker, re-run this script or use Start-LlamaCpp-Docker.ps1 manually."
    } else {
        Write-Ok "Docker: $($dockerCmd.Source)"
        # Quick GPU test
        Write-Host "    Testing NVIDIA GPU access in Docker..."
        $gpuTest = docker run --rm --gpus all "nvidia/cuda:12.0-base-ubuntu20.04" nvidia-smi 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Ok "GPU accessible in Docker"
        } else {
            Write-Warn "GPU test failed. NVIDIA Container Toolkit may not be installed."
            Write-Warn "Install from: https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/install-guide.html"
            Write-Warn "Docker launch script will still be created."
        }
    }

    # ── Write Docker launch script ─────────────────────────────────────────────
    $modelDir  = Split-Path $ModelPath -Parent
    $modelFile = Split-Path $ModelPath -Leaf

    # Normalise Windows path for Docker volume mount
    $modelDirDocker = $modelDir -replace '\\','/' -replace '^([A-Z]):', '/$1'

    $launchPs1 = Join-Path $ScriptsOutDir "Start-LlamaCpp-Docker.ps1"
    $launchBat = Join-Path $ScriptsOutDir "start-llamacpp-docker.bat"

    @"
<#
.SYNOPSIS
    Starts the llama.cpp inference server via Docker.
    Edit ModelDir/ModelFile and NcpuMoe for your hardware.

    For GTX 1080 8 GB with Qwen3-Coder 30B A3B:
      - Start with: `$NcpuMoe = 25  (lower = more experts on GPU = faster but more VRAM)
      - Monitor:   nvidia-smi in another terminal

    Server listens at: http://localhost:$LlamaCppPort
    SplitBrain.AI connects to: http://localhost:$LlamaCppPort/v1/chat/completions

    Requires: Docker Desktop + NVIDIA Container Toolkit
    Test GPU access first: docker run --rm --gpus all nvidia/cuda:12.0-base nvidia-smi
#>

`$ModelDir   = '$modelDir'       # Host folder containing your GGUF files
`$ModelFile  = '$modelFile'       # GGUF filename (no path)
`$Port       = $LlamaCppPort
`$NcpuMoe   = $NcpuMoe            # reduce to ~25 for GTX 1080 8 GB
`$GpuLayers = $GpuLayers

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error "Docker not found. Install Docker Desktop from https://docs.docker.com/desktop/windows/"
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Starting llama.cpp server (Docker)..." -ForegroundColor Cyan
Write-Host "  Model dir: `$ModelDir" -ForegroundColor Gray
Write-Host "  Model:     `$ModelFile" -ForegroundColor Gray
Write-Host "  Port:      `$Port" -ForegroundColor Gray
Write-Host ""

docker run --rm ``
    --gpus all ``
    --cap-add=IPC_LOCK ``
    -p "`${Port}:`${Port}" ``
    -v "`${ModelDir}:/models" ``
    ghcr.io/ggml-org/llama.cpp:server-cuda ``
    --model "/models/`$ModelFile" ``
    --n-gpu-layers `$GpuLayers ``
    --n-cpu-moe    `$NcpuMoe ``
    --no-mmap ``
    --mlock ``
    --cache-type-k turbo4 ``
    --cache-type-v turbo3 ``
    --host 0.0.0.0 ``
    --port `$Port
"@ | Set-Content -Path $launchPs1 -Encoding UTF8
    Write-Ok "Created: $launchPs1"

    @"
@echo off
REM SplitBrain.AI — llama.cpp Docker server launcher
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0Start-LlamaCpp-Docker.ps1"
pause
"@ | Set-Content -Path $launchBat -Encoding ASCII
    Write-Ok "Created: $launchBat"
}

# ═══════════════════════════════════════════════════════════════════════════════
#  COMMON — optional Windows Scheduled Task for auto-start at login
# ═══════════════════════════════════════════════════════════════════════════════
if ($RegisterStartup) {
    Write-Step "Registering Windows Scheduled Task (auto-start at login)"

    $taskName = "SplitBrain.AI llama.cpp Server"
    $scriptFile = if ($Mode -eq "docker") {
        Join-Path $ScriptsOutDir "Start-LlamaCpp-Docker.ps1"
    } else {
        Join-Path $ScriptsOutDir "Start-LlamaCpp.ps1"
    }

    # Remove existing task if present
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

    $action = New-ScheduledTaskAction `
        -Execute "powershell.exe" `
        -Argument "-WindowStyle Hidden -ExecutionPolicy Bypass -NoProfile -File `"$scriptFile`""

    $trigger = New-ScheduledTaskTrigger -AtLogOn

    $settings = New-ScheduledTaskSettingsSet `
        -ExecutionTimeLimit (New-TimeSpan -Hours 0) `
        -MultipleInstances IgnoreNew `
        -StartWhenAvailable

    Register-ScheduledTask `
        -TaskName $taskName `
        -Action   $action `
        -Trigger  $trigger `
        -Settings $settings `
        -RunLevel Highest `
        -Description "SplitBrain.AI: starts the llama.cpp inference server at login" | Out-Null

    Write-Ok "Scheduled Task registered: '$taskName'"
    Write-Host "    The server will auto-start on next login."
    Write-Host "    To start now: Start-ScheduledTask -TaskName '$taskName'"
}

# ═══════════════════════════════════════════════════════════════════════════════
#  Summary
# ═══════════════════════════════════════════════════════════════════════════════
Write-Host ""
Write-Host "[llama.cpp setup complete]" -ForegroundColor Green
Write-Host "  Mode:        $Mode"
Write-Host "  Port:        $LlamaCppPort"
Write-Host "  Launch dir:  $ScriptsOutDir"
Write-Host ""
if ($Mode -eq "native") {
    Write-Host "  Next steps:"
    Write-Host "    1. Download your GGUF model (e.g. Qwen3-Coder-30B-A3B-Q4_K_M.gguf)"
    Write-Host "       and place it at: $ModelPath"
    Write-Host "    2. Edit `$ModelPath in: $ScriptsOutDir\Start-LlamaCpp.ps1"
    Write-Host "    3. Run: $ScriptsOutDir\start-llamacpp.bat"
    Write-Host "    4. Verify: curl http://localhost:$LlamaCppPort/health"
} else {
    Write-Host "  Next steps:"
    Write-Host "    1. Ensure Docker Desktop is running"
    Write-Host "    2. Place your GGUF model at: $ModelPath"
    Write-Host "    3. Run: $ScriptsOutDir\start-llamacpp-docker.bat"
    Write-Host "    4. Verify: curl http://localhost:$LlamaCppPort/health"
}
Write-Host ""
Write-Host "  SplitBrain.AI will connect to: http://localhost:$LlamaCppPort"
Write-Host "  (The C# NodeClient.LlamaCpp doesn't care if it's native or Docker)"
