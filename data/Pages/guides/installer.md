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
| **Role** | Choose Orchestrator / Worker / Orchestrator+Worker |
| Destination | Install path (default: `C:\Program Files\SplitBrain.AI`) |
| **Network Config** | Peer machine IP (optional for Full), ports, Ollama URL, Copilot token |
| **Backend** (Worker/Full) | Choose Ollama or llama.cpp (MoE models for GTX 1080) |
| Optional Tasks | Windows service per component, firewall rules, model download |
| Ready | Summary of what will be installed |
| Installing | Writes config, installs service(s), firewall, pulls models |
| Finish | Launch MCP Server and/or Dashboard |

## Wizard Screenshots Walk-Through

### Role Page

Choose based on what this machine should do:

| Role | Installs | Best for |
|------|---------|---------|
| **Orchestrator** | MCP Server, Dashboard | Primary laptop/workstation that routes tasks to workers |
| **Worker** | NodeWorker | Dedicated inference tower that provides GPU compute |
| **Orchestrator + Worker** | Everything | Single-machine setup, or when this machine has both a good GPU and you want a self-contained install |

> **Tip:** If you have two machines, install **Orchestrator** on the laptop and **Worker** on the tower. If you only have one machine (or it's powerful enough to do both), choose **Orchestrator + Worker**.

### Network Configuration Page

| Field | What to Enter |
|-------|--------------|
| Peer Orchestrator IP | For **Worker** installs: IP of the Orchestrator machine. For **Orchestrator** or **Full**: optional (used to reach external workers) |
| MCP Server port | Port for MCP server (default: `5100`) |
| Node Worker port | Port for NodeWorker (default: `5050`) |
| Ollama URL | Ollama on *this* machine (default: `http://localhost:11434`) |
| Copilot token | Optional GitHub token with `copilot` scope for Node C |

> **Orchestrator + Worker** installs automatically point the Orchestrator at `localhost:5050` — the peer IP field is ignored.

### Backend Selection (Worker / Full Only)

| Option | Docker? | Best For |
|--------|---------|---------|
| **Ollama** | No | Simple setup, ~20-45 tok/s for 7B models |
| **llama.cpp Native** | **No** | 30B MoE models on limited VRAM, ~18-22 tok/s, recommended for llama.cpp |
| **llama.cpp Docker** | Yes | Same as Native, but containerized; requires Docker Desktop + NVIDIA Container Toolkit |

> **Key insight:** Docker is completely optional. `llama-server.exe` runs natively on Windows and provides identical performance and flags. All three options expose the same `http://localhost:8080` endpoint — SplitBrain.AI's NodeClient doesn't care which one started the server.

- **Native** path: installer downloads `llama-server.exe` (CUDA 12.4 build) from GitHub Releases and creates `Start-LlamaCpp.ps1` / `start-llamacpp.bat` launch scripts with the pre-tuned flags for your hardware.
- **Docker** path: creates a `Start-LlamaCpp-Docker.ps1` script wrapping the `docker run` command.

Switch at any time: set `NODE_B_BACKEND=llamacpp` (or `ollama`) before starting NodeWorker.

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
