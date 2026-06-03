<#
.SYNOPSIS
    Builds and packages the SplitBrain.AI Inno Setup installer.

.DESCRIPTION
    1. Publishes Orchestrator.Mcp and SplitBrain.Dashboard (Node A components)
    2. Publishes Orchestrator.NodeWorker (Node B component)
    3. Copies deploy/ and data/ into the installer source tree
    4. Runs Inno Setup Compiler (iscc.exe) to produce the .exe installer
    5. Places the output in installer/dist/

.PARAMETER Version
    Version string embedded in the installer name. Defaults to current date.

.PARAMETER SkipPublish
    Skip dotnet publish (use existing output\ directory contents).

.PARAMETER IsccPath
    Path to Inno Setup Compiler executable. Auto-detected from standard locations.

.EXAMPLE
    # Build everything from scratch
    .\installer\build.ps1

    # Rebuild installer without re-publishing
    .\installer\build.ps1 -SkipPublish

.NOTES
    Requires: .NET 10 SDK, Inno Setup 6+ (https://jrsoftware.org/isinfo.php)
    Run from the repository root.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Version    = (Get-Date -Format "yyyy.MM.dd"),
    [switch] $SkipPublish,
    [string] $IsccPath   = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot    = Split-Path $PSScriptRoot -Parent
$InstallerDir = $PSScriptRoot
$OutputDir   = Join-Path $InstallerDir "output"
$DistDir     = Join-Path $InstallerDir "dist"
$IssFile     = Join-Path $InstallerDir "SplitBrainAI.iss"

function Write-Step { param([string]$Msg) Write-Host "`n==> $Msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Msg) Write-Host "    [OK] $Msg" -ForegroundColor Green }
function Write-Skip { param([string]$Msg) Write-Host "    [--] $Msg" -ForegroundColor DarkGray }

# ── Locate Inno Setup compiler ────────────────────────────────────────────────
if (-not $IsccPath) {
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
        "C:\Program Files\Inno Setup 7\ISCC.exe",
        "D:\Program Files\Inno Setup 7\ISCC.exe",
        (Get-Command iscc -ErrorAction SilentlyContinue)?.Source
    ) | Where-Object { $_ -and (Test-Path $_) }
    $IsccPath = $candidates | Select-Object -First 1
}

if (-not $IsccPath) {
    Write-Warning @"
Inno Setup compiler (ISCC.exe) not found.

Install Inno Setup 6 from: https://jrsoftware.org/isdl.php
Or pass -IsccPath 'C:\path\to\ISCC.exe'

Tip (winget):  winget install JRSoftware.InnoSetup
Tip (choco):   choco install innosetup
"@
    exit 1
}
Write-Ok "Inno Setup: $IsccPath"

# ── Publish .NET projects ─────────────────────────────────────────────────────
if (-not $SkipPublish) {
    Write-Step "Publishing .NET projects"

    $publishProfiles = @(
        @{
            Project  = "src\Orchestrator.Mcp\Orchestrator.Mcp.csproj"
            Output   = Join-Path $OutputDir "node-a\mcp"
            Label    = "Orchestrator.Mcp (Node A)"
        },
        @{
            Project  = "src\SplitBrain.Dashboard\SplitBrain.Dashboard.csproj"
            Output   = Join-Path $OutputDir "node-a\dashboard"
            Label    = "SplitBrain.Dashboard (Node A)"
        },
        @{
            Project  = "src\Orchestrator.NodeWorker\Orchestrator.NodeWorker.csproj"
            Output   = Join-Path $OutputDir "node-b\worker"
            Label    = "Orchestrator.NodeWorker (Node B)"
        }
    )

    foreach ($p in $publishProfiles) {
        Write-Host "  Publishing $($p.Label) ..."
        $projPath = Join-Path $RepoRoot $p.Project
        dotnet publish $projPath `
            --configuration Release `
            --runtime win-x64 `
            --self-contained false `
            --output $p.Output `
            /p:PublishSingleFile=false `
            /p:GenerateDocumentationFile=false
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($p.Label)" }
        Write-Ok $p.Label
    }
} else {
    Write-Skip "Skipping dotnet publish (-SkipPublish)"
}

# ── Verify output exists ──────────────────────────────────────────────────────
Write-Step "Verifying output"
@(
    (Join-Path $OutputDir "node-a\mcp"),
    (Join-Path $OutputDir "node-a\dashboard"),
    (Join-Path $OutputDir "node-b\worker")
) | ForEach-Object {
    if (-not (Test-Path $_)) {
        throw "Expected output directory missing: $_. Run without -SkipPublish."
    }
    Write-Ok $_
}

# ── Create dist directory ─────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

# ── Run Inno Setup ────────────────────────────────────────────────────────────
Write-Step "Running Inno Setup Compiler"
$isccArgs = @(
    "/DMyAppVersion=$Version",
    "/DRepoRoot=$RepoRoot",
    "/DOutputBase=$DistDir",
    $IssFile
)

Write-Host "  iscc $($isccArgs -join ' ')"
& $IsccPath @isccArgs
if ($LASTEXITCODE -ne 0) { throw "ISCC.exe failed (exit $LASTEXITCODE)" }

# ── Done ──────────────────────────────────────────────────────────────────────
$installerExe = Get-ChildItem $DistDir -Filter "SplitBrainAI-Setup-*.exe" |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1

Write-Host ""
Write-Host "[Build complete]" -ForegroundColor Green
Write-Host "  Installer: $($installerExe.FullName)"
Write-Host "  Version:   $Version"
Write-Host ""
Write-Host "Distribute: $($installerExe.Name)"
