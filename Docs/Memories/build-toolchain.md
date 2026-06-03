# Build Toolchain — Installer

Last updated: June 2026

## Installer: Inno Setup 7

The installer is built via `installer/build.ps1`, which:
1. Publishes .NET projects to `installer/output/`
2. Runs `iscc.exe` on `installer/SplitBrainAI.iss`

### ISCC Auto-Detection

`installer/build.ps1` locates `ISCC.exe` from these candidates (in order):
- `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
- `C:\Program Files\Inno Setup 6\ISCC.exe`
- `C:\Program Files (x86)\Inno Setup 7\ISCC.exe`
- `C:\Program Files\Inno Setup 7\ISCC.exe`
- `D:\Program Files\Inno Setup 7\ISCC.exe` (user's install location)
- `iscc` from `PATH`

If ISCC is installed elsewhere, pass `-IsccPath` explicitly.

### Inno Setup 7 Beta Parsing Quirk

**Inno Setup 7.0.1-beta** (and likely all 7.x betas) has a stricter parser than 6.x: a `[` bracket appearing at the **start of any content line** (even indented inside a `[Code]` Pascal Script block) is misinterpreted as a section tag, producing:

```
Error on line N: Invalid section tag.
```

**Affected patterns:**
```pascal
// ❌ BROKEN in IS 7 beta — array bracket at start of line
Result := Format('...',
    [AppDir, CRole, ...]);

// ❌ Also broken — ExpandConstant inline inside array
  if not Exec('powershell.exe',
    Format('...',
      [ExpandConstant('{tmp}\...'), ...]),
```

**Fix:** Keep the array on the same line as `Format(...` and pre-compute `ExpandConstant(...)` calls into local variables:

```pascal
// ✅ WORKS — array on same line
Result := Format('...', [AppDir, CRole, ...]);

// ✅ WORKS — ExpandConstant pre-computed
ScriptPath := ExpandConstant('{tmp}\installer-scripts\Test-Prerequisites.ps1');
Exec('powershell.exe',
    Format('...', [ScriptPath, CheckRole, ...]), ...);
```

**Note:** `[fsBold]`, `Values[0]`, and `// [0]` comments are fine because the `[` is mid-line, not at the start of a content line.
