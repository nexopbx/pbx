# SPDX-License-Identifier: AGPL-3.0-only
#
# Installs the published server as a Windows Service with deploy/windows/install-service.ps1, proves that it starts
# under the Service Control Manager, answers health checks, and stops and uninstalls cleanly. Needs an elevated shell.
#   .\scripts\ci\smoke-windows-service.ps1 -BinDir <published-directory>
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $BinDir)
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$httpPort = 18082
$grpcPort = 18083
$name = 'NexoPBX'
$programData = Join-Path $env:ProgramData 'NexoPBX'

function Show-Diagnostics {
    Write-Host '--- service log files'
    Get-ChildItem (Join-Path $programData 'logs') -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_.FullName; Get-Content $_.FullName -Tail 60 }
    Write-Host '--- application event log'
    Get-WinEvent -LogName Application -MaxEvents 15 -ErrorAction SilentlyContinue | Where-Object { $_.ProviderName -match 'NexoPBX|\.NET Runtime|Application Error' } | Format-List TimeCreated, ProviderName, Message
}

try {
    & "$root\deploy\windows\install-service.ps1" -BinDir $BinDir -NoStart
    Add-Content -Path (Join-Path $programData 'nexopbx.yaml') -Value "server:`n  http_listen: 127.0.0.1:$httpPort`n  grpc_listen: 127.0.0.1:$grpcPort"

    Write-Host '==> Start-Service (the SCM waits for the host to report started)'
    Start-Service -Name $name
    (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

    Write-Host '==> health'
    $ready = $false
    for ($i = 0; $i -lt 30 -and -not $ready; $i++) {
        try { Invoke-RestMethod "http://127.0.0.1:$httpPort/health/ready" | Out-Null; $ready = $true } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw 'The service never became ready' }
    Invoke-RestMethod "http://127.0.0.1:$httpPort/health/live"

    Write-Host '==> data was created under ProgramData'
    if (-not (Test-Path (Join-Path $programData 'data\nexopbx.db'))) { throw 'database file not found' }

    Write-Host '==> Stop-Service'
    Stop-Service -Name $name
    (Get-Service -Name $name).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    Write-Host '==> OK: Windows service smoke test passed'
}
catch {
    Show-Diagnostics
    throw
}
finally {
    & "$root\deploy\windows\uninstall-service.ps1"
}
