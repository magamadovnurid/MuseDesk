$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. "$root\installer\Install.ps1" -LibraryOnly
function Assert($Condition,$Message){if(!$Condition){throw $Message};Write-Output "PASS: $Message"}
Assert ((Get-FrameworkTarget 19044 528040) -eq '4.8.1') 'Supported Windows upgrades older Framework'
Assert ($null -eq (Get-FrameworkTarget 19041 528040)) 'Older supported Windows keeps its compatible Framework'
Assert ($null -eq (Get-FrameworkTarget 26100 533325)) 'Current Framework is not reinstalled'
Assert ((Assert-ComponentExit 3010) -eq 'restart') 'Restart-required code cannot become installation success'
Assert ((Assert-ComponentExit 0) -eq 'complete') 'Component success is recognized'
$rejected=$false;try{Assert-ComponentExit 1603}catch{$rejected=$true};Assert $rejected 'Component installation failure is propagated'
$rejected=$false;try{Assert-MicrosoftSignature ([pscustomobject]@{Status='Valid';SignerCertificate=[pscustomobject]@{Subject='CN=Other, O=Other'}})}catch{$rejected=$true};Assert $rejected 'A valid signature from another publisher is rejected'
$rejected=$false;try{Assert-MicrosoftSignature ([pscustomobject]@{Status='NotSigned';SignerCertificate=$null})}catch{$rejected=$true};Assert $rejected 'Unsigned prerequisite is never executed'
Assert-MicrosoftSignature ([pscustomobject]@{Status='Valid';SignerCertificate=[pscustomobject]@{Subject='CN=Microsoft Corporation, O=Microsoft Corporation, C=US'}})
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
$badPath=Join-Path $testRoot ($blob.digest.Replace(':','-'));Set-Content -LiteralPath $badPath -Value corrupt
[IO.File]::WriteAllText(($badPath+'.part'),'{"test":true}',[Text.UTF8Encoding]::new($false))
Get-VerifiedFile $spec $badPath
Assert ((Get-FileHash $badPath).Hash -eq $spec.sha256) 'Corrupt cache recovers from verified partial bytes without network'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$badZip=Join-Path $testRoot 'bad.zip';$zip=[IO.Compression.ZipFile]::Open($badZip,[IO.Compression.ZipArchiveMode]::Create)
try{$null=$zip.CreateEntry('../outside.txt')}finally{$zip.Dispose()}
$rejected=$false;try{Expand-SafeZip $badZip (Join-Path $testRoot 'extract')}catch{$rejected=$true};Assert $rejected 'Archive paths cannot escape the destination'
$stage=Join-Path $testRoot 'stage';New-Item -ItemType Directory -Path $stage -Force|Out-Null
Copy-Item -LiteralPath "$root\installer\Install.ps1","$root\installer\Components.ps1","$root\installer\catalog.json" -Destination $stage
Copy-Item -LiteralPath "$root\.build\application.zip" -Destination "$stage\application.zip"
[IO.File]::WriteAllText("$stage\Install.ps1",(Get-Content -LiteralPath "$stage\Install.ps1" -Raw).Replace('Install-SystemComponents $hardware $Destination',"Send-Status 'components' 'System changes mocked in isolated test'"),[Text.UTF8Encoding]::new($true))
$target=Join-Path $testRoot 'installed'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$stage\Install.ps1" -Destination $target -AppOnly -NoShortcuts
Assert ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath "$target\release\MuseDesk.exe")) 'App-only installation succeeds in an isolated folder'
Assert (!(Test-Path -LiteralPath "$target\data\models")) 'App-only mode does not download model files'
Write-Output "Installer tests passed: $testRoot"
