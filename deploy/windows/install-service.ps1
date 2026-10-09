# SPDX-License-Identifier: AGPL-3.0-only
#
# Installs NexoPBX as a Windows Service (run from an elevated PowerShell).
#   .\install-service.ps1 -BinDir 'C:\Program Files\NexoPBX'
# Configuration lives in C:\ProgramData\NexoPBX\nexopbx.yaml, data in C:\ProgramData\NexoPBX\data.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $BinDir,
    [string] $ServiceName = 'NexoPBX',
    [switch] $NoStart
)
$ErrorActionPreference = 'Stop'

$exe = Join-Path (Resolve-Path $BinDir) 'nexopbxd.exe'
if (-not (Test-Path $exe)) { throw "nexopbxd.exe not found in $BinDir" }

$programData = Join-Path $env:ProgramData 'NexoPBX'
New-Item -ItemType Directory -Force -Path $programData, (Join-Path $programData 'data'), (Join-Path $programData 'logs') | Out-Null

$config = Join-Path $programData 'nexopbx.yaml'
if (-not (Test-Path $config)) {
    @"
logging:
  level: Information
  file: $((Join-Path $programData 'logs\nexopbx-.log').Replace('\', '/'))
"@ | Set-Content -Path $config -Encoding utf8
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Service '$ServiceName' already exists. Run uninstall-service.ps1 first."
}

New-Service -Name $ServiceName -BinaryPathName "`"$exe`" service" -DisplayName 'NexoPBX' `
    -Description 'NexoPBX telephone system' -StartupType Automatic | Out-Null
# Restart automatically after a crash: after 5s, 5s, then 30s.
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null

if (-not $NoStart) { Start-Service -Name $ServiceName }
Write-Host "Installed service '$ServiceName'. Configuration: $config"
