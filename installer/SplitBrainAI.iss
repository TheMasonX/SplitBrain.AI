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
Name: "pullmodels";         Description: "Download &Ollama models now (requires internet)";    GroupDescription: "Ollama models:"
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

; ── Pull Ollama models (optional) ────────────────────────────────────────────
Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -File ""{app}\installer-scripts\Pull-Models.ps1"" {code:GetPullModelsArgs}"; \
    StatusMsg: "Downloading Ollama models (this may take several minutes)..."; \
    Flags: runhidden waituntilterminated; \
    Tasks: pullmodels

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
  RbOllama        : TNewRadioButton;
  RbLlamaCpp      : TNewRadioButton;
  LblLlamaCppNote : TNewStaticText;

  // Cached values from pages
  CRole    : String;   // "Orchestrator" | "Worker" | "Full"
  CBackend : String;   // "ollama" | "llamacpp"

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

  // Defaults
  NetworkPage.Values[0] := '192.168.1.X';
  NetworkPage.Values[1] := '5100';
  NetworkPage.Values[2] := '5050';
  NetworkPage.Values[3] := 'http://localhost:11434';
  NetworkPage.Values[4] := '';
end;

// ── Backend page (Node B only) ────────────────────────────────────────────────
procedure CreateBackendPage;
var
  Lbl, LblOllama, LblLlama: TNewStaticText;
begin
  BackendPage := CreateCustomPage(NetworkPage.ID,
    'Node B Inference Backend',
    'Choose the inference engine for Node B (GTX 1080).');

  Lbl := TNewStaticText.Create(BackendPage);
  Lbl.Parent   := BackendPage.Surface;
  Lbl.Left     := 0;
  Lbl.Top      := 0;
  Lbl.Width    := BackendPage.SurfaceWidth;
  Lbl.Caption  := 'Node B can use either Ollama or llama.cpp as its inference backend:';
  Lbl.WordWrap := True;

  RbOllama := TNewRadioButton.Create(BackendPage);
  RbOllama.Parent  := BackendPage.Surface;
  RbOllama.Left    := 0;
  RbOllama.Top     := Lbl.Top + Lbl.Height + 16;
  RbOllama.Width   := BackendPage.SurfaceWidth;
  RbOllama.Caption := 'Ollama (recommended — simpler setup, ~20-45 tok/s for 7B model)';
  RbOllama.Checked := True;
  RbOllama.Font.Style := [fsBold];

  LblOllama := TNewStaticText.Create(BackendPage);
  LblOllama.Parent   := BackendPage.Surface;
  LblOllama.Left     := 20;
  LblOllama.Top      := RbOllama.Top + RbOllama.Height + 2;
  LblOllama.Width    := BackendPage.SurfaceWidth - 20;
  LblOllama.Caption  := 'Standard setup. Runs qwen2.5-coder 7B. Flash attention disabled automatically for GTX 1080 (Pascal) stability.';
  LblOllama.WordWrap := True;

  RbLlamaCpp := TNewRadioButton.Create(BackendPage);
  RbLlamaCpp.Parent  := BackendPage.Surface;
  RbLlamaCpp.Left    := 0;
  RbLlamaCpp.Top     := LblOllama.Top + LblOllama.Height + 16;
  RbLlamaCpp.Width   := BackendPage.SurfaceWidth;
  RbLlamaCpp.Caption := 'llama.cpp server (advanced — enables 30B MoE models via MoE offloading)';
  RbLlamaCpp.Font.Style := [fsBold];

  LblLlama := TNewStaticText.Create(BackendPage);
  LblLlama.Parent   := BackendPage.Surface;
  LblLlama.Left     := 20;
  LblLlama.Top      := RbLlamaCpp.Top + RbLlamaCpp.Height + 2;
  LblLlama.Width    := BackendPage.SurfaceWidth - 20;
  LblLlama.Caption  := 'Requires Docker + NVIDIA Container Toolkit. Start llama-server manually before running NodeWorker. Uses --n-cpu-moe 25 --no-mmap --mlock --cache-type-k turbo4 --cache-type-v turbo3. See data\Pages\guides\llamacpp-gtx1080-setup.md after installation.';
  LblLlama.WordWrap := True;

  LblLlamaCppNote := TNewStaticText.Create(BackendPage);
  LblLlamaCppNote.Parent   := BackendPage.Surface;
  LblLlamaCppNote.Left     := 20;
  LblLlamaCppNote.Top      := LblLlama.Top + LblLlama.Height + 8;
  LblLlamaCppNote.Width    := BackendPage.SurfaceWidth - 20;
  LblLlamaCppNote.Caption  := 'You can switch backends at any time by setting NODE_B_BACKEND=llamacpp (or ollama) before starting the NodeWorker.';
  LblLlamaCppNote.WordWrap := True;
  LblLlamaCppNote.Font.Color := clGray;
end;

// ── Initialise wizard ─────────────────────────────────────────────────────────
procedure InitializeWizard;
begin
  CRole    := 'Orchestrator';
  CBackend := 'ollama';

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

// ── Capture values when leaving pages ────────────────────────────────────────
procedure CurPageChanged(CurPageID: Integer);
begin
  // Capture role when entering the network page
  if CurPageID = NetworkPage.ID then
    CaptureRole;

  // Capture backend when entering the task page
  if CurPageID = wpSelectTasks then
  begin
    if RbOllama.Checked then CBackend := 'ollama'
    else                     CBackend := 'llamacpp';
  end;
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
    if RbOllama.Checked then CBackend := 'ollama' else CBackend := 'llamacpp';
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

  if HasWorker and (CBackend = 'llamacpp') then
    Result := Result + ' -NodeBBackend llamacpp';
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
begin
  if HasWorker then
    // Worker or Full: pull worker models for selected backend
    Result := '-Role NodeB -Backend ' + CBackend
  else
    // Orchestrator only: pull Node A model
    Result := '-Role NodeA';
end;

// ── Pre-install prereq check ─────────────────────────────────────────────────
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode : Integer;
  TempFile   : String;
  CheckRole  : String;
begin
  Result := '';
  TempFile := ExpandConstant('{tmp}\prereq-results.json');

  // For Full installs, check as NodeB (stricter GPU / driver requirements)
  if IsFull or HasWorker then CheckRole := 'NodeB'
  else                        CheckRole := 'NodeA';

  // Run prerequisite check script using {tmp} — {app} doesn't exist yet at this point
  if not Exec('powershell.exe',
    Format('-NonInteractive -ExecutionPolicy Bypass -File "%s" -Role %s -OutputFile "%s"',
      [ExpandConstant('{tmp}\installer-scripts\Test-Prerequisites.ps1'), CheckRole, TempFile]),
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Exit;  // Exec failed — skip, let installer proceed

  // ResultCode 1 = hard fail; warn but allow continuation
  if ResultCode = 1 then
    MsgBox('One or more prerequisite checks failed. The installation will continue, but SplitBrain.AI may not work correctly.' + #13#10 + #13#10 + 'Check the setup guide at data\Pages\guides\getting-started.md after installation.', mbError, MB_OK);
end;
