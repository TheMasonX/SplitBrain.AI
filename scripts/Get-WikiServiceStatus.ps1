[CmdletBinding()]
param(
  [string]$ServiceName = "SplitBrain.AI Wiki"
)

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
  Write-Host "Service: $ServiceName"
  Write-Host "Status: $($service.Status)"
  Write-Host "StartType: $($service.StartType)"

  $repoRoot = Split-Path -Parent $PSScriptRoot
  $portFile = Join-Path $repoRoot ".service/wiki.port"
  if (Test-Path $portFile) {
    $port = Get-Content $portFile -Raw
    Write-Host "Port: $port"
  }
}
else {
  Write-Host "Service '$ServiceName' not found."
}
