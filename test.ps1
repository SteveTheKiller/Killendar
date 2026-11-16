# Runs the Release test suite before release.ps1. Does not sign or publish.
# Builds bin/obj and writes TRX evidence to ResultsDirectory. Exits nonzero on failure.
# Compatible with Windows PowerShell 5.1 and PowerShell 7.
[CmdletBinding()]
param(
    [string]$ResultsDirectory = (Join-Path $env:USERPROFILE ('scratch\killendar-tests\retained\' + [Guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$previousTestRoot = $env:KILLENDAR_TEST_ROOT
Push-Location $PSScriptRoot
try {
    $ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
    New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null
    $resultFile = Join-Path $ResultsDirectory 'tests.trx'
    if (Test-Path -LiteralPath $resultFile) {
        throw 'ResultsDirectory already contains tests.trx. Choose a new results directory.'
    }
    if (-not $env:KILLENDAR_TEST_ROOT) {
        $env:KILLENDAR_TEST_ROOT = Join-Path $env:USERPROFILE 'scratch\killendar-tests\disposable\profile'
    }

    Write-Host 'Running Release tests' -ForegroundColor Cyan
    dotnet test 'Killendar.sln' -c Release --nologo --logger 'trx;LogFileName=tests.trx' --results-directory $ResultsDirectory
    if ($LASTEXITCODE -ne 0) { throw "Release tests failed (exit $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $resultFile)) { throw 'The test runner produced no TRX results.' }
    [xml]$results = Get-Content -LiteralPath $resultFile -Raw
    $counters = $results.TestRun.ResultSummary.Counters
    if (-not $counters -or [int]$counters.executed -le 0 -or [int]$counters.failed -ne 0 -or
        $results.TestRun.ResultSummary.outcome -ne 'Completed') {
        throw 'The test run did not complete successfully with executed tests.'
    }
    Write-Host "Tests passed. Results: $resultFile" -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    $env:KILLENDAR_TEST_ROOT = $previousTestRoot
    Pop-Location
}
