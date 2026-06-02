# Installing SplitBrain.AI with the Wizard Installer

The recommended way to set up SplitBrain.AI is with the Inno Setup wizard installer. It automates prerequisite checking, configuration, model downloading, and optional Windows service registration.

## Download

Get the latest installer from the [Releases page](https://github.com/TheMasonX/SplitBrain.AI/releases) or build it yourself:

```powershell
# Clone the repo and build the installer from source
git clone https://github.com/TheMasonX/SplitBrain.AI.git
cd SplitBrain.AI
.\installer\build.ps1
# → installer\dist\SplitBrainAI-Setup-{version}.exe
```

## What the Wizard Does

The wizard walks you through 10 steps:

| Step | What Happens |
|------|-------------|
| Welcome | Overview of SplitBrain.AI |
| License | MIT license agreement |
| **Node Role** | You select Node A (MCP + Dashboard) or Node B (worker) |
| Destination | Install path (default: `C:\Program Files\SplitBrain.AI`) |
| **Network Config** | Peer machine IP, ports, Ollama URL, optional Copilot token |
| **Backend (Node B)** | Choose Ollama or llama.cpp (MoE models for GTX 1080) |
| Optional Tasks | Windows service, firewall rules, Ollama model download |
| Ready | Summary of what will be installed |
| Installing | Runs dotnet publish, writes config, installs service, pulls models |
| Finish | Launch MCP Server and/or Dashboard |

## Wizard Screenshots Walk-Through

### Node Role Page

Choose based on which machine you're running the installer on:

- **Node A** — Your laptop or primary workstation. Installs: MCP Server, Dashboard, NodeA Ollama env vars.
- **Node B** — Your inference tower. Installs: NodeWorker, NodeB Ollama env vars (flash attention disabled for Pascal/GTX 1080).

### Network Configuration Page

| Field | What to Enter |
|-------|--------------|
| Peer machine IP | LAN IP of the *other* machine (e.g. `192.168.1.50`) |
| MCP Server port | Port for MCP server on Node A (default: `5100`) |
| Node Worker port | Port for NodeWorker on Node B (default: `5050`) |
| Ollama URL | Ollama on *this* machine (default: `http://localhost:11434`) |
| Copilot token | Optional GitHub token with `copilot` scope for Node C |

### Backend Selection (Node B Only)

| Option | Best For |
|--------|---------|
| **Ollama** (recommended) | Simpler setup, ~20-45 tok/s for 7B model |
| **llama.cpp** (advanced) | 30B MoE models, ~18-22 tok/s, requires Docker + NVIDIA Container Toolkit |

You can switch backends later with `NODE_B_BACKEND=llamacpp` (or `ollama`).

### Optional Tasks

- ✅ **Install as Windows Service** — both MCP Server and NodeWorker start automatically at boot
- ✅ **Add Firewall rules** — creates inbound allow rules on Domain and Private profiles
- ✅ **Download Ollama models** — pulls all required models during installation (requires internet, ~4-8 GB per model)
- ☐ **Desktop shortcut** — adds a shortcut to the MCP Server executable

## Uninstalling

Use **Add or Remove Programs** → SplitBrain.AI → Uninstall.

The uninstaller:
- Stops and removes the Windows services
- Removes firewall rules created by the installer
- Removes installed files (preserves `data/` wiki content)

## Building the Installer From Source

Requirements:
- .NET 10 SDK
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (`winget install JRSoftware.InnoSetup`)

```powershell
# From the repo root:
.\installer\build.ps1                          # full build
.\installer\build.ps1 -SkipPublish            # re-package without re-publishing
.\installer\build.ps1 -Version "1.2.0"        # custom version string
```

Output: `installer/dist/SplitBrainAI-Setup-{version}.exe`

## Manual Setup (Without Installer)

If you prefer to run the setup scripts directly:

```powershell
# On Machine A (as Administrator)
.\deploy\setup-node-a.ps1

# On Machine B (as Administrator)  
.\deploy\setup-node-b.ps1

# Reconfigure after install
.\installer-scripts\Write-Config.ps1 -InstallDir "." -Role NodeA -PeerIp 192.168.1.50
```

## Related Pages

- [Getting Started](getting-started.md)
- [Backend Switching](backend-switching.md)
- [llama.cpp GTX 1080 Setup](llamacpp-gtx1080-setup.md)
- [Configuration Reference](../ops/configuration-reference.md)
- [Installer Developer Guide](../ops/installer-dev-guide.md)
