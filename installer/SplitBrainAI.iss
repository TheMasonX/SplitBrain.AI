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
Name: "nodeA"; Description: "Node A — Machine A (MCP Server + Dashboard)"
Name: "nodeB"; Description: "Node B — Machine B (Node Worker, deep inference)"

[Components]
Name: "mcp";       Description: "MCP Server (Orchestrator.Mcp)";         Types: nodeA; Flags: fixed
Name: "dashboard"; Description: "SplitBrain Dashboard (Blazor UI)";       Types: nodeA
Name: "worker";    Description: "Node Worker (Orchestrator.NodeWorker)";   Types: nodeB; Flags: fixed
Name: "data";      Description: "Wiki / Documentation (data/)";            Types: nodeA nodeB

[Tasks]
Name: "installservice";   Description: "Install as Windows &Service (auto-start on boot)";   GroupDescription: "Service options:"
Name: "addfirewall";      Description: "Add Windows &Firewall rules for LAN access";          GroupDescription: "Network:"
Name: "pullmodels";       Description: "Download &Ollama models now (requires internet)";     GroupDescription: "Ollama models:"
Name: "desktopicon";      Description: "Create a &Desktop shortcut";                          GroupDescription: "Shortcuts:"; Flags: unchecked

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
    Tasks: installservice; Components: mcp

Filename: "powershell.exe"; \
    Parameters: "-NonInteractive -ExecutionPolicy Bypass -Command ""& '{{app}}\deploy\setup-node-b.ps1' -PublishPath '{{app}}\node-b\worker' -SkipDotNet -SkipOllama -SkipModels"""; \
    StatusMsg: "Installing Node Worker service..."; \
    Flags: runhidden waituntilterminated; \
    Tasks: installservice; Components: worker

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
  SummaryPage     : TWizardPage;

  // Radio buttons on RolePage
  RbNodeA         : TNewRadioButton;
  RbNodeB         : TNewRadioButton;

  // Radio buttons on BackendPage
  RbOllama        : TNewRadioButton;
  RbLlamaCpp      : TNewRadioButton;
  LblLlamaCppNote : TNewStaticText;

  // Network page controls (created via TInputQueryWizardPage)
  // Fields: PeerIp, McpPort / WorkerPort, OllamaUrl, CopilotToken (Node A only)

  // Cached values from pages
  CRole           : String;   // "NodeA" or "NodeB"
  CBackend        : String;   // "ollama" or "llamacpp"

// ── Helper: which role is selected? ──────────────────────────────────────────
function IsNodeA: Boolean;
begin
  Result := (CRole = 'NodeA');
end;

function IsNodeB: Boolean;
begin
  Result := (CRole = 'NodeB');
end;

// ── RolePage: Node A vs Node B ───────────────────────────────────────────────
procedure CreateRolePage;
var
  Lbl, LblA, LblB: TNewStaticText;
begin
  RolePage := CreateCustomPage(wpWelcome, 'Select Installation Role',
    'Which machine are you installing on?');

  Lbl := TNewStaticText.Create(RolePage);
  Lbl.Parent := RolePage.Surface;
  Lbl.Left   := 0;
  Lbl.Top    := 0;
  Lbl.Width  := RolePage.SurfaceWidth;
  Lbl.Caption := 'SplitBrain.AI runs across two machines. Choose the role for this machine:';
  Lbl.WordWrap := True;

  RbNodeA := TNewRadioButton.Create(RolePage);
  RbNodeA.Parent  := RolePage.Surface;
  RbNodeA.Left    := 0;
  RbNodeA.Top     := Lbl.Top + Lbl.Height + 16;
  RbNodeA.Width   := RolePage.SurfaceWidth;
  RbNodeA.Caption := 'Node A — Machine A (MCP Server + Dashboard + fast inference)';
  RbNodeA.Checked := True;
  RbNodeA.Font.Style := [fsBold];

  LblA := TNewStaticText.Create(RolePage);
  LblA.Parent   := RolePage.Surface;
  LblA.Left     := 20;
  LblA.Top      := RbNodeA.Top + RbNodeA.Height + 2;
  LblA.Width    := RolePage.SurfaceWidth - 20;
  LblA.Caption  := 'Your laptop or primary workstation. Runs the MCP server, Dashboard, and Node A inference. Typically has an RTX 5060+ GPU.';
  LblA.WordWrap := True;

  RbNodeB := TNewRadioButton.Create(RolePage);
  RbNodeB.Parent  := RolePage.Surface;
  RbNodeB.Left    := 0;
  RbNodeB.Top     := LblA.Top + LblA.Height + 16;
  RbNodeB.Width   := RolePage.SurfaceWidth;
  RbNodeB.Caption := 'Node B — Machine B (Deep inference worker)';
  RbNodeB.Font.Style := [fsBold];

  LblB := TNewStaticText.Create(RolePage);
  LblB.Parent   := RolePage.Surface;
  LblB.Left     := 20;
  LblB.Top      := RbNodeB.Top + RbNodeB.Height + 2;
  LblB.Width    := RolePage.SurfaceWidth - 20;
  LblB.Caption  := 'Your inference tower or secondary machine. Runs the NodeWorker service that handles deep inference. Typically has a GTX 1080+ GPU.';
  LblB.WordWrap := True;
end;

// ── Network configuration page ────────────────────────────────────────────────
procedure CreateNetworkPage;
begin
  NetworkPage := CreateInputQueryPage(RolePage.ID,
    'Network Configuration',
    'Configure how the two nodes connect to each other.',
    '');

  NetworkPage.Add('Peer machine IP address:', False);    // [0]
  NetworkPage.Add('MCP Server port (Node A):', False);   // [1]
  NetworkPage.Add('Node Worker port (Node B):', False);  // [2]
  NetworkPage.Add('Ollama URL (this machine):', False);  // [3]
  NetworkPage.Add('GitHub Copilot token (optional):', True);  // [4] — password

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
  CRole    := 'NodeA';
  CBackend := 'ollama';

  CreateRolePage;
  CreateNetworkPage;
  CreateBackendPage;
end;

// ── Capture values when leaving pages ────────────────────────────────────────
procedure CurPageChanged(CurPageID: Integer);
begin
  // Capture role selection
  if CurPageID = NetworkPage.ID then
  begin
    if RbNodeA.Checked then CRole := 'NodeA'
    else                     CRole := 'NodeB';
  end;

  // Capture backend selection
  if CurPageID = wpSelectTasks then
  begin
    if RbOllama.Checked then CBackend := 'ollama'
    else                     CBackend := 'llamacpp';
  end;

  // Hide backend page for Node A, show for Node B
  if CurPageID = BackendPage.ID then
  begin
    if IsNodeA then
      // Skip backend page for Node A
      WizardForm.NextButton.OnClick(WizardForm.NextButton);
  end;
end;

// ── Page visibility ───────────────────────────────────────────────────────────
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  // Skip backend selection page for Node A
  if (PageID = BackendPage.ID) and IsNodeA then
    Result := True;
end;

// ── Component selection based on role ────────────────────────────────────────
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Capture final values
    if RbNodeA.Checked then CRole := 'NodeA' else CRole := 'NodeB';
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
    PeerIp := Trim(NetworkPage.Values[0]);
    if (PeerIp = '') or (PeerIp = '192.168.1.X') then
    begin
      MsgBox('Please enter the IP address of the peer machine (e.g. 192.168.1.50).', mbError, MB_OK);
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
  PeerIp, McpPort, WorkerPort, OllamaUrl, CopilotToken: String;
begin
  PeerIp       := Trim(NetworkPage.Values[0]);
  McpPort      := Trim(NetworkPage.Values[1]);
  WorkerPort   := Trim(NetworkPage.Values[2]);
  OllamaUrl    := Trim(NetworkPage.Values[3]);
  CopilotToken := Trim(NetworkPage.Values[4]);

  Result := Format('-InstallDir "%s" -Role "%s" -PeerIp "%s" -McpPort %s -WorkerPort %s -OllamaUrl "%s"',
    [ExpandConstant('{app}'), CRole, PeerIp, McpPort, WorkerPort, OllamaUrl]);

  if CopilotToken <> '' then
    Result := Result + Format(' -CopilotToken "%s"', [CopilotToken]);

  if (CRole = 'NodeB') and (CBackend = 'llamacpp') then
    Result := Result + ' -NodeBBackend llamacpp';
end;

function GetFirewallArgs(Param: String): String;
begin
  if IsNodeA then Result := '-Role NodeA -McpPort ' + Trim(NetworkPage.Values[1]) + ' -DashboardPort 5000'
  else            Result := '-Role NodeB -WorkerPort ' + Trim(NetworkPage.Values[2]);
end;

function GetPullModelsArgs(Param: String): String;
begin
  if IsNodeA then Result := '-Role NodeA'
  else            Result := '-Role NodeB -Backend ' + CBackend;
end;

// ── Pre-install prereq check ─────────────────────────────────────────────────
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode : Integer;
  TempFile   : String;
begin
  Result := '';
  TempFile := ExpandConstant('{tmp}\prereq-results.json');

  // Run prerequisite check script
  if not Exec('powershell.exe',
    Format('-NonInteractive -ExecutionPolicy Bypass -File "%s\installer-scripts\Test-Prerequisites.ps1" -Role %s -OutputFile "%s"',
      [ExpandConstant('{app}'), CRole, TempFile]),
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Exit;  // Exec failed — skip, let installer proceed

  // ResultCode 1 = hard fail; show warning but allow user to continue
  if ResultCode = 1 then
    MsgBox('One or more prerequisite checks failed. The installation will continue, but SplitBrain.AI may not work correctly. Check the setup guide at data\Pages\guides\getting-started.md after installation.', mbError, MB_OK);
end;
