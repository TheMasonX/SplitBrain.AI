# PowerShell Script Gotchas

Source: Merged from `SCRIPT-FIX-SUMMARY.md`, `SCRIPT-IMPROVEMENTS.md`, `FINAL-SCRIPT-STATUS.md`, `COMPLETE-RESOLUTION.md`, `RESOLUTION-SUMMARY.md`, `QUICK-START-Script.md`
Status: **Unconsolidated** — candidate for a `Docs/Memories/scripting-gotchas.md` once scripts are stable

---

## Gotcha 1: Unicode Characters Break PowerShell Encoding

Emoji and Unicode check marks (`✓`, `✗`, `✅`, `❌`) in PowerShell scripts cause encoding errors on systems with non-UTF-8 default codepages.

**Fix:** Replace with ASCII equivalents.
```powershell
# ❌ Breaks:  Write-Host "✓ Done"
# ✅ Works:   Write-Host "[OK] Done"
# ✅ Works:   Write-Host "[FAIL] Error"
```

## Gotcha 2: Here-String Syntax Conflicts with Emoji/Special Chars

`@" ... "@` here-strings that contain special characters can cause parse errors on strict PS environments.

**Fix:** Use individual `Write-Host` calls instead of here-strings for output blocks.

## Gotcha 3: `gh` Not Found Should Not Crash

If `gh` (GitHub CLI) is not in PATH, any script that calls it should fail gracefully, not throw a terminating error.

**Fix:** Wrap `gh` calls in `try/catch` or use `Get-Command gh -ErrorAction SilentlyContinue` before calling.

## Gotcha 4: Invoke-WebRequest Needs `-UseBasicParsing`

On systems with IE engine (common in Server/restricted environments), `Invoke-WebRequest` without `-UseBasicParsing` triggers IE initialization which can time out.

**Fix:**
```powershell
# ❌ Can timeout:    Invoke-WebRequest $url
# ✅ Always safe:    Invoke-WebRequest $url -UseBasicParsing
```

## Script Location
`scripts/setup-test-nodeac.ps1` — main test setup script for Node A + Node C scenarios.
All four fixes above have been applied to this script.
