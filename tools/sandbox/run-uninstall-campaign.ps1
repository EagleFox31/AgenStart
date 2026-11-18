#requires -Version 5.1
<#
.SYNOPSIS
  Disposable-machine campaign for the official uninstall (P3). Installs every program of the
  manifest with WinGet, then lets AgenStart's own session uninstall it twice (Removed, then
  AlreadyRemoved) and writes a JSON report.

.DESCRIPTION
  THIS REALLY INSTALLS AND UNINSTALLS SOFTWARE. It refuses to run unless
  AGENSTART_DISPOSABLE_MACHINE=1 is set AND it runs inside Windows Sandbox (WDAGUtilityAccount)
  or on a GitHub-hosted runner. Never run it on a developer or user PC.

  Entry points: tools/sandbox/uninstall-campaign.wsb (Windows Sandbox) and
  .github/workflows/uninstall-campaign.yml (manual workflow_dispatch only).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Harness,
    [Parameter(Mandatory)] [string] $Manifest,
    [Parameter(Mandatory)] [string] $Rules,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    # Windows Sandbox ships without WinGet: install it from the Microsoft.WinGet.Client module.
    [switch] $BootstrapWinGet
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-DisposableMachine {
    if ($env:AGENSTART_DISPOSABLE_MACHINE -ne '1') {
        throw 'Refused: AGENSTART_DISPOSABLE_MACHINE=1 is not set.'
    }

    $sandbox = $env:USERNAME -eq 'WDAGUtilityAccount'
    $hostedRunner = ($env:GITHUB_ACTIONS -eq 'true') -and ($env:RUNNER_ENVIRONMENT -eq 'github-hosted')
    if (-not ($sandbox -or $hostedRunner)) {
        throw 'Refused: not inside Windows Sandbox (WDAGUtilityAccount) nor on a GitHub-hosted runner.'
    }
}

function Install-WinGetIfMissing {
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        return
    }

    if (-not $BootstrapWinGet) {
        throw 'WinGet is not available. Re-run with -BootstrapWinGet (Windows Sandbox) or use a runner image that ships it.'
    }

    Install-PackageProvider -Name NuGet -Force -Scope CurrentUser | Out-Null
    Install-Module -Name Microsoft.WinGet.Client -Force -Scope CurrentUser -Repository PSGallery
    Import-Module Microsoft.WinGet.Client
    Repair-WinGetPackageManager -AllUsers -Latest
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw 'WinGet could not be installed.'
    }
}

Assert-DisposableMachine
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$transcript = Join-Path $OutputDirectory 'campaign-transcript.txt'
Start-Transcript -Path $transcript -Force | Out-Null
try {
    Install-WinGetIfMissing
    $targets = (Get-Content -Raw -Path $Manifest | ConvertFrom-Json).targets

    foreach ($target in $targets) {
        Write-Host "Installing $($target.name) ($($target.winGetId), $($target.scope))"
        $arguments = @(
            'install', '--id', $target.winGetId, '--exact', '--source', 'winget', '--scope', $target.scope,
            '--silent', '--disable-interactivity', '--accept-package-agreements', '--accept-source-agreements'
        )
        if ($target.installerType) {
            $arguments += @('--installer-type', $target.installerType)
        }

        & winget @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Installing $($target.name) failed with exit code $LASTEXITCODE."
        }
    }

    $dataDirectory = Join-Path $OutputDirectory 'data'
    $report = Join-Path $OutputDirectory 'uninstall-campaign-report.json'
    & $Harness --manifest $Manifest --rules $Rules --data-dir $dataDirectory --report $report --confirm-disposable-machine
    $code = $LASTEXITCODE

    # The journals are part of the evidence (ADR-0007).
    $journal = Join-Path $dataDirectory 'uninstall\journal'
    if (Test-Path $journal) {
        Copy-Item -Recurse -Force -Path $journal -Destination (Join-Path $OutputDirectory 'journal')
    }
}
finally {
    Stop-Transcript | Out-Null
}

exit $code
