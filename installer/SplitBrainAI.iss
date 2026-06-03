; SplitBrain.AI — Inno Setup Installer Script
; ============================================================================
; Build with: .\installer\build.ps1
; Requires:   Inno Setup 6+ (https://jrsoftware.org/isdl.php)
; ============================================================================

#define MyAppName      "SplitBrain.AI"
#define MyAppPublisher "TheMasonX"
#define MyAppURL       "https://github.com/TheMasonX/SplitBrain.AI"
#define MyAppExeA      "node-a\mcp\Orchestrator.Mcp.exe"
#define MyAppExeB      "node-b\worker\Orchestrator.NodeWorker.exe"

; Version passed from build.ps1 via /DMyAppVersion=...
#ifndef MyAppVersion
  #define MyAppVersion "0.0.1"
#endif

#ifndef RepoRoot
  #define RepoRoot ".."
#endif

#ifndef OutputBase
  #define OutputBase "dist"
#endif

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile={#RepoRoot}\LICENSE
OutputDir={#OutputBase}
OutputBaseFilename=SplitBrainAI-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardResizable=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\node-a\mcp\Orchestrator.Mcp.exe
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Restart required for env vars to propagate
RestartIfNeededByRun=no
; Wizard appearance
WizardImageFile=compiler:WizModernImage.bmp
WizardSmallImageFile=compiler:WizModernSmallImage.bmp
SetupIconFile=
; Show changelog on finish
; DisableFinishedPage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "orchestrator"; Description: "Orchestrator — MCP Server + Dashboard (routes tasks to workers)"
Name: "worker";       Description: "Worker — Node Worker only (provides local inference)"
Name: "full";         Description: "Orchestrator + Worker — both roles on this machine"

[Components]
Name: "mcp";       Description: "MCP Server (Orchestrator.Mcp)";         Types: orchestrator full
Name: "dashboard"; Description: "SplitBrain Dashboard (Blazor UI)";       Types: orchestrator full
Name: "worker";    Description: "Node Worker (Orchestrator.NodeWorker)";   Types: worker full
Name: "data";      Description: "Wiki / Documentation (data/)";            Types: orchestrator worker full

[Tasks]
Name: "installservice";     Description: "Install as Windows &Service (auto-start on boot)";   GroupDescription: "Service options:"
Name: "installservice\mcp"; Description: "MCP Server service";                                 GroupDescription: "Service options:"; Components: mcp
Name: "installservice\wrk"; Description: "Node Worker service";                                GroupDescription: "Service options:"; Components: worker
Name: "addfirewall";        Description: "Add Windows &Firewall rules for LAN access";         GroupDescription: "Network:"
Name: "pullmodels";         Description: "Download &Ollama models now (auto-skipped if llama.cpp selected)"; GroupDescription: "Ollama models:"
Name: "desktopicon";        Description: "Create a &Desktop shortcut";                         GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; ── Node A binaries ───────────────────────────────────────────────────────────
Source: "output\node-a\mcp\*";       DestDir: "{app}\node-a\mcp";       Components: mcp;       Flags: recursesubdirs ignoreversion
Source: "output\node-a\dashboard\*"; DestDir: "{app}\node-a\dashboard"; Components: dashboard; Flags: recursesubdirs ignoreversion

; ── Node B binaries ───────────────────────────────────────────────────────────
Source: "output\node-b\worker\*";    DestDir: "{app}\node-b\worker";    Components: worker;    Flags: recursesubdirs ignoreversion

; ── Deploy / setup scripts ────────────────────────────────────────────────────
Source: "{#RepoRoot}\deploy\*";      DestDir: "{app}\deploy";           Flags: recursesubdirs ignoreversion
Source: "scripts\*";                 DestDir: "{app}\installer-scripts"; Flags: recursesubdirs ignoreversion

; ── Data / wiki ───────────────────────────────────────────────────────────────
Source: "{#RepoRoot}\data\*";        DestDir: "{app}\data";             Components: data; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist

; ── Docs ─────────────────────────────────────────────────────────────────────
Source: "{#RepoRoot}\README.md";     DestDir: "{app}";                  Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE";       DestDir: "{app}";                  Flags: ignoreversion

[Icons]
; Node A shortcuts
Name: "{group}\SplitBrain.AI MCP Server"; Filename: "{app}\node-a\mcp\Orchestrator.Mcp.exe"; Components: mcp
Name: "{group}\SplitBrain Dashboard";     Filename: "{app}\node-a\dashboard\SplitBrain.Dashboard.exe"; Components: dashboard
; Node B shortcuts
Name: "{group}\SplitBrain.AI Node Worker"; Filename: "{app}\node-b\worker\Orchestrator.NodeWorker.exe"; Components: worker
; Common
Name: "{group}\Setup Guide";          Filename: "{app}\data\Pages\guides\getting-started.md"; Components: data
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
; Desktop
Name: "{autodesktop}\SplitBrain.AI";  Filename: "{app}\node-a\mcp\Orchestrator.Mcp.exe"; Components: mcp; Tasks: desktopicon

[Run]
; ── Write configuration (always) ─────────────────────────────────────────────
Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -File ""{app}\installer-scripts\Write-Config.ps1"" {code:GetWriteConfigArgs}"; \
    StatusMsg: "Configuring SplitBrain.AI..."; \
    Flags: runhidden waituntilterminated

; ── Install Windows service (optional) ───────────────────────────────────────
Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -Command ""& '{{app}}\deploy\setup-node-a.ps1' -PublishPath '{{app}}\node-a\mcp' -SkipDotNet -SkipOllama -SkipModels"""; \
    StatusMsg: "Installing MCP Server service..."; \
    Flags: runhidden waituntilterminated; \
    Tasks: installservice\mcp; Components: mcp

Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -Command ""& '{{app}}\deploy\setup-node-b.ps1' -PublishPath '{{app}}\node-b\worker' -SkipDotNet -SkipOllama -SkipModels"""; \
    StatusMsg: "Installing Node Worker service..."; \
    Flags: runhidden waituntilterminated; \
    Tasks: installservice\wrk; Components: worker

; ── Add firewall rules (optional) ────────────────────────────────────────────
Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -File ""{app}\installer-scripts\Add-FirewallRules.ps1"" {code:GetFirewallArgs}"; \
    StatusMsg: "Configuring firewall..."; \
    Flags: runhidden waituntilterminated; \
    Tasks: addfirewall

; ── Pull Ollama models (optional — skipped automatically if llama.cpp selected) ─
Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -File ""{app}\installer-scripts\Pull-Models.ps1"" {code:GetPullModelsArgs}"; \
    StatusMsg: "Downloading Ollama models (this may take several minutes)..."; \
    Flags: runhidden waituntilterminated; \
    Tasks: pullmodels

; ── Set up llama.cpp backend (Worker + llamacpp only) ────────────────────────
; Works for BOTH native (downloads llama-server.exe) and Docker (creates run script).
; The C# NodeClient.LlamaCpp connects to http://localhost:8080 regardless of mode.
Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -File ""{app}\installer-scripts\Setup-LlamaCpp.ps1"" {code:GetLlamaCppSetupArgs}"; \
    StatusMsg: "Setting up llama.cpp inference server..."; \
    Flags: runhidden waituntilterminated; \
    Check: HasLlamaCppSelected; \
    Components: worker

; ── Launch after install (offer in Finish page) ───────────────────────────────
Filename: "{app}\node-a\mcp\Orchestrator.Mcp.exe"; \
    Description: "Launch MCP Server now"; \
    Flags: nowait postinstall skipifsilent; \
    Components: mcp

Filename: "{app}\node-a\dashboard\SplitBrain.Dashboard.exe"; \
    Description: "Launch Dashboard now"; \
    Flags: nowait postinstall skipifsilent; \
    Components: dashboard

[UninstallRun]
; Stop and remove services on uninstall
Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -Command ""Stop-Service SplitBrainMcpServer -Force -ea 0; sc.exe delete SplitBrainMcpServer"""; \
    Flags: runhidden; Components: mcp
Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -Command ""Stop-Service SplitBrainNodeWorker -Force -ea 0; sc.exe delete SplitBrainNodeWorker"""; \
    Flags: runhidden; Components: worker
; Remove firewall rules
Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -Command ""Remove-NetFirewallRule -DisplayName 'SplitBrain.AI*' -ea 0"""; \
    Flags: runhidden

; ===========================================================================
; Custom wizard pages (Pascal script)
; ===========================================================================
[Code]

// ── Global state ─────────────────────────────────────────────────────────────
var
  // Page references
  RolePage        : TWizardPage;
  NetworkPage     : TInputQueryWizardPage;
  BackendPage     : TWizardPage;

  // Radio buttons on RolePage
  RbOrchestrator  : TNewRadioButton;
  RbWorker        : TNewRadioButton;
  RbFull          : TNewRadioButton;

  // Radio buttons on BackendPage
  RbOllama          : TNewRadioButton;
  RbLlamaCppNative  : TNewRadioButton;   // llama-server.exe — no Docker
  RbLlamaCppDocker  : TNewRadioButton;   // Docker + NVIDIA Container Toolkit

  // Radio buttons for CUDA version (on BackendPage, Native sub-option)
  RbCuda12  : TNewRadioButton;
  RbCuda13  : TNewRadioButton;

  // Cached values from pages
  CRole      : String;   // "Orchestrator" | "Worker" | "Full"
  CBackend   : String;   // "ollama" | "llamacpp-native" | "llamacpp-docker"
  CCudaBuild : String;   // "12.4" | "13.3" (for native mode)

// ── Role helpers ──────────────────────────────────────────────────────────────

// Has MCP Server + Dashboard
function HasOrchestrator: Boolean;
begin
  Result := (CRole = 'Orchestrator') or (CRole = 'Full');
end;

// Has NodeWorker
function HasWorker: Boolean;
begin
  Result := (CRole = 'Worker') or (CRole = 'Full');
end;

function IsFull: Boolean;
begin
  Result := (CRole = 'Full');
end;

// True when any llama.cpp variant is selected (native OR docker)
function HasLlamaCpp: Boolean;
begin
  Result := (CBackend = 'llamacpp-native') or (CBackend = 'llamacpp-docker');
end;

function IsLlamaCppNative: Boolean;
begin
  Result := (CBackend = 'llamacpp-native');
end;

function IsLlamaCppDocker: Boolean;
begin
  Result := (CBackend = 'llamacpp-docker');
end;

// ── RolePage: Orchestrator / Worker / Full ────────────────────────────────────
procedure CreateRolePage;
var
  Lbl, LblO, LblW, LblF: TNewStaticText;
begin
  RolePage := CreateCustomPage(wpWelcome, 'Select Installation Role',
    'What role should this machine play?');

  Lbl := TNewStaticText.Create(RolePage);
  Lbl.Parent   := RolePage.Surface;
  Lbl.Left     := 0;
  Lbl.Top      := 0;
  Lbl.Width    := RolePage.SurfaceWidth;
  Lbl.Caption  := 'SplitBrain.AI can run as an Orchestrator, a Worker, or both on the same machine:';
  Lbl.WordWrap := True;

  // ── Orchestrator ──────────────────────────────────────────────────────────
  RbOrchestrator := TNewRadioButton.Create(RolePage);
  RbOrchestrator.Parent  := RolePage.Surface;
  RbOrchestrator.Left    := 0;
  RbOrchestrator.Top     := Lbl.Top + Lbl.Height + 16;
  RbOrchestrator.Width   := RolePage.SurfaceWidth;
  RbOrchestrator.Caption := 'Orchestrator — MCP Server + Dashboard only';
  RbOrchestrator.Checked := True;
  RbOrchestrator.Font.Style := [fsBold];

  LblO := TNewStaticText.Create(RolePage);
  LblO.Parent   := RolePage.Surface;
  LblO.Left     := 20;
  LblO.Top      := RbOrchestrator.Top + RbOrchestrator.Height + 2;
  LblO.Width    := RolePage.SurfaceWidth - 20;
  LblO.Caption  := 'Installs the MCP Server (port 5100) and Dashboard. Routes inference tasks to remote workers. Use this on your primary laptop or workstation.';
  LblO.WordWrap := True;

  // ── Worker ────────────────────────────────────────────────────────────────
  RbWorker := TNewRadioButton.Create(RolePage);
  RbWorker.Parent  := RolePage.Surface;
  RbWorker.Left    := 0;
  RbWorker.Top     := LblO.Top + LblO.Height + 16;
  RbWorker.Width   := RolePage.SurfaceWidth;
  RbWorker.Caption := 'Worker — Node Worker only (inference provider)';
  RbWorker.Font.Style := [fsBold];

  LblW := TNewStaticText.Create(RolePage);
  LblW.Parent   := RolePage.Surface;
  LblW.Left     := 20;
  LblW.Top      := RbWorker.Top + RbWorker.Height + 2;
  LblW.Width    := RolePage.SurfaceWidth - 20;
  LblW.Caption  := 'Installs the NodeWorker service (port 5050). Exposes local GPU inference to the Orchestrator over the network. Use this on a dedicated inference tower.';
  LblW.WordWrap := True;

  // ── Orchestrator + Worker ─────────────────────────────────────────────────
  RbFull := TNewRadioButton.Create(RolePage);
  RbFull.Parent  := RolePage.Surface;
  RbFull.Left    := 0;
  RbFull.Top     := LblW.Top + LblW.Height + 16;
  RbFull.Width   := RolePage.SurfaceWidth;
  RbFull.Caption := 'Orchestrator + Worker — all roles on this machine';
  RbFull.Font.Style := [fsBold];

  LblF := TNewStaticText.Create(RolePage);
  LblF.Parent   := RolePage.Surface;
  LblF.Left     := 20;
  LblF.Top      := RbFull.Top + RbFull.Height + 2;
  LblF.Width    := RolePage.SurfaceWidth - 20;
  LblF.Caption  := 'Installs everything: MCP Server, Dashboard, and NodeWorker. The Orchestrator routes tasks to the local Worker. Ideal for single-machine setups or when this machine has a capable GPU and you want a self-contained installation.';
  LblF.WordWrap := True;
end;

// ── Network configuration page ────────────────────────────────────────────────
procedure CreateNetworkPage;
begin
  NetworkPage := CreateInputQueryPage(RolePage.ID,
    'Network Configuration',
    'Configure ports and peer addresses. Leave Peer IP as-is if running Orchestrator+Worker on one machine.',
    '');

  NetworkPage.Add('Peer Orchestrator IP (Workers only, or leave empty for local):', False); // [0]
  NetworkPage.Add('MCP Server port:', False);    // [1]
  NetworkPage.Add('Node Worker port:', False);   // [2]
  NetworkPage.Add('Ollama URL (this machine):', False);  // [3]
  NetworkPage.Add('GitHub Copilot token (optional — for Node C):', True); // [4] password
  NetworkPage.Add('llama.cpp model file path (e.g. C:\Models\model.gguf):', False); // [5] — only used if llamacpp backend

  // Defaults
  NetworkPage.Values[0] := '192.168.1.X';
  NetworkPage.Values[1] := '5100';
  NetworkPage.Values[2] := '5050';
  NetworkPage.Values[3] := 'http://localhost:11434';
  NetworkPage.Values[4] := '';
  NetworkPage.Values[5] := 'C:\Models\qwen3-coder-30b-a3b.gguf';
end;

// ── Backend page (Worker / Full roles) ───────────────────────────────────────
// THREE options: Ollama, llama.cpp Native (no Docker), llama.cpp Docker.
// NOTE: NodeClient.LlamaCpp connects to http://localhost:8080 regardless of
// which option starts the server — the C# code does not care about the
// launch method. The endpoint is always OpenAI-compatible /v1/chat/completions.
procedure CreateBackendPage;
var
  Lbl: TNewStaticText;
  LblOllama, LblNative, LblDocker, LblNote: TNewStaticText;
begin
  BackendPage := CreateCustomPage(NetworkPage.ID,
    'Worker Inference Backend',
    'Choose how to run the inference server on this machine.');

  Lbl := TNewStaticText.Create(BackendPage);
  Lbl.Parent   := BackendPage.Surface;
  Lbl.Left     := 0;
  Lbl.Top      := 0;
  Lbl.Width    := BackendPage.SurfaceWidth;
  Lbl.Caption  := 'Select the inference backend for this Worker machine:';
  Lbl.WordWrap := True;

  // ── Option 1: Ollama ───────────────────────────────────────────────────────
  RbOllama := TNewRadioButton.Create(BackendPage);
  RbOllama.Parent    := BackendPage.Surface;
  RbOllama.Left      := 0;
  RbOllama.Top       := Lbl.Top + Lbl.Height + 16;
  RbOllama.Width     := BackendPage.SurfaceWidth;
  RbOllama.Caption   := 'Ollama  (recommended — simple, no extra tools required)';
  RbOllama.Checked   := True;
  RbOllama.Font.Style := [fsBold];

  LblOllama := TNewStaticText.Create(BackendPage);
  LblOllama.Parent   := BackendPage.Surface;
  LblOllama.Left     := 24;
  LblOllama.Top      := RbOllama.Top + RbOllama.Height + 2;
  LblOllama.Width    := BackendPage.SurfaceWidth - 24;
  LblOllama.Caption  := 'Standard setup using Ollama. Broad GPU support, easy model management. Flash attention disabled automatically for GTX 1080 (Pascal) stability.';
  LblOllama.WordWrap := True;

  // ── Option 2: llama.cpp Native (no Docker) ────────────────────────────────
  RbLlamaCppNative := TNewRadioButton.Create(BackendPage);
  RbLlamaCppNative.Parent    := BackendPage.Surface;
  RbLlamaCppNative.Left      := 0;
  RbLlamaCppNative.Top       := LblOllama.Top + LblOllama.Height + 14;
  RbLlamaCppNative.Width     := BackendPage.SurfaceWidth;
  RbLlamaCppNative.Caption   := 'llama.cpp Native  (Windows EXE — no Docker required, recommended for llama.cpp)';
  RbLlamaCppNative.Font.Style := [fsBold];

  LblNative := TNewStaticText.Create(BackendPage);
  LblNative.Parent   := BackendPage.Surface;
  LblNative.Left     := 24;
  LblNative.Top      := RbLlamaCppNative.Top + RbLlamaCppNative.Height + 2;
  LblNative.Width    := BackendPage.SurfaceWidth - 24;
  LblNative.Caption  := 'Downloads llama-server.exe (CUDA 12.4 build) from GitHub Releases and creates a launch script. Runs natively on Windows with full GPU access. Enables MoE offloading (--n-cpu-moe) for 30B+ models on limited VRAM. No Docker or container runtime required.';
  LblNative.WordWrap := True;

  // ── Option 3: llama.cpp Docker ────────────────────────────────────────────
  RbLlamaCppDocker := TNewRadioButton.Create(BackendPage);
  RbLlamaCppDocker.Parent    := BackendPage.Surface;
  RbLlamaCppDocker.Left      := 0;
  RbLlamaCppDocker.Top       := LblNative.Top + LblNative.Height + 14;
  RbLlamaCppDocker.Width     := BackendPage.SurfaceWidth;
  RbLlamaCppDocker.Caption   := 'llama.cpp Docker  (requires Docker Desktop + NVIDIA Container Toolkit)';
  RbLlamaCppDocker.Font.Style := [fsBold];

  LblDocker := TNewStaticText.Create(BackendPage);
  LblDocker.Parent   := BackendPage.Surface;
  LblDocker.Left     := 24;
  LblDocker.Top      := RbLlamaCppDocker.Top + RbLlamaCppDocker.Height + 2;
  LblDocker.Width    := BackendPage.SurfaceWidth - 24;
  LblDocker.Caption  := 'Runs ghcr.io/ggml-org/llama.cpp:server-cuda in a Docker container. Identical flags and performance to Native. Use this if you prefer containerized deployments or already have Docker set up.';
  LblDocker.WordWrap := True;

  // ── Shared note ────────────────────────────────────────────────────────────
  LblNote := TNewStaticText.Create(BackendPage);
  LblNote.Parent     := BackendPage.Surface;
  LblNote.Left       := 0;
  LblNote.Top        := LblDocker.Top + LblDocker.Height + 10;
  LblNote.Width      := BackendPage.SurfaceWidth;
  LblNote.Caption    := 'All three options expose http://localhost:8080 — SplitBrain.AI''s NodeClient.LlamaCpp connects to the same endpoint regardless. Switch anytime via NODE_B_BACKEND=llamacpp (or ollama).';
  LblNote.WordWrap   := True;
  LblNote.Font.Color := clGray;
end;

// ── Initialise wizard ─────────────────────────────────────────────────────────
procedure InitializeWizard;
begin
  CRole      := 'Orchestrator';
  CBackend   := 'ollama';
  CCudaBuild := '12.4';

  CreateRolePage;
  CreateNetworkPage;
  CreateBackendPage;
end;

// ── Capture role from radio buttons ──────────────────────────────────────────
procedure CaptureRole;
begin
  if      RbOrchestrator.Checked then CRole := 'Orchestrator'
  else if RbWorker.Checked       then CRole := 'Worker'
  else                                CRole := 'Full';
end;

// ── Capture backend from radio buttons ───────────────────────────────────────
procedure CaptureBackend;
begin
  if      RbOllama.Checked         then CBackend := 'ollama'
  else if RbLlamaCppNative.Checked then CBackend := 'llamacpp-native'
  else                                  CBackend := 'llamacpp-docker';
end;

// ── Capture values when leaving pages ────────────────────────────────────────
procedure CurPageChanged(CurPageID: Integer);
begin
  // Capture role when entering the network page
  if CurPageID = NetworkPage.ID then
    CaptureRole;

  // Capture backend when entering the task page
  if CurPageID = wpSelectTasks then
    CaptureBackend;
end;

// ── Page visibility ───────────────────────────────────────────────────────────
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  // Backend selection only relevant when a Worker is installed
  if (PageID = BackendPage.ID) and not HasWorker then
    Result := True;
end;

// ── Capture final values before install ───────────────────────────────────────
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    CaptureRole;
    CaptureBackend;
  end;
end;

// ── Validation ────────────────────────────────────────────────────────────────
function NextButtonClick(CurPageID: Integer): Boolean;
var
  PeerIp  : String;
  McpPort : Integer;
begin
  Result := True;

  if CurPageID = NetworkPage.ID then
  begin
    // Peer IP is required for Worker-only installs (Orchestrator must know where to connect).
    // For Orchestrator-only or Full, it's optional (Full routes to localhost).
    CaptureRole;
    PeerIp := Trim(NetworkPage.Values[0]);
    if (CRole = 'Worker') and ((PeerIp = '') or (PeerIp = '192.168.1.X')) then
    begin
      MsgBox('For a Worker installation, please enter the IP address of the Orchestrator machine (e.g. 192.168.1.10). This is the machine running the MCP Server.' + #13#10 + #13#10 + 'Tip: for Orchestrator+Worker on one machine, choose the "Orchestrator + Worker" role instead.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    McpPort := StrToIntDef(Trim(NetworkPage.Values[1]), 0);
    if (McpPort < 1024) or (McpPort > 65535) then
    begin
      MsgBox('MCP Server port must be between 1024 and 65535 (default: 5100).', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

// ── Build argument strings for [Run] scripts ──────────────────────────────────
function GetWriteConfigArgs(Param: String): String;
var
  PeerIp, McpPort, WorkerPort, OllamaUrl, CopilotToken, EffectivePeerIp: String;
begin
  PeerIp       := Trim(NetworkPage.Values[0]);
  McpPort      := Trim(NetworkPage.Values[1]);
  WorkerPort   := Trim(NetworkPage.Values[2]);
  OllamaUrl    := Trim(NetworkPage.Values[3]);
  CopilotToken := Trim(NetworkPage.Values[4]);

  // Full mode: Orchestrator connects to the local Worker, ignore peer IP
  if IsFull then EffectivePeerIp := 'localhost'
  else           EffectivePeerIp := PeerIp;

  Result := Format('-InstallDir "%s" -Role "%s" -PeerIp "%s" -McpPort %s -WorkerPort %s -OllamaUrl "%s"',
    [ExpandConstant('{app}'), CRole, EffectivePeerIp, McpPort, WorkerPort, OllamaUrl]);

  if CopilotToken <> '' then
    Result := Result + Format(' -CopilotToken "%s"', [CopilotToken]);

  // Pass backend to Write-Config — llamacpp-native and llamacpp-docker both
  // use "llamacpp" from NodeWorker's perspective (same endpoint, same env var)
  if HasWorker and HasLlamaCpp then
    Result := Result + ' -NodeBBackend llamacpp';
end;

// ── Setup-LlamaCpp.ps1 argument builder ───────────────────────────────────────
function HasLlamaCppSelected: Boolean;
begin
  Result := HasWorker and HasLlamaCpp;
end;

function GetLlamaCppSetupArgs(Param: String): String;
var
  Mode      : String;
  ModelPath : String;
  CudaVer   : String;
begin
  if IsLlamaCppNative then Mode := 'native'
  else                     Mode := 'docker';

  // Model path from NetworkPage [5] (empty = script uses its built-in default)
  ModelPath := Trim(NetworkPage.Values[5]);

  // CUDA build version (default 12.4 unless RbCuda13 exists and is checked)
  CudaVer := '12.4';
  CCudaBuild := CudaVer;

  Result := Format('-InstallDir "%s" -Mode %s -LlamaCppPort 8080 -CudaBuild %s',
    [ExpandConstant('{app}'), Mode, CudaVer]);

  if ModelPath <> '' then
    Result := Result + Format(' -ModelPath "%s"', [ModelPath]);
end;

function GetFirewallArgs(Param: String): String;
var
  McpPort, WorkerPort: String;
begin
  McpPort    := Trim(NetworkPage.Values[1]);
  WorkerPort := Trim(NetworkPage.Values[2]);

  if HasOrchestrator and HasWorker then
    // Full: open both MCP+Dashboard ports and Worker port
    Result := '-Role Both -McpPort ' + McpPort + ' -DashboardPort 5000 -WorkerPort ' + WorkerPort
  else if HasOrchestrator then
    Result := '-Role NodeA -McpPort ' + McpPort + ' -DashboardPort 5000'
  else
    Result := '-Role NodeB -WorkerPort ' + WorkerPort;
end;

function GetPullModelsArgs(Param: String): String;
var
  EffectiveBackend: String;
begin
  // Map ternary CBackend to the two values Pull-Models.ps1 understands
  if HasLlamaCpp then EffectiveBackend := 'llamacpp'
  else                EffectiveBackend := 'ollama';

  if HasWorker then
    // Worker or Full: pass the backend — Pull-Models.ps1 skips if llamacpp
    Result := '-Role Worker -Backend ' + EffectiveBackend
  else
    // Orchestrator only: pull Node A / Orchestrator model
    Result := '-Role Orchestrator';
end;

// ── Pre-install prereq check ─────────────────────────────────────────────────
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode : Integer;
begin
  Result := '';
  // Inline .NET check — external scripts not yet available at this stage.
  // {app} doesn't exist until ssInstall; run a direct dotnet check instead.
  if not Exec('powershell.exe',
    '-NonInteractive -Command "if (-not (dotnet --list-runtimes 2>$null | Select-String ''Microsoft.NETCore.App 10.'')) { exit 1 }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := 0;  // Exec failed — skip, let installer proceed

  if ResultCode = 1 then
    MsgBox(
      '.NET 10 runtime was not detected on this machine.' + #13#10 +
      'The installer will attempt to install it automatically.' + #13#10 + #13#10 +
      'If automatic install fails, download .NET 10 from:' + #13#10 +
      '  https://dotnet.microsoft.com/download/dotnet/10.0',
      mbInformation, MB_OK);
end;
