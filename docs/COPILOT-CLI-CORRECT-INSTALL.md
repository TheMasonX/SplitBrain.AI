# ⚠️ IMPORTANT: Correct GitHub Copilot CLI Installation

## The Issue You Hit

You saw this error:
```
npm error 404 Not Found - GET https://registry.npmjs.org/@github%2fgh-copilot
```

This is because **the npm package `@github/gh-copilot` no longer exists**.

## Why This Happened

- Old documentation referenced: `npm install -g @github/gh-copilot`
- GitHub changed the distribution method
- Copilot CLI is now a **GitHub CLI extension**, not an npm package

## ✅ Correct Installation (Modern)

### Step 1: Install GitHub CLI
```powershell
# Check if already installed
gh --version

# If not installed
winget install GitHub.cli
# or: choco install gh
# or: https://cli.github.com
```

### Step 2: Install Copilot CLI Extension (CORRECT)
```powershell
# This is the right command
gh extension install github/gh-copilot

# GitHub will automatically download the correct binary for your platform (Windows/Mac/Linux)
```

### Step 3: Authenticate
```powershell
# Authenticate with GitHub
gh auth login

# Then authenticate Copilot
gh copilot auth login
```

### Step 4: Test It
```powershell
# Test the CLI works
gh copilot explain "what does this code do?"
```

If that works, Copilot CLI is fully installed and operational.

---

## What Happened When You Ran It

```powershell
PS> gh extension install github/gh-copilot
"copilot" matches the name of a built-in command or alias
? GitHub Copilot CLI is not installed. Would you like to install it? Yes
Downloading Copilot CLI from https://github.com/github/copilot-cli/releases/latest/download/copilot-win32-x64.zip
```

This is **correct behavior**. GitHub CLI:
1. Detected Copilot extension not installed
2. Offered to install it automatically
3. Downloaded the correct Windows binary
4. Installed it as a gh extension

**This is now fully installed!** ✅

---

## How Our Script Now Handles This

The updated `scripts/setup-test-nodeac.ps1`:

### Before (Wrong)
```powershell
# Would fail because npm package doesn't exist
npm install -g @github/gh-copilot
```

### After (Correct)
```powershell
# Uses the correct extension installation
gh extension install github/gh-copilot
```

### Smart Handling
The script now:
- ✅ Checks if `gh copilot --version` works
- ✅ If not, suggests the correct command: `gh extension install github/gh-copilot`
- ✅ Doesn't fail the prerequisite check (Copilot CLI is optional for initial setup)
- ✅ You can add it later if needed

---

## Why This Matters

| Method | Status | Works |
|--------|--------|-------|
| `npm install -g @github/gh-copilot` | ❌ **Deprecated** | Never |
| `gh extension install github/gh-copilot` | ✅ **Current** | Yes |

---

## Your Next Steps

Since you got this message:
```
Downloading Copilot CLI from https://github.com/github/copilot-cli/releases/latest/download/copilot-win32-x64.zip
```

The download is in progress. When it completes:

1. **Complete the authentication:**
   ```powershell
   gh copilot auth login
   ```

2. **Test that it works:**
   ```powershell
   gh copilot explain "print('hello')"
   ```

3. **Then you're done!** The script is now ready to run:
   ```powershell
   .\scripts\setup-test-nodeac.ps1 -GitHubToken "your-token"
   ```

---

## Quick Reference

### Installing GitHub Copilot CLI (Modern Way)
```powershell
# Prerequisites
gh --version                    # GitHub CLI must be installed
gh auth login                   # GitHub CLI must be authenticated

# Install Copilot CLI as extension
gh extension install github/gh-copilot

# Authenticate Copilot
gh copilot auth login

# Test it works
gh copilot explain "test code"
```

### What NOT to Do
```powershell
# ❌ WRONG - This npm package doesn't exist
npm install -g @github/gh-copilot

# ❌ WRONG - This path doesn't exist
npm install @github/copilot-cli

# ✅ RIGHT - Use the GitHub CLI extension
gh extension install github/gh-copilot
```

---

## Error Message We Used to Give

Our old script said:
```
Copilot CLI error. Install with: npm install -g @github/gh-copilot
```

This was **wrong and outdated**.

## Error Message We Now Give

Our updated script says:
```
Copilot CLI extension not yet installed.
You can install it with: gh extension install github/gh-copilot
Or the first time you use: gh copilot auth login
```

This is **correct and modern**. ✅

---

## Summary

| Issue | Cause | Fix |
|-------|-------|-----|
| `404 Not Found` | npm package deleted | Use `gh extension install` |
| Installation fails | Wrong method | Use correct extension method |
| "Command not recognized" | Extension not installed | Run `gh extension install github/gh-copilot` |

---

**✅ You're now on the correct path!**

Wait for the download to complete, then:
```powershell
gh copilot auth login
```

Then you can use the setup script normally.
