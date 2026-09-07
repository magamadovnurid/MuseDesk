param([Parameter(Mandatory=$true)][string]$Address, [int]$Port = 11437, [switch]$Reconfigure)
$ErrorActionPreference='Stop'
$private=Join-Path $PSScriptRoot 'private'
if((Test-Path -LiteralPath "$private\server.json") -and !$Reconfigure) { Write-Output 'Muse connection is already configured. Existing keys preserved.'; exit 0 }
$interface=Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq $Address | Select-Object -First 1
if(!$interface) { throw 'The selected address is not assigned to this computer.' }
if($Address -notmatch '^100\.(6[4-9]|[7-9]\d|1[01]\d|12[0-7])\.') { throw 'Use the Tailscale IPv4 address of this computer.' }
if(!(Test-Path -LiteralPath $private)) {
  New-Item -ItemType Directory -Path $private -Force | Out-Null
  $acl=Get-Acl -LiteralPath $private
  $acl.SetAccessRuleProtection($true,$false)
  $identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name
  $rule=New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
  $acl.AddAccessRule($rule)
  Set-Acl -LiteralPath $private -AclObject $acl
}
$rng=[Security.Cryptography.RandomNumberGenerator]::Create()
$bytes=New-Object byte[] 32
$rng.GetBytes($bytes)
$token=([BitConverter]::ToString($bytes)).Replace('-','').ToLowerInvariant()
$rng.GetBytes($bytes)
$passphrase=[Convert]::ToBase64String($bytes)
$rng.Dispose()
$cert=New-SelfSignedCertificate -Subject 'CN=Muse Desk Wi-Fi' -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(2) -TextExtension @('2.5.29.17={text}IPAddress='+$Address)
Export-PfxCertificate -Cert $cert -FilePath "$private\server.pfx" -Password (ConvertTo-SecureString $passphrase -AsPlainText -Force) -CryptoAlgorithmOption AES256_SHA256 | Out-Null
$sha=[Security.Cryptography.SHA256]::Create()
$fingerprint=([BitConverter]::ToString($sha.ComputeHash($cert.RawData))).Replace('-','').ToLowerInvariant()
$sha.Dispose()
$config=@{host='127.0.0.1';port=$Port;subnet='127.0.0.1/32';tailnetAddress=$Address;token=$token;passphrase=$passphrase}
$profile=@{version=1;url="https://${Address}:$Port";token=$token;certificateSHA256=$fingerprint}
$config | ConvertTo-Json | Set-Content -LiteralPath "$private\server.json" -Encoding UTF8
$profile | ConvertTo-Json | Set-Content -LiteralPath "$private\MuseDesk.museconnection" -Encoding UTF8
Write-Output "Prepared secure connection to https://${Address}:$Port"
Write-Output "Private pairing file: $private\MuseDesk.museconnection"
Write-Output 'Keep the pairing file private; import it on your Mac. Do not publish it.'
