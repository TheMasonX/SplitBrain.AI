# SplitBrain.AI Installer

This directory contains the Inno Setup installer wizard for SplitBrain.AI.

## Prerequisites

| Tool | Version | Download |
|------|---------|---------|
| .NET 10 SDK | 10.x | https://dotnet.microsoft.com/download/dotnet/10.0 |
| Inno Setup | 6.3+ | https://jrsoftware.org/isdl.php |

Quick install (winget):
```powershell
winget install JRSoftware.InnoSetup
winget install Microsoft.DotNet.SDK.10
```

## Building the Installer

Run from the **repository root**:

```powershell
# Full build (publish + package)
.\installer\build.ps1

# Rebuild installer without re-publishing (fast iteration on .iss changes)
.\installer\build.ps1 -SkipPublish

# Specify a version string
.\installer\build.ps1 -Version "1.2.0"
```

The installer `.exe` is written to `installer/dist/SplitBrainAI-Setup-{version}.exe`.

## Wizard Flow

The installer guides users through:

```
1. Welcome           — project overview
2. License           — MIT license agreement
3. Node Role         — Node A (MCP + Dashboard) or Node B (deep inference worker)
4. Destination       — install path (default: C:\Program Files\SplitBrain.AI)
5. Network Config    — peer machine IP, ports, Ollama URL, optional Copilot token
6. Backend (Node B)  — Ollama vs. llama.cpp (MoE offloading for GTX 1080)
7. Tasks             — optional: Windows service, firewall rules, model download
8. Ready to Install  — summary
9. Installing        — progress (publish, configure, service, firewall, models)
10. Finish           — launch checkboxes (MCP Server, Dashboard)
```

## What Gets Installed

```
{app}/
  node-a/
    mcp/        ← Orchestrator.Mcp binaries (Node A)
    dashboard/  ← SplitBrain.Dashboard binaries (Node A)
  node-b/
    worker/     ← Orchestrator.NodeWorker binaries (Node B)
  deploy/       ← Setup PowerShell scripts (idempotent, re-runnable)
  installer-scripts/ ← Config writer, firewall, model puller
  data/         ← MemorySmith wiki (if selected)
  README.md
  LICENSE
```

## Post-Install Scripts

The installer runs these scripts in order:

| Script | Purpose |
|--------|---------|
| `installer-scripts\Write-Config.ps1` | Writes `appsettings.json` for the selected role and configuration |
| `installer-scripts\Add-FirewallRules.ps1` | Adds Windows Firewall inbound rules (if task selected) |
| `installer-scripts\Pull-Models.ps1` | Downloads Ollama models (if task selected) |
| `deploy\setup-node-a.ps1` | Installs Ollama env vars + Windows service (Node A, if task selected) |
| `deploy\setup-node-b.ps1` | Installs Ollama env vars + Windows service (Node B, if task selected) |

## Manual Configuration After Install

Even without the installer, you can run the deploy scripts directly:

```powershell
# Node A — run as Administrator
.\deploy\setup-node-a.ps1

# Node B — run as Administrator on Machine B
.\deploy\setup-node-b.ps1

# Reconfigure appsettings manually
.\installer-scripts\Write-Config.ps1 -InstallDir "C:\Program Files\SplitBrain.AI" `
    -Role NodeA -PeerIp 192.168.1.50 -McpPort 5100

# Add firewall rules
.\installer-scripts\Add-FirewallRules.ps1 -Role NodeA

# Pull models
.\installer-scripts\Pull-Models.ps1 -Role NodeA
```

## Customising the Installer

Key files:

| File | Purpose |
|------|---------|
| `SplitBrainAI.iss` | Main Inno Setup script — wizard pages, files, tasks, [Code] section |
| `build.ps1` | Orchestrates dotnet publish + ISCC.exe |
| `scripts\Write-Config.ps1` | appsettings.json writer |
| `scripts\Test-Prerequisites.ps1` | Pre-install checks (.NET, Ollama, GPU, disk, ports) |
| `scripts\Add-FirewallRules.ps1` | Firewall rule creation |
| `scripts\Pull-Models.ps1` | Ollama model downloader |
| `output\` | Published binaries (created by build.ps1, gitignored) |
| `dist\` | Output installer .exe (created by build.ps1, gitignored) |

## Inno Setup Resources

- [Inno Setup Documentation](https://jrsoftware.org/ishelp/)
- [Pascal Script Reference](https://jrsoftware.org/ishelp/topic_scriptintro.htm)
- [Custom Wizard Pages](https://jrsoftware.org/ishelp/topic_scriptclasses.htm)

## Troubleshooting

| Issue | Solution |
|-------|---------|
| `ISCC.exe not found` | Install Inno Setup 6, or pass `-IsccPath "C:\...\ISCC.exe"` |
| `Publish failed` | Check .NET 10 SDK is installed: `dotnet --version` |
| `Output directory missing` | Run without `-SkipPublish` |
| Installer can't find scripts | Ensure `output\` dirs exist (run `build.ps1`) |
| Service won't start | Run `deploy\setup-node-a.ps1` manually as Administrator |
