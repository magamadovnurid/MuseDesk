$ErrorActionPreference='Stop'
$config=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'private\server.json') -Raw | ConvertFrom-Json
$interface=Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq $config.host | Select-Object -First 1
if(!$interface) { throw 'The configured network address is no longer available.' }
$name='MuseDesk-WiFi-11437'
if(Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue) { Write-Output 'Firewall rule already exists.'; exit 0 }
New-NetFirewallRule -Name $name -DisplayName 'Muse Desk - encrypted home network access' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $config.port -LocalAddress $config.host -RemoteAddress $config.subnet -InterfaceAlias $interface.InterfaceAlias -Profile Any | Out-Null
Write-Output 'Muse Desk port is open only on the configured interface and local subnet.'
