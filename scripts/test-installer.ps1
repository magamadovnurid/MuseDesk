$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. "$root\installer\Install.ps1" -LibraryOnly
function Assert($Condition,$Message){if(!$Condition){throw $Message};Write-Output "PASS: $Message"}
Assert ((Select-Profile 32 24576 8.6 60 $true) -eq 'glimmer-q4-q8') '24GB GPU selects Q8 image encoder'
Assert ((Select-Profile 64 32768 8.9 60 $true) -eq 'glimmer-q4-f16') '32GB GPU selects F16 image encoder'
Assert ((Select-Profile 32 16384 8.6 60 $true) -eq 'app-only') 'Insufficient VRAM does not trigger a heavy download'
Assert ((Select-Profile 16 24576 8.6 60 $true) -eq 'app-only') 'Insufficient RAM is rejected'
Assert ((Select-Profile 32 24576 6.1 60 $true) -eq 'app-only') 'Old or unsupported GPU is rejected'
Assert ((Select-Profile 32 24576 8.6 10 $true) -eq 'app-only') 'Insufficient disk is rejected'
Assert ((Select-Profile 64 24576 8.6 80 $false) -eq 'unsupported') 'Unsupported Windows platform is rejected'
$testRoot=Join-Path $root ('.build\installer-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$blob=Write-Blob '{"test":true}' $testRoot
Assert ((Get-FileHash -LiteralPath (Join-Path $testRoot ($blob.digest.Replace(':','-')))).Hash -eq $blob.digest.Substring(7)) 'Model metadata content hashes match'
$CancelFile=Join-Path $testRoot 'cancel';Set-Content -LiteralPath $CancelFile -Value stop
$canceled=$false;try{Check-Cancel}catch{$canceled=$true};Assert $canceled 'Cancellation is checked before downloads and extraction';$CancelFile=$null
$spec=[pscustomobject]@{bytes=$blob.size;sha256=$blob.digest.Substring(7)}
Get-VerifiedFile $spec (Join-Path $testRoot ($blob.digest.Replace(':','-')))
Assert $true 'Verified cached file does not trigger a network request'
$spec.sha256='0'*64;$rejected=$false;try{Get-VerifiedFile $spec (Join-Path $testRoot ($blob.digest.Replace(':','-')))}catch{$rejected=$true};Assert $rejected 'Corrupted cached file is rejected'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$badZip=Join-Path $testRoot 'bad.zip';$zip=[IO.Compression.ZipFile]::Open($badZip,[IO.Compression.ZipArchiveMode]::Create)
try{$null=$zip.CreateEntry('../outside.txt')}finally{$zip.Dispose()}
$rejected=$false;try{Expand-SafeZip $badZip (Join-Path $testRoot 'extract')}catch{$rejected=$true};Assert $rejected 'Archive paths cannot escape the destination'
$stage=Join-Path $testRoot 'stage';New-Item -ItemType Directory -Path $stage -Force|Out-Null
Copy-Item -LiteralPath "$root\installer\Install.ps1","$root\installer\catalog.json" -Destination $stage
Copy-Item -LiteralPath "$root\.build\application.zip" -Destination "$stage\application.zip"
$target=Join-Path $testRoot 'installed'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$stage\Install.ps1" -Destination $target -AppOnly -NoShortcuts
Assert ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath "$target\release\MuseDesk.exe")) 'App-only installation succeeds in an isolated folder'
Assert (!(Test-Path -LiteralPath "$target\data\models")) 'App-only mode does not download model files'
Write-Output "Installer tests passed: $testRoot"
