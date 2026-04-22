# Configure Node A + Node C for testing
# Usage: .\setup-test-nodeac.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$GitHubToken,

    [Parameter(Mandatory=$false)]
    [switch]$UseKeyVault,

    [Parameter(Mandatory=$false)]
    [string]$KeyVaultUri,

    [switch]$CheckPrereqs
)

$ErrorActionPreference = "Stop"

function Write-Header {
    param([string]$Message)
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Error-Custom {
    param([string]$Message)
    Write-Host "[FAIL] $Message" -ForegroundColor Red
}

# ============================================================================
# Check Prerequisites
# ============================================================================

if ($CheckPrereqs) {
    Write-Header "Checking Prerequisites"

    $failed = @()

    # Check .NET
    Write-Host "Checking .NET SDK..."
    try {
        $dotnet = dotnet --version 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Found .NET $dotnet"
        } else {
            $failed += "dotnet CLI not found. Install .NET 10 SDK from: https://dotnet.microsoft.com/download"
        }
    } catch {
        $failed += "dotnet CLI error: $_"
    }

    # Check Ollama
    Write-Host "Checking Ollama..."
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:11434/api/tags" -UseBasicParsing
        Write-Success "Ollama is running on localhost:11434"
    } catch {
        $failed += "Ollama not responding. Start it with: ollama serve"
    }

    # Check GitHub CLI
    Write-Host "Checking GitHub CLI..."
    $ghPath = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghPath) {
        try {
            $ghVersion = gh --version 2>&1
            Write-Success "Found GitHub CLI: $ghVersion"
        } catch {
            $failed += "GitHub CLI error. Try reinstalling."
        }
    } else {
        $failed += "GitHub CLI (gh) not found. Install from: https://cli.github.com or choco install gh"
    }

    # Check Copilot CLI
    Write-Host "Checking Copilot CLI..."
    if ($ghPath) {
        try {
            $copilot = gh copilot --version 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Found Copilot: $copilot"
            } else {
                # Copilot CLI not installed as extension yet
                Write-Host "[INFO] Copilot CLI extension not yet installed." -ForegroundColor Cyan
                Write-Host "       You can install it later with: gh extension install github/gh-copilot"
                Write-Host "       Or the first time you use: gh copilot auth login"
            }
        } catch {
            # Copilot CLI error or not installed
            Write-Host "[INFO] Copilot CLI not installed yet." -ForegroundColor Cyan
            Write-Host "       Install with: gh extension install github/gh-copilot"
        }
    } else {
        Write-Host "[SKIP] Skipping Copilot check (GitHub CLI not found)"
    }

    # Report results
    Write-Host ""
    if ($failed.Count -eq 0) {
        Write-Success "All prerequisites met!"
        exit 0
    } else {
        Write-Host "[WARNING] Missing prerequisites:" -ForegroundColor Yellow
        foreach ($issue in $failed) {
            Write-Host "  - $issue"
        }
        Write-Host ""
        Write-Host "Please install missing prerequisites and try again." -ForegroundColor Yellow
        exit 1
    }
}

# ============================================================================
# Setup GitHub Token
# ============================================================================

Write-Header "Setting up GitHub Copilot Authentication"

if ($UseKeyVault -and $KeyVaultUri) {
    Write-Host "Using Azure Key Vault: $KeyVaultUri"

    # Update appsettings.json
    $appsettingsPath = "src\Orchestrator.Mcp\appsettings.json"
    Write-Host "Updating $appsettingsPath..."

    $json = Get-Content $appsettingsPath | ConvertFrom-Json
    $json.CopilotNode.KeyVaultUri = $KeyVaultUri
    $json.CopilotNode.KeyVaultSecretName = "CopilotApiKey"

    $json | ConvertTo-Json | Set-Content $appsettingsPath
    Write-Success "Key Vault configured: $KeyVaultUri"

} elseif ($GitHubToken) {
    Write-Host "Setting GitHub token via environment variable..."
    [System.Environment]::SetEnvironmentVariable("COPILOT_API_KEY", $GitHubToken, "User")
    Write-Success "COPILOT_API_KEY set (User scope)"

} else {
    Write-Host "No token provided. Attempting GitHub CLI authentication..."

    # Check if gh CLI is available
    $ghPath = Get-Command gh -ErrorAction SilentlyContinue
    if ($ghPath) {
        Write-Host "Checking GitHub CLI login status..."
        try {
            $auth = gh auth token 2>&1
            if ($auth -and $auth -notmatch "error|not authenticated") {
                [System.Environment]::SetEnvironmentVariable("COPILOT_API_KEY", $auth, "User")
                Write-Success "Using GitHub CLI token"
            } else {
                Write-Host "[INFO] GitHub CLI not authenticated." -ForegroundColor Cyan
                Write-Host ""
                Write-Host "To authenticate, run:" -ForegroundColor Yellow
                Write-Host "  gh auth login"
                Write-Host "  gh copilot auth login"
                Write-Host ""
                Write-Host "Then run this script again." -ForegroundColor Yellow
                exit 1
            }
        } catch {
            Write-Host "[ERROR] Failed to get token from GitHub CLI: $_" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "[ERROR] GitHub CLI (gh) not found." -ForegroundColor Red
        Write-Host ""
        Write-Host "Install GitHub CLI from: https://cli.github.com" -ForegroundColor Yellow
        Write-Host "  Windows: winget install GitHub.cli"
        Write-Host "  Or: choco install gh"
        Write-Host ""
        Write-Host "Then install Copilot extension: gh extension install github/gh-copilot" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Or pass token directly: .\setup-test-nodeac.ps1 -GitHubToken 'ghp_...'" -ForegroundColor Yellow
        exit 1
    }
}

# ============================================================================
# Configure appsettings.json
# ============================================================================

Write-Header "Configuring appsettings.json"

$appsettingsPath = "src\Orchestrator.Mcp\appsettings.json"
$json = Get-Content $appsettingsPath | ConvertFrom-Json

# Ensure Node B is unreachable (routes to Node C as fallback)
$json.OllamaNodeB.BaseUrl = "http://unreachable.local:11434"

# Ensure Node C is configured
if (-not $json.CopilotNode) {
    $json | Add-Member -NotePropertyName "CopilotNode" -NotePropertyValue @{}
}

$json.CopilotNode.Model = "gpt-4o"
$json.CopilotNode.TimeoutSeconds = 60

$json | ConvertTo-Json -Depth 4 | Set-Content $appsettingsPath
Write-Success "appsettings.json updated"

Write-Host "Configuration:"
Write-Host "  Node A (Ollama):     $($json.OllamaNode.BaseUrl)"
Write-Host "  Node B (Disabled):   $($json.OllamaNodeB.BaseUrl)"
Write-Host "  Node C (Copilot):    Model=$($json.CopilotNode.Model), Timeout=$($json.CopilotNode.TimeoutSeconds)s"

# ============================================================================
# Summary
# ============================================================================

Write-Header "Setup Complete!"

Write-Host ""
Write-Host "Next steps:"
Write-Host ""
Write-Host "1. Start Ollama (Terminal 1):"
Write-Host "   ollama serve"
Write-Host ""
Write-Host "2. Start MCP Server (Terminal 2):"
Write-Host "   dotnet run --project src/Orchestrator.Mcp"
Write-Host ""
Write-Host "3. Test with Claude Desktop or custom MCP client"
Write-Host ""
Write-Host "To verify prerequisites are installed, run:"
Write-Host "   .\setup-test-nodeac.ps1 -CheckPrereqs"
Write-Host ""

Write-Success "Ready to test Node A and Node C!"
