[CmdletBinding()]
param(
  [string]$ServiceName = "SplitBrain.AI Wiki",
  [switch]$Quiet
)

$ErrorActionPreference = "Stop"
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($service) {
  if ($service.Status -eq "Running") {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    if (-not $Quiet) { Write-Host "Service '$ServiceName' stopped." }
  }
  # Remove the service so re-install is clean
  & sc.exe delete $ServiceName *> $null
  if (-not $Quiet) { Write-Host "Service '$ServiceName' deleted." }
}
else {
  if (-not $Quiet) { Write-Host "Service '$ServiceName' not found." }
}
