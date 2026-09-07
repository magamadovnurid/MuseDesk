$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if(!(Test-Path -LiteralPath "$PSScriptRoot\private\server.json")) { throw 'Run setup.ps1 first.' }
$node=Join-Path $PSScriptRoot 'runtime\node.exe'
if(!(Test-Path -LiteralPath $node)) { throw 'Muse Wi-Fi Node runtime is missing.' }
$config=Get-Content -LiteralPath "$PSScriptRoot\private\server.json" -Raw | ConvertFrom-Json
if(!(Get-Process MuseDesk -ErrorAction SilentlyContinue)) { Start-Process -FilePath "$root\release\MuseDesk.exe" }
$listener=Get-NetTCPConnection -LocalPort $config.port -State Listen -ErrorAction SilentlyContinue
if($listener) { Write-Output 'A listener already exists on the Muse Wi-Fi port. No duplicate started.'; exit 0 }
$gateway=Join-Path $PSScriptRoot 'gateway.mjs'
$process=Start-Process -FilePath $node -ArgumentList @('"'+$gateway+'"') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -RedirectStandardOutput "$PSScriptRoot\private\gateway.log" -RedirectStandardError "$PSScriptRoot\private\gateway-error.log" -PassThru
$process.Id | Set-Content -LiteralPath "$PSScriptRoot\private\gateway.pid"
Write-Output "Muse Wi-Fi started: https://$($config.host):$($config.port)"
