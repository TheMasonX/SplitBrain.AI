[CmdletBinding()]
param(
  [string]$MemorySmithRepoPath = "C:\Users\norrt\source\repos\MemorySmith",
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts/service"
$appProject = Join-Path $MemorySmithRepoPath "MemorySmith.App/MemorySmith.App.csproj"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet SDK is required but was not found on PATH."
}

if (-not (Test-Path $MemorySmithRepoPath)) {
  throw "MemorySmith repo path not found: $MemorySmithRepoPath"
}

if (-not (Test-Path $appProject)) {
  throw "MemorySmith.App project not found at: $appProject"
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $appProject -c $Configuration -o $publishDir -nologo | Out-Host

Write-Host "Publish complete. Output: $publishDir"
