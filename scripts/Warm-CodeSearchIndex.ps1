<#
.SYNOPSIS
Builds or refreshes the SplitBrain.AI code-search index through the local wiki's MCP endpoint.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://127.0.0.1:6769',
    [string]$Query = 'SplitBrain.AI code search warmup',
    [string[]]$Targets = @(
        'src/Orchestrator.Core', 'src/Orchestrator.Infrastructure', 'src/Orchestrator.Agents',
        'src/Orchestrator.Mcp', 'src/Orchestrator.NodeWorker', 'src/SplitBrain.Dashboard',
        'src/NodeClient.Ollama', 'src/NodeClient.LlamaCpp', 'src/NodeClient.Copilot',
        'src/NodeClient.Worker', 'src/Orchestrator.Tests'
    ),
    [int]$Limit = 5,
    [switch]$ForceRebuild,
    [switch]$SkipCertificateCheck,
    [string]$ApiKey,
    [string]$SummaryPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-McpRequest {
    param(
        [Parameter(Mandatory = $true)][string]$Endpoint,
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][hashtable]$Params,
        [Parameter(Mandatory = $true)][bool]$SkipTlsValidation
    )
    $body = @{
        jsonrpc = '2.0'
        id = [Guid]::NewGuid().ToString('N')
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 20

    $arguments = @{
        Uri = $Endpoint
        Method = 'Post'
        ContentType = 'application/json'
        Body = $body
        TimeoutSec = 3600
    }
    if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
        $arguments['Headers'] = @{ 'X-Api-Key' = $ApiKey }
    }
    if ($SkipTlsValidation) {
        $arguments['SkipCertificateCheck'] = $true
    }
    return Invoke-RestMethod @arguments
}

function Invoke-McpTool {
    param(
        [Parameter(Mandatory = $true)][string]$Endpoint,
        [Parameter(Mandatory = $true)][string]$ToolName,
        [Parameter(Mandatory = $true)][hashtable]$Arguments,
        [Parameter(Mandatory = $true)][bool]$SkipTlsValidation
    )
    $response = Invoke-McpRequest -Endpoint $Endpoint -Method 'tools/call' -Params @{ name = $ToolName; arguments = $Arguments } -SkipTlsValidation $SkipTlsValidation
    $errorProperty = $response.PSObject.Properties['error']
    if ($null -ne $errorProperty -and $null -ne $errorProperty.Value) {
        throw "MCP call failed: $($errorProperty.Value.message)"
    }
    $result = $response.PSObject.Properties['result'].Value
    $text = [string]$result.content[0].text
    $isErrorProperty = $result.PSObject.Properties['isError']
    if ($null -ne $isErrorProperty -and [bool]$isErrorProperty.Value) {
        throw "MCP tool '$ToolName' failed: $text"
    }
    return $text
}

$endpoint = ([System.Uri]::new([System.Uri]::new($BaseUrl.TrimEnd('/') + '/'), 'mcp')).AbsoluteUri

Write-Host "==> Warming code-search index via $endpoint"

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$resultJson = Invoke-McpTool -Endpoint $endpoint -ToolName 'memorysmith_code_search' -Arguments @{
    query = $Query
    targets = $Targets
    limit = $Limit
    rebuildIfStale = $true
    forceRebuild = [bool]$ForceRebuild
} -SkipTlsValidation $SkipCertificateCheck
$stopwatch.Stop()

$result = $resultJson | ConvertFrom-Json -ErrorAction Stop

# Parse summary from response — handle both direct object and text-wrapped formats
$indexedFileCount = 0; $indexedChunkCount = 0; $providerMode = "unknown"
try { $indexedFileCount = [int]$result.indexedFileCount } catch { try { $indexedFileCount = [int]$result.result.indexedFileCount } catch {} }
try { $indexedChunkCount = [int]$result.indexedChunkCount } catch { try { $indexedChunkCount = [int]$result.result.indexedChunkCount } catch {} }
try { $providerMode = [string]$result.providerMode } catch { try { $providerMode = [string]$result.result.providerMode } catch {} }

$summary = [ordered]@{
    BaseUrl = $BaseUrl
    McpEndpoint = $endpoint
    Query = $Query
    Targets = $Targets
    ForceRebuild = [bool]$ForceRebuild
    ElapsedMilliseconds = [int][Math]::Round($stopwatch.Elapsed.TotalMilliseconds)
    IndexedFileCount = $indexedFileCount
    IndexedChunkCount = $indexedChunkCount
    ProviderMode = $providerMode
}

$summaryJson = $summary | ConvertTo-Json -Depth 20
if (-not [string]::IsNullOrWhiteSpace($SummaryPath)) {
    $fullSummaryPath = [System.IO.Path]::GetFullPath($SummaryPath)
    Set-Content -Path $fullSummaryPath -Value $summaryJson -Encoding UTF8
    Write-Host "Summary written to: $fullSummaryPath"
}

Write-Host $summaryJson
Write-Host "`nCode-search index warmed in $([int][Math]::Round($stopwatch.Elapsed.TotalSeconds))s"
