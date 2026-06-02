# Installer Developer Guide

This page documents the internals of the SplitBrain.AI Inno Setup installer for developers who need to modify or extend it.

## File Layout

```
installer/
  SplitBrainAI.iss          ← Main Inno Setup script (wizard + packaging)
  build.ps1                 ← Orchestrates dotnet publish + ISCC.exe
  README.md                 ← Developer quick-reference
  .gitignore                ← Excludes output/ and dist/
  scripts/
    Write-Config.ps1        ← appsettings.json writer
    Test-Prerequisites.ps1  ← Pre-install checks
    Add-FirewallRules.ps1   ← Firewall rule management
    Pull-Models.ps1         ← Ollama model downloader
  output/                   ← Published binaries (gitignored, created by build.ps1)
    node-a/
      mcp/                  ← Orchestrator.Mcp publish output
      dashboard/            ← SplitBrain.Dashboard publish output
    node-b/
      worker/               ← Orchestrator.NodeWorker publish output
  dist/                     ← Installer .exe output (gitignored)
```

## Inno Setup Script Structure

`SplitBrainAI.iss` is organised into standard Inno Setup sections:

| Section | Purpose |
|---------|---------|
| `[Setup]` | App metadata, output filename, compression, UAC requirements |
| `[Languages]` | English only (extend here for localisation) |
| `[Types]` | `nodeA` and `nodeB` installation types |
| `[Components]` | `mcp`, `dashboard`, `worker`, `data` — shown in component selector |
| `[Tasks]` | Optional: Windows service, firewall, model pull, desktop shortcut |
| `[Files]` | Maps `output\*` → `{app}\*` with component guards |
| `[Icons]` | Start Menu and Desktop shortcuts |
| `[Run]` | Post-install: Write-Config, firewall, model pull, service registration |
| `[UninstallRun]` | Cleanup: stop/remove services, remove firewall rules |
| `[Code]` | Pascal scripting: custom wizard pages, validation, argument builders |

## Custom Wizard Pages (Pascal)

Three custom pages are created in `InitializeWizard`:

### RolePage (after wpWelcome)
- `TNewRadioButton` for Node A / Node B
- Sets global `CRole` variable
- Affects component selection and page visibility

### NetworkPage (TInputQueryWizardPage, after RolePage)
- Fields: PeerIp [0], McpPort [1], WorkerPort [2], OllamaUrl [3], CopilotToken [4]
- Validated in `NextButtonClick` (non-empty PeerIp, valid port range)

### BackendPage (after NetworkPage, Node B only)
- `TNewRadioButton` for Ollama / llama.cpp
- Skipped via `ShouldSkipPage` for Node A

## Argument Builders for [Run]

The `[Code]` section provides three functions that build argument strings for the PowerShell scripts:

```pascal
function GetWriteConfigArgs(Param: String): String;
// → -InstallDir "{app}" -Role "NodeA" -PeerIp "192.168.1.50" -McpPort 5100 ...

function GetFirewallArgs(Param: String): String;
// → -Role NodeA -McpPort 5100 -DashboardPort 5000

function GetPullModelsArgs(Param: String): String;
// → -Role NodeA   (or -Role NodeB -Backend llamacpp)
```

These are referenced in `[Run]` as `{code:GetWriteConfigArgs}` etc.

## Build Pipeline

```
build.ps1
  │
  ├─ dotnet publish src\Orchestrator.Mcp          → installer\output\node-a\mcp\
  ├─ dotnet publish src\SplitBrain.Dashboard      → installer\output\node-a\dashboard\
  ├─ dotnet publish src\Orchestrator.NodeWorker   → installer\output\node-b\worker\
  │
  └─ ISCC.exe SplitBrainAI.iss
        /DMyAppVersion={version}
        /DRepoRoot={repoRoot}
        /DOutputBase={distDir}
        → installer\dist\SplitBrainAI-Setup-{version}.exe
```

## Pre-Install Checks (`Test-Prerequisites.ps1`)

Runs in `PrepareToInstall` (before files are copied). Checks:

| Check | Required | Note |
|-------|---------|------|
| .NET 10 runtime | Warning | Installer can download it |
| Ollama | Warning | Installer can download it |
| Disk space ≥ 5 GB free | Hard fail | |
| GPU ≥ 8 GB VRAM | Warning | CPU fallback is possible |
| NVIDIA driver ≥ 520 (Node B) | Warning | Needed for CUDA 12 |
| Windows 10+ | Hard fail | |
| Ports available | Warning | Configure different port |

Exit codes: 0 = all pass, 1 = hard fail, 2 = warnings only.

## Configuration Written (`Write-Config.ps1`)

Writes `appsettings.json` to the installed binary directories:

**Node A** (`node-a\mcp\appsettings.json`):
```json
{
  "Urls": "http://0.0.0.0:5100",
  "OllamaNode": { "BaseUrl": "http://localhost:11434" },
  "NodeWorker": { "BaseUrl": "http://{PeerIp}:{PeerPort}" },
  "CopilotNode": { "Model": "gpt-4o", "TimeoutSeconds": 60 }
}
```

**Node B** (`node-b\worker\appsettings.json` + `appsettings.NodeB.json`):
```json
{
  "Urls": "http://0.0.0.0:5050",
  "OllamaNode": { "BaseUrl": "http://localhost:11434" },
  "LlamaCppNode": { "BaseUrl": "http://localhost:8080", "NodeId": "B", "VramMb": 8192 }
}
```

Also sets `NODE_B_BACKEND` machine-scope environment variable.

## Adding a New Wizard Page

1. Declare the page variable in the `var` block
2. Create it in `InitializeWizard` using `CreateCustomPage` or `TInputQueryWizardPage`
3. Capture its values in `CurPageChanged` or `CurStepChanged`
4. Skip it conditionally via `ShouldSkipPage`
5. Validate in `NextButtonClick`
6. Pass values to scripts via an argument builder function

## Localisation

To add a language:
1. Add to `[Languages]` section: `Name: "french"; MessagesFile: "compiler:Languages\French.isl"`
2. Add `[CustomMessages]` for any custom strings
3. Use `{cm:MyCustomMessage}` in the ISS file

## Signing the Installer

For distribution, sign the `.exe` with a code-signing certificate:

```powershell
# After build.ps1 produces the installer:
signtool sign /tr http://timestamp.digicert.com /td sha256 /fd sha256 `
    /a installer\dist\SplitBrainAI-Setup-*.exe
```

Add to `build.ps1` as an optional post-step.

## CI/CD Integration

To build the installer in a GitHub Actions workflow:

```yaml
- name: Install Inno Setup
  run: choco install innosetup -y

- name: Build Installer
  run: .\installer\build.ps1 -Version ${{ github.ref_name }}

- name: Upload Artifact
  uses: actions/upload-artifact@v4
  with:
    name: installer
    path: installer/dist/*.exe
```
