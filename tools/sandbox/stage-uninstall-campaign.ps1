#requires -Version 5.1
<#
.SYNOPSIS
  Host side of the Windows Sandbox campaign: publishes the campaign harness and copies what the
  sandbox needs into a staging folder. It builds only; it installs, uninstalls and launches nothing.

.EXAMPLE
  ./tools/sandbox/stage-uninstall-campaign.ps1
  # then open tools/sandbox/uninstall-campaign.wsb by hand; results land in C:\agenstart-campaign\results
#>
[CmdletBinding()]
param(
    # Must match the HostFolder paths of uninstall-campaign.wsb.
    [string] $StagingDirectory = 'C:\agenstart-campaign'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$payload = Join-Path $StagingDirectory 'payload'
New-Item -ItemType Directory -Force -Path $payload, (Join-Path $StagingDirectory 'results') | Out-Null

# Self-contained, because the sandbox has no .NET runtime.
dotnet publish (Join-Path $root 'tools\uninstall-campaign\AgenStart.UninstallCampaign.csproj') `
    --configuration Release --runtime win-x64 --self-contained true --output (Join-Path $payload 'harness')
if ($LASTEXITCODE -ne 0) {
    throw "Publishing the campaign harness failed with exit code $LASTEXITCODE."
}

Copy-Item -Force -Path (Join-Path $root 'tools\sandbox\run-uninstall-campaign.ps1') -Destination $payload
Copy-Item -Force -Path (Join-Path $root 'tools\sandbox\uninstall-campaign.json') -Destination $payload
Copy-Item -Force -Path (Join-Path $root 'docs\product\uninstall-rules.json') -Destination $payload
Write-Host "Staged in $StagingDirectory. Open tools\sandbox\uninstall-campaign.wsb to run the campaign in Windows Sandbox."
