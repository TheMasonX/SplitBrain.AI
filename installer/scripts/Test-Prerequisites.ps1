<#
.SYNOPSIS
    Pre-installation prerequisite check for SplitBrain.AI.

.DESCRIPTION
    Checks .NET runtime, Ollama, disk space, GPU, and network.
    Returns exit code 0 if all required checks pass, 1 if any required check fails.
    Warnings (non-fatal) set exit code 2.

.PARAMETER Role
    "NodeA" or "NodeB".

.PARAMETER OutputFile
    Optional path to write JSON results for use by the Inno Setup [Code] section.
#>
param(
    [ValidateSet("Orchestrator","Worker","Full","NodeA","NodeB")] [string] $Role = "Orchestrator",
    [string] $OutputFile = ""
)

# Normalise legacy names
if ($Role -eq "NodeA") { $Role = "Orchestrator" }
if ($Role -eq "NodeB") { $Role = "Worker" }

$results = @{
    Passed   = @()
    Warnings = @()
    Failed   = @()
}

function Pass([string]$msg)    { $results.Passed   += $msg; Write-Host "[PASS] $msg" -ForegroundColor Green }
function Warn([string]$msg)    { $results.Warnings += $msg; Write-Host "[WARN] $msg" -ForegroundColor Yellow }
function Fail([string]$msg)    { $results.Failed   += $msg; Write-Host "[FAIL] $msg" -ForegroundColor Red }

# ── .NET Runtime ──────────────────────────────────────────────────────────────
$runtimes = dotnet --list-runtimes 2>$null
if ($runtimes -match "^Microsoft\.NETCore\.App 10\.") {
    Pass ".NET 10 runtime is installed"
} elseif ($runtimes -match "^Microsoft\.NETCore\.App (\d+)") {
    Warn ".NET 10 not found (have: $($Matches[1])). Installer will download .NET 10."
} else {
    Warn ".NET not found. Installer will download .NET 10."
}

# ── Ollama ────────────────────────────────────────────────────────────────────
$ollamaCmd = Get-Command ollama -ErrorAction SilentlyContinue
if ($ollamaCmd) {
    Pass "Ollama is installed ($($ollamaCmd.Source))"
} else {
    Warn "Ollama not found. Installer will download and install Ollama."
}

# ── Disk space (require 5 GB free for models + binaries) ─────────────────────
$drive = (Get-PSDrive C).Free
$freeGB = [math]::Round($drive / 1GB, 1)
if ($freeGB -ge 10) {
    Pass "Disk space: $freeGB GB free on C: (≥ 10 GB recommended)"
} elseif ($freeGB -ge 5) {
    Warn "Disk space: $freeGB GB free on C: (10 GB recommended for models)"
} else {
    Fail "Disk space: only $freeGB GB free on C: — at least 5 GB required"
}

# ── GPU detection ─────────────────────────────────────────────────────────────
$gpuInfo = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match "NVIDIA|AMD|Radeon" } |
    Select-Object -First 1

if ($gpuInfo) {
    $vramGB = [math]::Round($gpuInfo.AdapterRAM / 1GB, 1)
    if ($vramGB -ge 8) {
        Pass "GPU: $($gpuInfo.Name) ($vramGB GB VRAM)"
    } elseif ($vramGB -ge 4) {
        Warn "GPU: $($gpuInfo.Name) ($vramGB GB VRAM) — 8 GB recommended for large models"
    } else {
        Warn "GPU: $($gpuInfo.Name) ($vramGB GB VRAM) — may be insufficient for local inference"
    }
} else {
    Warn "No dedicated GPU detected — local inference may be slow (CPU fallback)"
}

# ── NVIDIA driver (Worker or Full — need CUDA for inference) ─────────────────
if ($Role -in @("Worker","Full")) {
    $nvidiaInfo = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "NVIDIA" } | Select-Object -First 1
    if ($nvidiaInfo) {
        # Driver version in Windows CIM is reported as e.g. "31.0.15.5185"
        # NVIDIA convention: last 5 digits before decimal = driver version * 100
        $driverStr = $nvidiaInfo.DriverVersion -replace "\.", ""
        $driverVer = [int]($driverStr[-5..-1] -join "")
        if ($driverVer -ge 52000) {
            Pass "NVIDIA driver: $($nvidiaInfo.DriverVersion) (≥ 520 for CUDA 12)"
        } else {
            Warn "NVIDIA driver: $($nvidiaInfo.DriverVersion) — driver ≥ 520 recommended for CUDA 12 / llama.cpp"
        }
    } else {
        Warn "No NVIDIA GPU found — Node B works best with NVIDIA GPU for CUDA acceleration"
    }
}

# ── Windows version ───────────────────────────────────────────────────────────
$osVer = [System.Environment]::OSVersion.Version
if ($osVer.Major -ge 10) {
    Pass "Windows version: $($osVer) (Windows 10/11)"
} else {
    Fail "Windows 10 or later required (found: $($osVer))"
}

# ── Port availability ─────────────────────────────────────────────────────────
$portsToCheck = @()
if ($Role -in @("Orchestrator","Full")) { $portsToCheck += @(5100, 5000) }
if ($Role -in @("Worker","Full"))       { $portsToCheck += @(5050) }

foreach ($port in $portsToCheck) {
    $inUse = (Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0
    if ($inUse) {
        Warn "Port $port is already in use — configure a different port during installation"
    } else {
        Pass "Port $port is available"
    }
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Prerequisites check for $Role:"
Write-Host "  Passed:   $($results.Passed.Count)"
Write-Host "  Warnings: $($results.Warnings.Count)"
Write-Host "  Failed:   $($results.Failed.Count)"

if ($OutputFile) {
    $results | ConvertTo-Json -Depth 5 | Set-Content $OutputFile -Encoding UTF8
}

if ($results.Failed.Count -gt 0) { exit 1 }
if ($results.Warnings.Count -gt 0) { exit 2 }
exit 0
