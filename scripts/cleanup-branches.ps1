<#
.SYNOPSIS
    Deletes all stale feature branches that have been merged to main.
    Run from the repo root after reviewing the list below.
.NOTES
    Requires gh CLI (winget install GitHub.cli) + gh auth login
    Or run from GitHub Actions with GITHUB_TOKEN set.
#>

$Repo = "TheMasonX/SplitBrain.AI"

$BranchesToDelete = @(
    "feature/batch-1a-concurrency-safety",
    "feature/batch-1b-auth-error-handling",
    "feature/batch-2-deduplication",
    "feature/batch-3-robustness",
    "feature/batch-4-security-hardening",
    "feature/p0-p1-tools",
    "feature/phase-0-fixes",
    "feature/phase-0-v2",
    "feature/phase-1-tools",
    "feature/phase-a-foundation",
    "feature/roadmap-tasks-and-plan",
    "next-gen"  # retain or delete based on preference
)

Write-Host "Deleting merged feature branches from $Repo" -ForegroundColor Cyan
Write-Host "(All content is in main at 427dd26e)" -ForegroundColor Gray
Write-Host ""

foreach ($branch in $BranchesToDelete) {
    Write-Host "  Deleting $branch ..." -NoNewline
    $result = gh api -X DELETE "repos/$Repo/git/refs/heads/$branch" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host " OK" -ForegroundColor Green
    } else {
        Write-Host " SKIP ($result)" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Done. Remaining branches:" -ForegroundColor Cyan
gh api "repos/$Repo/branches" --jq '.[].name'
