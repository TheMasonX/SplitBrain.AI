# MemorySmith Wiki Deployment for SplitBrain.AI

## What We Did
Set up a MemorySmith wiki service for the SplitBrain.AI MCP server project,
modeled after course wikis at `C:\@Repos\School\ (CMST-341` / `ASTR-107`).

## Deployment Pattern (Reusable)
Each wiki = a `data/` directory with standard MemorySmith layout, plus:

- `scripts/Deploy-WikiService.ps1` — publishes MemorySmith.App, installs Windows service
- `scripts/Publish-WikiEngine.ps1` — `dotnet publish` from MemorySmith repo
- `scripts/Stop-WikiService.ps1` — stops/deletes the service
- `scripts/Get-WikiServiceStatus.ps1` — queries status
- `scripts/Remove-WikiService.ps1` — teardown with optional artifact cleanup
- `scripts/Warm-CodeSearchIndex.ps1` — warms code search via MCP endpoint
- `scripts/Install-CodeSearchModel.ps1` — downloads ONNX embedding model
- `.service/wiki.port` — active port file
- `data/codesearch-config.json` — CodeSearch settings override

## Service Parameters
- Service name: `SplitBrain.AI Wiki`
- Port: 6769
- MemorySmith repo: `C:\Users\norrt\source\repos\MemorySmith`
- Data root: `<repo>/data/`

## Key Differences from Course Wikis
- Course wikis used names like `MemorySmith - CMST` and `*-CourseWikiService*` scripts
- MCP project uses `SplitBrain.AI Wiki` and `*-WikiService*` naming
- Course wikis had `%CourseRepo%` vars; MCP project uses `%SplitBrainRepo%`

## Command-Line Service Install Pattern
```powershell
dotnet $appDll install `
  --service-name "$ServiceName" `
  --service-display-name "$ServiceName" `
  --service-description "..." `
  --service-start-type auto `
  --memory-directory <MemoriesPath> `
  -- `
  --urls http://0.0.0.0:<Port> `
  --MemorySmith:AllowRemoteApi true `
  --MemorySmith:SettingsOverridePath <config-override.json> `
  --MemorySmith:DataProtectionKeysPath <KeysPath> `
  --MemorySmith:AllowedFileRoots:0 <Root0> ...
  --MemorySmith:SourceLinks:AllowedFileRoots:0 <Root0> ...
```

## Gotchas
- Windows service re-install requires `dotnet $appDll uninstall` first, not just `sc.exe delete`
- `appsettings.Secrets.json` is loaded from `AppContext.BaseDirectory` (service working dir)
- Settings override path via `MemorySmith:SettingsOverridePath` key
- MemorySmith uses `Microsoft.NET.Sdk.Web`, so `AddJsonFile("appsettings.LocalOverrides.json")` is not used — override via CLI args or dedicated file
