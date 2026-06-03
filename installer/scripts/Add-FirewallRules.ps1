<#
.SYNOPSIS
    Adds Windows Firewall rules for SplitBrain.AI.

.DESCRIPTION
    Creates inbound allow rules for the MCP server, Dashboard, and NodeWorker ports.
    Idempotent — removes existing rules with the same name before adding.

.PARAMETER Role
    "NodeA" or "NodeB" (or "Both").

.PARAMETER McpPort
    MCP server port (Node A, default 5100).

.PARAMETER DashboardPort
    Dashboard port (Node A, default 5000).

.PARAMETER WorkerPort
    NodeWorker port (Node B, default 5050).
#>
#Requires -RunAsAdministrator
param(
    [ValidateSet("NodeA","NodeB","Both","Orchestrator","Worker","Full")] [string] $Role = "Orchestrator",
    [int] $McpPort       = 5100,
    [int] $DashboardPort = 5000,
    [int] $WorkerPort    = 5050
)

$ErrorActionPreference = "Stop"

function Add-SBFirewallRule {
    param([string]$Name, [int]$Port, [string]$Protocol = "TCP")
    # Remove existing rule with same name (idempotent)
    Remove-NetFirewallRule -DisplayName $Name -ErrorAction SilentlyContinue

    New-NetFirewallRule `
        -DisplayName    $Name `
        -Direction      Inbound `
        -Protocol       $Protocol `
        -LocalPort      $Port `
        -Action         Allow `
        -Profile        @("Domain","Private") `
        -Description    "SplitBrain.AI — auto-created by installer" | Out-Null

    Write-Host "[OK] Firewall: $Name (TCP $Port)"
}

if ($Role -in @("NodeA","Both","Orchestrator","Full")) {
    Add-SBFirewallRule -Name "SplitBrain.AI — MCP Server"  -Port $McpPort
    Add-SBFirewallRule -Name "SplitBrain.AI — Dashboard"   -Port $DashboardPort
}

if ($Role -in @("NodeB","Both","Worker","Full")) {
    Add-SBFirewallRule -Name "SplitBrain.AI — NodeWorker"  -Port $WorkerPort
}

Write-Host "[Firewall rules configured]"
