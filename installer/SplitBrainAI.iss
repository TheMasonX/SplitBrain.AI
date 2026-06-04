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
OutputDir={#OutputBase}
OutputBaseFilename=SplitBrainAI-Setup-{#MyAppVersion}
; Fast compression — lzma2/ultra is painfully slow on copilot.exe (~106 MB native binary)
Compression=zip
SolidCompression=no
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

[Tasks]
Name: "installservice";     Description: "Install as Windows &Service (auto-start on boot)";   GroupDescription: "Service options:"
Name: "installservice\mcp"; Description: "MCP Server service";                                 GroupDescription: "Service options:"; Check: HasOrchestrator
Name: "installservice\wrk"; Description: "Node Worker service";                                GroupDescription: "Service options:"; Check: HasWorker
Name: "addfirewall";        Description: "Add Windows &Firewall rules for LAN access";         GroupDescription: "Network:"
Name: "pullmodels";         Description: "Download &Ollama models now (auto-skipped if llama.cpp selected)"; GroupDescription: "Ollama models:"; Flags: unchecked
Name: "desktopicon";        Description: "Create a &Desktop shortcut";                         GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
; ── Node A binaries ───────────────────────────────────────────────────────────
; copilot.exe (~106 MB native binary) — no compression to speed up build
Source: "output\node-a\mcp\runtimes\win-x64\native\copilot.exe"; DestDir: "{app}\node-a\mcp\runtimes\win-x64\native"; Flags: ignoreversion
Source: "output\node-a\dashboard\runtimes\win-x64\native\copilot.exe"; DestDir: "{app}\node-a\dashboard\runtimes\win-x64\native"; Flags: ignoreversion
Source: "output\node-a\mcp\*";       DestDir: "{app}\node-a\mcp";       Flags: recursesubdirs ignoreversion; Excludes: "*.pdb, copilot.exe"
Source: "output\node-a\dashboard\*"; DestDir: "{app}\node-a\dashboard"; Flags: recursesubdirs ignoreversion; Excludes: "*.pdb, copilot.exe"

; ── Node B binaries ───────────────────────────────────────────────────────────
Source: "output\node-b\worker\*";    DestDir: "{app}\node-b\worker";    Flags: recursesubdirs ignoreversion; Excludes: "*.pdb"

; ── Deploy / setup scripts ────────────────────────────────────────────────────
Source: "{#RepoRoot}\deploy\*";      DestDir: "{app}\deploy";           Flags: recursesubdirs ignoreversion
Source: "scripts\*";                 DestDir: "{app}\installer-scripts"; Flags: recursesubdirs ignoreversion

; DO NOT UNCOMMENT! These are for development convenience only and would bloat the installer size massively.
; ── Data / wiki ───────────────────────────────────────────────────────────────
; Source: "{#RepoRoot}\data\*";        DestDir: "{app}\data";             Components: data; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist

; ── Docs ─────────────────────────────────────────────────────────────────────
Source: "{#RepoRoot}\README.md";     DestDir: "{app}";                  Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE";       DestDir: "{app}";                  Flags: ignoreversion

[Icons]
; Node A shortcuts
Name: "{group}\SplitBrain.AI MCP Server"; Filename: "{app}\node-a\mcp\Orchestrator.Mcp.exe"; Check: HasOrchestrator
Name: "{group}\SplitBrain Dashboard";     Filename: "{app}\node-a\dashboard\SplitBrain.Dashboard.exe"; Check: HasOrchestrator
; Node B shortcuts
Name: "{group}\SplitBrain.AI Node Worker"; Filename: "{app}\node-b\worker\Orchestrator.NodeWorker.exe"; Check: HasWorker
; Common
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
; Desktop
Name: "{autodesktop}\SplitBrain.AI";  Filename: "{app}\node-a\mcp\Orchestrator.Mcp.exe"; Check: HasOrchestrator; Tasks: desktopicon

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
    Tasks: installservice\mcp

Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -Command ""& '{{app}}\deploy\setup-node-b.ps1' -PublishPath '{{app}}\node-b\worker' -SkipDotNet -SkipOllama -SkipModels"""; \
    StatusMsg: "Installing Node Worker service..."; \
    Flags: runhidden waituntilterminated; \
    Tasks: installservice\wrk

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
    Check: HasLlamaCppSelected

; ── Launch after install (offer in Finish page) ───────────────────────────────
Filename: "{app}\node-a\mcp\Orchestrator.Mcp.exe"; \
    Description: "Launch MCP Server now"; \
    Flags: nowait postinstall skipifsilent; \
    Check: HasOrchestrator

Filename: "{app}\node-a\dashboard\SplitBrain.Dashboard.exe"; \
    Description: "Launch Dashboard now"; \
    Flags: nowait postinstall skipifsilent; \
    Check: HasOrchestrator

[UninstallRun]
; Stop and remove services on uninstall
Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -Command ""Stop-Service SplitBrainMcpServer -Force -ea 0; sc.exe delete SplitBrainMcpServer"""; \
    Flags: runhidden; Check: HasOrchestrator
Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -Command ""Stop-Service SplitBrainNodeWorker -Force -ea 0; sc.exe delete SplitBrainNodeWorker"""; \
    Flags: runhidden; Check: HasWorker
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
  OptionalPage    : TInputQueryWizardPage;
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

// ── Registry helpers for value persistence ──────────────────────────────────
// Must be declared before any page creation procedure that calls them.
function RestoreValue(KeyName, Default: String): String;
begin
  Result := Default;
  if RegQueryStringValue(HKCU, 'Software\SplitBrain.AI\Setup', KeyName, Result) then
    Exit;
  Result := Default;
end;

procedure SaveValue(KeyName, Value: String);
begin
  RegWriteStringValue(HKCU, 'Software\SplitBrain.AI\Setup', KeyName, Value);
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
  Lbl.AutoSize := False;
  Lbl.Height   := ScaleY(50);

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
  LblO.AutoSize := False;
  LblO.Height   := ScaleY(45);

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
  LblW.AutoSize := False;
  LblW.Height   := ScaleY(45);

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
  LblF.AutoSize := False;
  LblF.Height   := ScaleY(65);
end;

// ── File open dialog via PowerShell (real file picker with .gguf filter) ───
function SelectModelFile(const InitialDir: String): String;
var
  ScriptFile, TmpFile, PsCode: String;
  ResultVal: AnsiString;
  ResultCode: Integer;
  SafeDir: String;
  Wd: String;
begin
  Result := '';
  ScriptFile := ExpandConstant('{tmp}\_filedlg.ps1');
  TmpFile    := ExpandConstant('{tmp}\_filedlg_out.txt');
  DeleteFile(TmpFile);

  // Escape backslashes for PowerShell string
  SafeDir := InitialDir;
  StringChange(SafeDir, '\', '\\');
  StringChange(SafeDir, '"', '\"');

  // Use the working directory of the current path, or fall back to C:\
  Wd := ExtractFileDir(InitialDir);
  if Wd = '' then Wd := 'C:\';
  StringChange(Wd, '\', '\\');

  PsCode :=
    'Add-Type -AssemblyName System.Windows.Forms; ' +
    '[System.Windows.Forms.Application]::EnableVisualStyles(); ' +
    '$d = New-Object System.Windows.Forms.OpenFileDialog; ' +
    '$d.Title = ''Select llama.cpp GGUF Model File''; ' +
    '$d.Filter = ''GGUF model files (*.gguf)|*.gguf|All files (*.*)|*.*''; ' +
    '$d.RestoreDirectory = $true; ' +
    'if (''' + SafeDir + ''' -ne '''') { ' +
    '  if (Test-Path ''' + Wd + ''') { $d.InitialDirectory = ''' + Wd + '''; }; ' +
    '  if (Test-Path ''' + SafeDir + ''') { $d.FileName = ''' + SafeDir + '''; }; ' +
    '}; ' +
    'if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { ' +
    '  [System.IO.File]::WriteAllText(''' + TmpFile + ''', $d.FileName); ' +
    '}';
  SaveStringToFile(ScriptFile, PsCode, False);

  if not Exec('powershell.exe',
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' +
    '& { $e = $ErrorActionPreference; $ErrorActionPreference=''Stop''; ' +
    '. ''' + ScriptFile + '''; ' +
    '$ErrorActionPreference = $e }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Exit;

  if FileExists(TmpFile) then
  begin
    LoadStringFromFile(TmpFile, ResultVal);
    Result := Trim(ResultVal);
    DeleteFile(TmpFile);
  end;
end;

// ── Browse button handler — opens real file dialog, sets the path if chosen ──
procedure ModelBrowseClick(Sender: TObject);
var
  Chosen: String;
begin
  Chosen := SelectModelFile(OptionalPage.Values[1]);
  if Chosen <> '' then
    OptionalPage.Values[1] := Chosen;
end;

// ── Network configuration page ────────────────────────────────────────────────
procedure CreateNetworkPage;
begin
  NetworkPage := CreateInputQueryPage(RolePage.ID,
    'Network Configuration',
    'Configure ports and peer addresses. Leave Peer IP as-is if running Orchestrator+Worker on one machine.',
    '');

  NetworkPage.Add('Orchestrator IP (Worker only):', False); // [0]
  NetworkPage.Add('MCP Server port:', False);                // [1]
  NetworkPage.Add('Node Worker port:', False);               // [2]
  NetworkPage.Add('Ollama URL:', False);                     // [3]

  // Defaults (restored from previous install if available)
  NetworkPage.Values[0] := RestoreValue('PeerIp', '192.168.1.X');
  NetworkPage.Values[1] := RestoreValue('McpPort', '5100');
  NetworkPage.Values[2] := RestoreValue('WorkerPort', '5050');
  NetworkPage.Values[3] := RestoreValue('OllamaUrl', 'http://localhost:11434');
end;

// ── Optional settings page (token, model path) ──────────────────────────────
procedure CreateOptionalPage;
var
  BrowseBtn: TNewButton;
begin
  OptionalPage := CreateInputQueryPage(NetworkPage.ID,
    'Optional Settings',
    'GitHub Copilot token and llama.cpp model path. These are optional and can be configured later.',
    '');

  OptionalPage.Add('GitHub Copilot token:', True); // [0] password
  OptionalPage.Add('llama.cpp model path:', False);        // [1]

  // Defaults (restored from previous install if available)
  OptionalPage.Values[0] := RestoreValue('CopilotToken', '');
  OptionalPage.Values[1] := RestoreValue('ModelPath', '');

  // ── "Browse..." button for the model path field ─────────────────────────
  // Shrink edit width to make room, then place button to its right.
  OptionalPage.Edits[1].Width := OptionalPage.Edits[1].Width - 85;

  BrowseBtn := TNewButton.Create(OptionalPage);
  BrowseBtn.Parent := OptionalPage.Surface;
  BrowseBtn.Left   := OptionalPage.Edits[1].Left + OptionalPage.Edits[1].Width + 4;
  BrowseBtn.Top    := OptionalPage.Edits[1].Top;
  BrowseBtn.Width  := 79;
  BrowseBtn.Height := OptionalPage.Edits[1].Height;
  BrowseBtn.Caption := 'Browse...';
  BrowseBtn.OnClick := @ModelBrowseClick;
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
  Lbl.AutoSize := False;
  Lbl.Height   := ScaleY(50);

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
  LblOllama.AutoSize := False;
  LblOllama.Height   := ScaleY(45);

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
  LblNative.AutoSize := False;
  LblNative.Height   := ScaleY(65);

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
  LblDocker.AutoSize := False;
  LblDocker.Height   := ScaleY(45);

  // ── Shared note ────────────────────────────────────────────────────────────
  LblNote := TNewStaticText.Create(BackendPage);
  LblNote.Parent     := BackendPage.Surface;
  LblNote.Left       := 0;
  LblNote.Top        := LblDocker.Top + LblDocker.Height + 10;
  LblNote.Width      := BackendPage.SurfaceWidth;
  LblNote.Caption    := 'All three options expose http://localhost:8080 — SplitBrain.AI''s NodeClient.LlamaCpp connects to the same endpoint regardless. Switch anytime via NODE_B_BACKEND=llamacpp (or ollama).';
  LblNote.WordWrap   := True;
  LblNote.AutoSize   := False;
  LblNote.Height     := ScaleY(65);
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
  CreateOptionalPage;
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

// ── Persist wizard values on install ─────────────────────────────────────────
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    CaptureRole;
    CaptureBackend;

    // Persist wizard values for next run
    SaveValue('PeerIp',      Trim(NetworkPage.Values[0]));
    SaveValue('McpPort',     Trim(NetworkPage.Values[1]));
    SaveValue('WorkerPort',  Trim(NetworkPage.Values[2]));
    SaveValue('OllamaUrl',   Trim(NetworkPage.Values[3]));
    SaveValue('CopilotToken',Trim(OptionalPage.Values[0]));
    SaveValue('ModelPath',   Trim(OptionalPage.Values[1]));
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
    if (CRole = 'Worker') and (PeerIp = '') then
    begin
      MsgBox('Please enter the Orchestrator machine IP address (e.g. 192.168.1.10).' + #13#10 + #13#10 + 'Tip: for a single-machine setup, choose "Orchestrator + Worker" instead.', mbError, MB_OK);
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
  CopilotToken := Trim(OptionalPage.Values[0]);

  // Full mode: Orchestrator connects to the local Worker, ignore peer IP
  if IsFull then EffectivePeerIp := 'localhost'
  else           EffectivePeerIp := PeerIp;

  Result := Format('-InstallDir "%s" -Role "%s" -PeerIp "%s" -McpPort %s -WorkerPort %s -OllamaUrl "%s"', [ExpandConstant('{app}'), CRole, EffectivePeerIp, McpPort, WorkerPort, OllamaUrl]);

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
  ModelPath := Trim(OptionalPage.Values[1]);

  // CUDA build version (default 12.4 unless RbCuda13 exists and is checked)
  CudaVer := '12.4';
  CCudaBuild := CudaVer;

  Result := Format('-InstallDir "%s" -Mode %s -LlamaCppPort 8080 -CudaBuild %s', [ExpandConstant('{app}'), Mode, CudaVer]);

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
  CheckCmd  : String;
  CheckDesc : String;
begin
  Result := '';

  // Role-aware .NET 10 check:
  //   Server (Orchestrator/Full) — need ASP.NET Core Runtime (hosting bundle)
  //   Worker only               — need .NET Runtime only
  CaptureRole;
  if HasOrchestrator then
  begin
    CheckCmd  := 'if (-not (dotnet --list-runtimes 2>$null | Select-String ''Microsoft.AspNetCore.App 10.'')) { exit 1 }';
    CheckDesc := 'ASP.NET Core Runtime 10 (hosting bundle)';
  end else begin
    CheckCmd  := 'if (-not (dotnet --list-runtimes 2>$null | Select-String ''Microsoft.NETCore.App 10.'')) { exit 1 }';
    CheckDesc := '.NET Runtime 10';
  end;

  if not Exec('powershell.exe',
    '-NonInteractive -Command "' + CheckCmd + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := 0;  // Exec failed — skip, let installer proceed

  if ResultCode = 1 then
    MsgBox(
      CheckDesc + ' was not detected on this machine.' + #13#10 +
      'The installer will attempt to install it automatically.' + #13#10 + #13#10 +
      'If automatic install fails, download .NET 10 from:' + #13#10 +
      '  https://dotnet.microsoft.com/download/dotnet/10.0',
      mbInformation, MB_OK);
end;
