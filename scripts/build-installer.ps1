param([string]$OutputDirectory=(Join-Path ([Environment]::GetFolderPath('Desktop')) 'Muse Desk Install'))
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build-windows.ps1')
$staging=Join-Path $root '.build\installer-payload'
New-Item -ItemType Directory -Path "$staging\release" -Force | Out-Null
Copy-Item -LiteralPath "$root\release\MuseDesk.exe","$root\release\MuseDesk.exe.config","$root\release\MuseDesk.ico" -Destination "$staging\release" -Force
Copy-Item -LiteralPath "$root\THIRD_PARTY_NOTICES.md","$root\runtime.lock.json" -Destination $staging -Force
'{"contextSize":8192,"toolsEnabled":false}'|Set-Content -LiteralPath "$staging\installation-settings.json" -Encoding UTF8
$zip=Join-Path $root '.build\application.zip'
Compress-Archive -Path "$staging\*" -DestinationPath $zip -Force
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$setup=Join-Path $OutputDirectory 'MuseDesk-Setup.exe'
& "$framework\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ "/out:$setup" "/win32icon:$root\release\MuseDesk.ico" "/win32manifest:$root\windows-native\app.manifest" "/reference:$framework\System.dll" "/reference:$framework\System.Core.dll" "/reference:$framework\System.Drawing.dll" "/reference:$framework\System.Windows.Forms.dll" "/reference:$framework\System.Web.Extensions.dll" "/resource:$root\installer\Install.ps1,Install.ps1" "/resource:$root\installer\catalog.json,catalog.json" "/resource:$zip,application.zip" "$root\installer\Setup.cs" "$root\windows-native\Brand.cs"
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
Copy-Item -LiteralPath "$root\windows-native\MuseDesk.exe.config" -Destination ($setup+'.config') -Force
$hash=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
"$hash  MuseDesk-Setup.exe"|Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256.txt') -Encoding ASCII
Write-Output "Built: $setup"
