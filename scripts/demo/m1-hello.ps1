# SPDX-License-Identifier: AGPL-3.0-only
#
# Milestone 1 demo (Windows/PowerShell): a fresh NexoPBX install, from nothing to a client reading live events
# through the public API. Same steps as m1-hello.sh. Reuses $env:NEXOPBX_BIN_DIR / $env:NEXOPBX_SAMPLE_DIR if set.
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("nexopbx-demo-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$httpPort = if ($env:NEXOPBX_DEMO_HTTP_PORT) { $env:NEXOPBX_DEMO_HTTP_PORT } else { 18080 }
$grpcPort = if ($env:NEXOPBX_DEMO_GRPC_PORT) { $env:NEXOPBX_DEMO_GRPC_PORT } else { 18081 }
$server = $null

function Invoke-Checked {
    param([scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE" }
}

try {
    $bin = $env:NEXOPBX_BIN_DIR
    $sample = $env:NEXOPBX_SAMPLE_DIR
    if (-not $bin) {
        Write-Host '==> Publishing the server'
        Invoke-Checked { dotnet publish "$root\src\NexoPBX.App" -c Release -o "$work\bin" --nologo -v q }
        $bin = "$work\bin"
    }
    if (-not $sample) {
        Write-Host '==> Publishing the sample client'
        Invoke-Checked { dotnet publish "$root\samples\NexoPBX.SampleClient" -c Release -o "$work\sample" --nologo -v q }
        $sample = "$work\sample"
    }

    $env:NEXOPBX_DATA_DIR = "$work\data"
    $env:NEXOPBX_CONFIG = "$work\nexopbx.yaml"
    @"
server:
  http_listen: 127.0.0.1:$httpPort
  grpc_listen: 127.0.0.1:$grpcPort
logging:
  level: Warning
"@ | Set-Content -Path $env:NEXOPBX_CONFIG -Encoding utf8

    Write-Host "==> Creating the first Owner and an API token (no 'db migrate' needed on a fresh install)"
    'correct horse battery staple' | & "$bin\nexopbxd.exe" user bootstrap --username admin --password-stdin
    if ($LASTEXITCODE -ne 0) { throw 'bootstrap failed' }
    $token = (& "$bin\nexopbxd.exe" token create --user admin --name demo --scope system.read --scope users.read --scope events.subscribe 2>$null | Select-Object -First 1).Trim()
    if (-not $token) { throw 'token creation failed' }

    Write-Host '==> Starting the server'
    $server = Start-Process -FilePath "$bin\nexopbxd.exe" -ArgumentList 'run' -PassThru -NoNewWindow `
        -RedirectStandardOutput "$work\server.log" -RedirectStandardError "$work\server.err"
    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        try { Invoke-RestMethod "http://127.0.0.1:$httpPort/health/ready" | Out-Null; $ready = $true }
        catch {
            if ($server.HasExited) { Get-Content "$work\server.log", "$work\server.err" -ErrorAction SilentlyContinue; throw 'Server exited early' }
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $ready) { Get-Content "$work\server.log", "$work\server.err" -ErrorAction SilentlyContinue; throw 'Server never became ready' }

    Write-Host '==> REST: GET /v1/system/info'
    Invoke-RestMethod "http://127.0.0.1:$httpPort/v1/system/info" -Headers @{ Authorization = "Bearer $token" } | ConvertTo-Json -Compress

    Write-Host '==> Sample client self-test over native gRPC'
    $env:NEXOPBX_URL = "http://127.0.0.1:$grpcPort"
    $env:NEXOPBX_TOKEN = $token
    Invoke-Checked { dotnet "$sample\NexoPBX.SampleClient.dll" --selftest }

    Write-Host '==> OK: M1 demo passed'
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
    Start-Sleep -Milliseconds 300
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
