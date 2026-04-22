# ---------------------------------------------------------------------------
# Module State
# ---------------------------------------------------------------------------
$script:McpUrl = "http://localhost:5000/mcp"
$script:SessionId = $null
$script:ShowRaw = $true
$script:LastRequest = $null

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
function Set-McpEndpoint {
    param(
        [Parameter(Mandatory)]
        [string]$Url
    )

    $script:McpUrl = $Url
    Write-Host "[OK] MCP endpoint set to $Url" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Initialize Session
# ---------------------------------------------------------------------------
function Connect-Mcp {
    Write-Host "`n==> Initializing MCP session..." -ForegroundColor Cyan

    $initBody = @{
        jsonrpc = "2.0"
        id      = [guid]::NewGuid().ToString()
        method  = "initialize"
        params  = @{
            protocolVersion = "2024-11-05"
            clientInfo = @{
                name    = "powershell-client"
                version = "1.0"
            }
            capabilities = @{}
        }
    }

    try {
        $response = Invoke-WebRequest -Uri $script:McpUrl `
            -Method Post `
            -UseBasicParsing `
            -ContentType "application/json" `
            -Headers @{ "Accept" = "application/json, text/event-stream" } `
            -Body ($initBody | ConvertTo-Json -Depth 10)

        # Parse JSON body from SSE data line
        $dataLine = $response.Content -split "`n" |
            Where-Object { $_ -match '^data:\s*' } |
            Select-Object -Last 1

        if ($dataLine) {
            $json = ($dataLine -replace '^data:\s*', '').Trim() | ConvertFrom-Json
        } else {
            $json = $response.Content | ConvertFrom-Json
        }

        # Get session ID from headers (case-insensitive)
        $sessionId = $response.Headers["Mcp-Session-Id"]

        # Fallback to body if needed
        if (-not $sessionId) {
            $sessionId = $json.result.sessionId
        }

        if (-not $sessionId) {
            throw "No session ID returned"
        }

        $script:SessionId = $sessionId

        Write-Host "    [OK] Session established: $sessionId" -ForegroundColor Green
    }
    catch {
        Write-Host "[ERROR] Failed to initialize session" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor DarkRed
    }
}

# ---------------------------------------------------------------------------
# Core Request
# ---------------------------------------------------------------------------
function Invoke-McpRequest {
    param(
        [Parameter(Mandatory)]
        [string]$Method,

        [hashtable]$Params = @{},
        [string]$Id = ([guid]::NewGuid().ToString())
    )

    if (-not $script:SessionId) {
        throw "No MCP session. Run Connect-Mcp first."
    }

    $body = @{
        jsonrpc = "2.0"
        id      = $Id
        method  = $Method
        params  = $Params
    }

    $script:LastRequest = $body

    if ($script:ShowRaw) {
        Write-Host "`n--- REQUEST ---" -ForegroundColor DarkGray
        $body | ConvertTo-Json -Depth 10
    }

    try {
        $raw = Invoke-WebRequest -Uri $script:McpUrl `
            -Method Post `
            -UseBasicParsing `
            -TimeoutSec 30 `
            -ContentType "application/json" `
            -Headers @{ "Mcp-Session-Id" = $script:SessionId; "Accept" = "application/json, text/event-stream" } `
            -Body ($body | ConvertTo-Json -Depth 10)

        # Extract session ID from response headers if present (e.g. after initialize)
        $newSession = $raw.Headers["Mcp-Session-Id"]
        if ($newSession) { $script:SessionId = $newSession }

        # Parse SSE stream: find lines starting with "data:" and take the last one
        $dataLine = $raw.Content -split "`n" |
            Where-Object { $_ -match '^data:\s*' } |
            Select-Object -Last 1

        if (-not $dataLine) {
            Write-Host "[WARN] No data line found in SSE response" -ForegroundColor Yellow
            return $null
        }

        $response = ($dataLine -replace '^data:\s*', '').Trim() | ConvertFrom-Json

        if ($script:ShowRaw) {
            Write-Host "`n--- RESPONSE ---" -ForegroundColor DarkGray
            $response | ConvertTo-Json -Depth 10
        }

        if ($response.error) {
            Write-Host "[MCP ERROR] $($response.error.message)" -ForegroundColor Red
        }

        return $response
    }
    catch {
        Write-Host "[HTTP ERROR] $($_.Exception.Message)" -ForegroundColor Red

        if ($_.Exception.Response) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $bodyText = $reader.ReadToEnd()
            Write-Host $bodyText -ForegroundColor DarkRed
        }

        return $null
    }
}

# ---------------------------------------------------------------------------
# Tooling Commands
# ---------------------------------------------------------------------------
function Get-McpTools {
    $response = Invoke-McpRequest -Method "tools/list"

    if (-not $response) { return }

    $response.result.tools | ForEach-Object {
        Write-Host "`n[$($_.name)]" -ForegroundColor Cyan
        Write-Host $_.description

        if ($_.inputSchema) {
            Write-Host "Schema:"
            $_.inputSchema | ConvertTo-Json -Depth 10
        }
    }

    return $response.result.tools
}

function Invoke-McpTool {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [hashtable]$Arguments = @{}
    )

    $response = Invoke-McpRequest -Method "tools/call" -Params @{
        name      = $Name
        arguments = $Arguments
    }

    if ($response) {
        Write-Host "`n--- RESULT ---" -ForegroundColor Green
        $response.result | ConvertTo-Json -Depth 10
    }

    return $response
}

# ---------------------------------------------------------------------------
# Interactive Tool Caller
# ---------------------------------------------------------------------------
function Invoke-McpToolInteractive {
    $toolName = Read-Host "Tool name"

    Write-Host "Enter JSON arguments (paste, then press ENTER twice for empty line to submit):"
    $lines = @()
    while ($true) {
        $line = Read-Host
        if ([string]::IsNullOrWhiteSpace($line)) { break }
        $lines += $line
    }
    $jsonInput = ($lines -join " ").Trim()

    if ([string]::IsNullOrWhiteSpace($jsonInput)) {
        $toolArgs = @{}
    } else {
        try {
            $obj = $jsonInput | ConvertFrom-Json
            $toolArgs = @{}
            $obj.PSObject.Properties | ForEach-Object { $toolArgs[$_.Name] = $_.Value }
        } catch {
            Write-Host "Invalid JSON" -ForegroundColor Red
            Write-Host $_.Exception.Message -ForegroundColor DarkRed
            return
        }
    }

    Invoke-McpTool -Name $toolName -Arguments $toolArgs
}

# ---------------------------------------------------------------------------
# Raw Mode
# ---------------------------------------------------------------------------
function Invoke-McpRaw {
    Write-Host "Paste full JSON-RPC body:"
    $raw = Read-Host "json"

    try {
        $null = $raw | ConvertFrom-Json
    } catch {
        Write-Host "Invalid JSON" -ForegroundColor Red
        return
    }

    try {
        $response = Invoke-RestMethod -Uri $script:McpUrl `
            -Method Post `
            -ContentType "application/json" `
            -Headers @{ "Mcp-Session-Id" = $script:SessionId } `
            -Body $raw

        Write-Host "`n--- RESPONSE ---" -ForegroundColor Green
        $response | ConvertTo-Json -Depth 10
    }
    catch {
        Write-Host "[HTTP ERROR] $($_.Exception.Message)" -ForegroundColor Red
    }
}

# ---------------------------------------------------------------------------
# Repeat Last Request
# ---------------------------------------------------------------------------
function Repeat-McpLast {
    if (-not $script:LastRequest) {
        Write-Host "No previous request" -ForegroundColor Yellow
        return
    }

    Invoke-McpRequest `
        -Method $script:LastRequest.method `
        -Params $script:LastRequest.params `
        -Id $script:LastRequest.id
}

# ---------------------------------------------------------------------------
# REPL
# ---------------------------------------------------------------------------
function Start-McpRepl {
    Write-Host "`nMCP Interactive Client" -ForegroundColor Green

    while ($true) {
        Write-Host "`nCommands: connect | list | call | raw | repeat | exit" -ForegroundColor Yellow
        $cmd = Read-Host "mcp>"

        switch ($cmd) {
            "connect" { Connect-Mcp }
            "list"    { Get-McpTools }
            "call"    { Invoke-McpToolInteractive }
            "raw"     { Invoke-McpRaw }
            "repeat"  { Repeat-McpLast }
            "exit"    { break }
            default   { Write-Host "Unknown command" -ForegroundColor Red }
        }
    }
}

Export-ModuleMember -Function *