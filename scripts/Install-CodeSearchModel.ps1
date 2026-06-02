<#
.SYNOPSIS
Downloads and exports the ONNX embedding model for SplitBrain.AI wiki code-search.
Copies the exported model to data/Models/.

.EXAMPLE
./Scripts/Install-CodeSearchModel.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ModelId = "nomic-ai/nomic-embed-text-v1.5",
    [string]$MemorySmithRepoPath = "C:\Users\norrt\source\repos\MemorySmith",
    [switch]$ForceRedownload
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$modelsDir = Join-Path $repoRoot "data/Models"

# Use MemorySmith's Install-CodeSearchModel.ps1 if available
$msInstallScript = Join-Path $MemorySmithRepoPath "Scripts/Install-CodeSearchModel.ps1"
if (Test-Path $msInstallScript) {
    Write-Host "Using MemorySmith's model installer at: $msInstallScript"
    & $msInstallScript -ModelId $ModelId -ModelsDir $modelsDir -ForceRedownload:$ForceRedownload
}
else {
    Write-Host "MemorySmith installer not found. Ensure models are in: $modelsDir"
    Write-Host "Expected: e5-base-v2.onnx and vocab.txt"
    New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null
}

Write-Host "Model directory: $modelsDir"
Write-Host "To complete setup, ensure e5-base-v2.onnx and vocab.txt are present in $modelsDir"
