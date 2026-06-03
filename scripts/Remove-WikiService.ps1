[CmdletBinding()]
param(
  [string]$ServiceName = "SplitBrain.AI Wiki",
  [switch]$RemoveArtifacts
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot "Stop-WikiService.ps1") -ServiceName $ServiceName

if ($RemoveArtifacts) {
  $publishDir = Join-Path $repoRoot "artifacts"
  if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
    Write-Host "Removed publish artifacts: $publishDir"
  }

  $serviceDir = Join-Path $repoRoot ".service"
  Get-ChildItem -Path $serviceDir -Filter "*.log" | Remove-Item -Force
  Write-Host "Cleaned service logs in: $serviceDir"
}

Write-Host "Remove complete."
