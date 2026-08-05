param(
    [string] $Configuration = 'Release',
    [string] $Framework = 'net10.0',
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'coverage'
$rawOutput = Join-Path $output 'raw'
$projects = @(
    'PyDotNet.Tests',
    'PyDotNet.Lifecycle.Tests',
    'PyDotNet.Snippets.Tests',
    'PyDotNet.NumPy.Tests',
    'PyDotNet.DataFrames.Tests',
    'PyDotNet.Torch.Tests',
    'PyDotNet.Matplotlib.Tests'
)

New-Item -ItemType Directory -Path $rawOutput -Force | Out-Null
Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

    if (-not $NoBuild) {
        dotnet build PyDotNet.slnx -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Coverage build failed.' }
    }

    foreach ($name in $projects) {
        $assembly = Join-Path $root "tests/$name/bin/$Configuration/$Framework/$name.dll"
        if (-not (Test-Path -LiteralPath $assembly)) {
            throw "Test assembly not found: $assembly"
        }

        $arguments = @(
            'exec', $assembly,
            '--maximum-parallel-tests', '1',
            '--coverlet',
            '--coverlet-file-prefix', $name,
            '--coverlet-output-format', 'cobertura',
            '--coverlet-include', '[PyDotNet*]*',
            '--coverlet-exclude', '[*.Tests]*',
            '--coverlet-exclude-by-file', '**/obj/**',
            '--coverlet-exclude-by-file', '**/*.g.cs',
            '--coverlet-exclude-by-file', '**/*.generated.cs',
            '--coverlet-exclude-by-attribute', 'GeneratedCodeAttribute,ExcludeFromCodeCoverageAttribute'
        )
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "Coverage tests failed for $name." }
    }

    $reports = foreach ($name in $projects) {
        Get-ChildItem "tests/$name/bin/$Configuration/$Framework/TestResults" -Filter "$name.coverage.cobertura.*.xml" |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
    }
    if ($reports.Count -ne $projects.Count) {
        throw "Expected $($projects.Count) coverage reports but found $($reports.Count)."
    }

    $mergedReport = Join-Path $output 'coverage.cobertura.xml'
    $mergeArguments = @('tool', 'run', 'dotnet-coverage', 'merge') +
        @($reports.FullName) +
        @('--output', $mergedReport, '--output-format', 'cobertura')
    & dotnet @mergeArguments
    if ($LASTEXITCODE -ne 0) { throw 'Coverage merge failed.' }

    & "$PSScriptRoot/Test-Coverage.ps1" -Report $mergedReport
}
finally {
    Pop-Location
}
