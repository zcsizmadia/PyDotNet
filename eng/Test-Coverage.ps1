param(
    [Parameter(Mandatory = $true)]
    [string] $Report,
    [string] $Baseline = "$PSScriptRoot/coverage-baseline.json",
    [double] $MinimumLine = 60.0,
    [double] $MinimumBranch = 45.0
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Report)) {
    throw "Coverage report not found: $Report"
}

[xml] $coverage = Get-Content -LiteralPath $Report
$baselineValues = Get-Content -LiteralPath $Baseline | ConvertFrom-Json
$line = [math]::Round(([double] $coverage.coverage.'line-rate') * 100, 2)
$branch = [math]::Round(([double] $coverage.coverage.'branch-rate') * 100, 2)
$requiredLine = [math]::Max($MinimumLine, [double] $baselineValues.line)
$requiredBranch = [math]::Max($MinimumBranch, [double] $baselineValues.branch)

Write-Host "Coverage: lines $line% (required $requiredLine%), branches $branch% (required $requiredBranch%)"
Write-Host "Roadmap targets: lines $($baselineValues.targetLine)%, branches $($baselineValues.targetBranch)%"

$failures = @()
if ($line -lt $requiredLine) {
    $failures += "Line coverage $line% is below $requiredLine%."
}
if ($branch -lt $requiredBranch) {
    $failures += "Branch coverage $branch% is below $requiredBranch%."
}

if ($failures.Count -gt 0) {
    throw ($failures -join ' ')
}
