# SPDX-License-Identifier: AGPL-3.0-only
#
# Stops and removes the NexoPBX Windows Service (run from an elevated PowerShell). Data is kept.
[CmdletBinding()]
param([string] $ServiceName = 'NexoPBX')
$ErrorActionPreference = 'Stop'

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) { Write-Host "Service '$ServiceName' is not installed."; return }
if ($service.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
sc.exe delete $ServiceName | Out-Null
Write-Host "Removed service '$ServiceName'. Data in $env:ProgramData\NexoPBX was kept."
