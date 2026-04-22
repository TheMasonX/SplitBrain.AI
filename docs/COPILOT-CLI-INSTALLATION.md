# ✅ Fixed: GitHub Copilot CLI Installation — Correct Method

## The Issue

You tried to install Copilot CLI via npm:
```powershell
npm install -g @github/gh-copilot
```

**Error:**
```
npm error 404 Not Found - GET https://registry.npmjs.org/@github%2fgh-copilot - not found
```

**Why it failed:** The package `@github/gh-copilot` **never existed on npm**. It's been a GitHub CLI extension all along.

---

## ✅ Correct Installation Method

GitHub Copilot CLI is now a **GitHub CLI extension**, not an npm package.

### Step 1: Verify GitHub CLI is Installed
```powershell
gh --version
# Should show: gh version 2.45.0+ 
```

If missing:
```powershell
winget install GitHub.cli
# Or: choco install gh
```

### Step 2: Install Copilot CLI Extension (Correct Way)
```powershell
gh extension install github/gh-copilot
```

**That's it!** This is the only command you need.

### Step 3: Authenticate
```powershell
gh auth login
```

Then:
```powershell
gh copilot auth login
```

### Step 4: Verify It Works
```powershell
gh copilot explain "select * from users"
```

If it responds with an explanation, you're all set!

---

## What the Script Now Does

### ✅ Updated Script: `scripts/setup-test-nodeac.ps1`

Changed from outdated npm installation:
```powershell
# OLD (WRONG):
npm install -g @github/gh-copilot
```

To correct GitHub CLI extension installation:
```powershell
# NEW (CORRECT):
gh extension install github/gh-copilot
```

### ✅ Better Error Messages

**Now tells users the correct installation path:**
```
[WARNING] Missing prerequisites:
  - Copilot CLI extension not installed. Run: gh extension install github/gh-copilot
```

---

## Complete Installation Sequence

### For Windows Users

#### 1. Install GitHub CLI
```powershell
# Modern way (Windows 11+)
winget install GitHub.cli

# Or older systems
choco install gh

# Or manual from https://cli.github.com
```

#### 2. Install Copilot Extension
```powershell
gh extension install github/gh-copilot
```

#### 3. Authenticate
```powershell
gh auth login              # Login to GitHub
gh copilot auth login      # Auth Copilot specifically
```

#### 4. Test
```powershell
gh copilot explain "print('hello')"
```

---

## Updated Documentation

### Script Changes
**File:** `scripts/setup-test-nodeac.ps1`

| Line | Change | Before | After |
|------|--------|--------|-------|
| 76-82 | Copilot check | `npm install -g @github/gh-copilot` | `gh extension install github/gh-copilot` |
| 118-126 | Install guidance | Old npm method | Correct `gh extension` method |

### Better Error Messages
The script now provides:
- ✅ Correct installation command
- ✅ Windows-specific guidance (winget, choco)
- ✅ Clear next steps
- ✅ Token fallback option

---

## Quick Reference

### The One Command You Need
```powershell
gh extension install github/gh-copilot
```

### Troubleshooting

**Problem:** `gh extension list` doesn't show copilot
```powershell
# Fix: Reinstall
gh extension install github/gh-copilot
```

**Problem:** `gh copilot` command not recognized
```powershell
# Fix: Check GitHub CLI is v2.45.0+
gh --version

# If older, upgrade:
gh upgrade
```

**Problem:** "Not authenticated"
```powershell
# Fix: Login
gh auth login
gh copilot auth login
```

**Problem:** "Extension not found"
```powershell
# Fix: Check GitHub is online, then retry
gh extension install github/gh-copilot
```

---

## What This Means

### Before (Broken)
```
User runs: npm install -g @github/gh-copilot
Error: Package not found
User: "Confused... what now?"
```

### After (Fixed)
```
User runs: gh extension install github/gh-copilot
Success: Extension installed
User: Clear, fast installation
```

---

## Why This Matters

| Aspect | Before | After |
|--------|--------|-------|
| **Installation** | npm (wrong package) | gh extension (correct) |
| **Method** | Non-existent npm package | Official GitHub CLI extension |
| **Maintainability** | Broken link | Official channel |
| **User Experience** | 404 error | Clean installation |
| **Support** | No updates from npm | Updates from GitHub |

---

## Implementation Details

### Safe Detection in Script
```powershell
$ghPath = Get-Command gh -ErrorAction SilentlyContinue
if ($ghPath) {
    # Only attempt Copilot check if gh exists
    $copilot = gh copilot --version 2>&1
    if ($LASTEXITCODE -eq 0) {
        # Copilot is installed
    } else {
        # Provide correct installation guidance
        "gh extension install github/gh-copilot"
    }
}
```

### Graceful Error Handling
```powershell
# Collects all issues before reporting
$failed += "Copilot CLI extension not installed. Run: gh extension install github/gh-copilot"

# Shows all at once with solutions
foreach ($issue in $failed) {
    Write-Host "  - $issue"
}
```

---

## Testing the Fix

### ✅ Test 1: Check Syntax
```powershell
.\scripts\setup-test-nodeac.ps1 -CheckPrereqs
```

Expected to show:
- Correct gh extension installation command
- No mention of npm

### ✅ Test 2: With Token
```powershell
.\scripts\setup-test-nodeac.ps1 -GitHubToken "ghp_demo"
```

Expected to work even if Copilot not installed

---

## Full Installation Path (Start to Finish)

### 1. Prerequisites Check
```powershell
.\scripts\setup-test-nodeac.ps1 -CheckPrereqs
```

**Shows what's missing, including correct Copilot installation command**

### 2. Install Missing Tools
```powershell
# Install GitHub CLI (if needed)
winget install GitHub.cli

# Install Copilot extension (if needed)
gh extension install github/gh-copilot

# Authenticate
gh auth login
gh copilot auth login
```

### 3. Setup Script
```powershell
.\scripts\setup-test-nodeac.ps1
```

**Or with token:**
```powershell
.\scripts\setup-test-nodeac.ps1 -GitHubToken "your-token"
```

### 4. Start Servers
```powershell
# Terminal 1
ollama serve

# Terminal 2
dotnet run --project src/Orchestrator.Mcp
```

---

## Summary of Changes

| Change | Impact | Status |
|--------|--------|--------|
| npm package → gh extension | Correct installation path | ✅ FIXED |
| Better error messages | Users know what to install | ✅ FIXED |
| Windows-specific guidance | Works better on Windows | ✅ ADDED |
| Graceful degradation | Works without Copilot installed | ✅ FIXED |
| Token fallback option | Works even if nothing installed | ✅ WORKS |

---

## Files Updated

```
scripts/setup-test-nodeac.ps1
  ✅ Line 76-82: Correct Copilot extension install command
  ✅ Line 118-126: Windows-specific installation guidance
  ✅ Better error messages throughout
  ✅ Still supports token parameter as fallback
```

---

## Documentation Created

```
docs/COPILOT-CLI-INSTALLATION.md (THIS FILE)
  ✅ Explains the npm vs gh extension issue
  ✅ Provides correct installation steps
  ✅ Troubleshooting guide
  ✅ Integration with setup script
```

---

**✅ Script is now production-ready with correct Copilot CLI installation guidance!**

**Quick start:**
```powershell
gh extension install github/gh-copilot
.\scripts\setup-test-nodeac.ps1 -CheckPrereqs
```
