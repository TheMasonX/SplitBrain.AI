[CmdletBinding()]
param(
  [int]$PreferredPort = 6769,
  [int]$FallbackPort = 6969,
  [string]$MemorySmithRepoPath = "C:\Users\norrt\source\repos\MemorySmith",
  [string]$Configuration = "Release",
  [string]$ServiceName = "SplitBrain.AI Wiki",
  [string]$BindAddress = "0.0.0.0",
  [bool]$AllowRemoteApi = $true,
  [switch]$EnsurePrivateFirewallRule = $true
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$serviceDir = Join-Path $repoRoot ".service"
$publishDir = Join-Path $repoRoot "artifacts/service"
$appDll = Join-Path $publishDir "MemorySmith.App.dll"
$dataRoot = Join-Path $repoRoot "data"
$dataPath = Join-Path $dataRoot "Memories"
$pagesPath = Join-Path $dataRoot "Pages"
$pagesSourcesPath = Join-Path $pagesPath "Sources"
$varsPath = Join-Path $dataRoot "vars.json"
$eventLogPath = Join-Path $dataRoot "Events/audit.log"
$keysPath = Join-Path $dataRoot "Keys"
$portFile = Join-Path $serviceDir "wiki.port"
$logFile = Join-Path $serviceDir "wiki.log"
$errFile = Join-Path $serviceDir "wiki.err.log"

New-Item -ItemType Directory -Path $serviceDir -Force | Out-Null

& (Join-Path $PSScriptRoot "Stop-WikiService.ps1") -Quiet
& (Join-Path $PSScriptRoot "Publish-WikiEngine.ps1") -MemorySmithRepoPath $MemorySmithRepoPath -Configuration $Configuration

if (-not (Test-Path $appDll)) {
  throw "Published app dll not found: $appDll"
}

# Deploy codesearch configuration override into the publish directory
$codesearchConfigSrc = Join-Path $dataRoot "codesearch-config.json"
$codesearchConfigDst = Join-Path $publishDir "codesearch-config.json"
if (Test-Path $codesearchConfigSrc) {
  Copy-Item -Path $codesearchConfigSrc -Destination $codesearchConfigDst -Force
  Write-Host "Codesearch config deployed: $codesearchConfigDst"
}

New-Item -ItemType Directory -Path $keysPath -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $eventLogPath -Parent) -Force | Out-Null
if (-not (Test-Path $varsPath)) {
  Set-Content -Path $varsPath -Value "{`"%SplitBrainRepo%`": `"$repoRoot`"}" -Encoding utf8
}

function Test-PortAvailable([int]$Port) {
  return -not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

$chosenPort = if (Test-PortAvailable -Port $PreferredPort) { $PreferredPort } elseif (Test-PortAvailable -Port $FallbackPort) { $FallbackPort } else { throw "Neither preferred port $PreferredPort nor fallback port $FallbackPort is available." }
$listenUrl = "http://$BindAddress`:$chosenPort"

function Get-PrimaryLanIp {
  $defaultRoute = Get-NetRoute -DestinationPrefix "0.0.0.0/0" -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.NextHop -and $_.NextHop -ne "0.0.0.0" } |
    Sort-Object RouteMetric, InterfaceMetric |
    Select-Object -First 1

  if ($defaultRoute) {
    $routeIp = Get-NetIPAddress -InterfaceIndex $defaultRoute.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
      Where-Object {
        $_.IPAddress -notlike "127.*" -and
        $_.IPAddress -notlike "169.254.*"
      } |
      Select-Object -ExpandProperty IPAddress -First 1

    if ($routeIp) {
      return $routeIp
    }
  }

  return (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object {
      $_.IPAddress -like "192.168.*" -or
      $_.IPAddress -like "10.*" -or
      $_.IPAddress -match "^172\.(1[6-9]|2[0-9]|3[0-1])\."
    } |
    Select-Object -ExpandProperty IPAddress -First 1)
}

# Ensure stale service definitions do not block re-install.
& dotnet $appDll uninstall --service-name $ServiceName | Out-Host

# Install the Windows service with SplitBrain.AI scoped data paths.
& dotnet $appDll install `
  --service-name $ServiceName `
  --service-display-name $ServiceName `
  --service-description "SplitBrain.AI MCP server project wiki (MemorySmith)" `
  --service-start-type auto `
  --memory-directory $dataPath `
  -- `
  --urls $listenUrl `
  --MemorySmith:AllowRemoteApi $AllowRemoteApi `
  --MemorySmith:SettingsOverridePath $codesearchConfigDst `
  --MemorySmith:DataProtectionKeysPath $keysPath `
  --MemorySmith:AllowedFileRoots:0 $repoRoot `
  --MemorySmith:AllowedFileRoots:1 $dataRoot `
  --MemorySmith:AllowedFileRoots:2 $pagesPath `
  --MemorySmith:SourceLinks:AllowedFileRoots:0 $repoRoot `
  --MemorySmith:SourceLinks:AllowedFileRoots:1 $dataRoot `
  --MemorySmith:SourceLinks:AllowedFileRoots:2 $pagesPath | Out-Host

Start-Service -Name $ServiceName

if ($EnsurePrivateFirewallRule) {
  $ruleName = "SplitBrain.AI Wiki HTTP $chosenPort"
  try {
    $existingRule = Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
    if (-not $existingRule) {
      New-NetFirewallRule `
        -DisplayName $ruleName `
        -Direction Inbound `
        -Action Allow `
        -Profile Private `
        -Protocol TCP `
        -LocalPort $chosenPort | Out-Null
    }
  }
  catch {
    Write-Warning "Unable to create/update firewall rule '$ruleName': $($_.Exception.Message)"
  }
}

$lanIp = Get-PrimaryLanIp

Set-Content -Path $portFile -Value $chosenPort

Write-Host "=== SplitBrain.AI Wiki Service Deployed ==="
Write-Host "ServiceName: $ServiceName"
Write-Host "Local URL: http://127.0.0.1:$chosenPort"
if ($lanIp) {
  Write-Host "LAN URL: http://${lanIp}:$chosenPort"
}
Write-Host "Listen URL: $listenUrl"
Write-Host "PublishDir: $publishDir"
Write-Host "OutLog: $logFile"
Write-Host "ErrLog: $errFile"
