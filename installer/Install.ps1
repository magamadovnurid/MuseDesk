param([string]$Destination, [switch]$Probe, [switch]$AppOnly, [switch]$LibraryOnly, [switch]$NoShortcuts, [string]$CancelFile)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
. (Join-Path $PSScriptRoot 'Components.ps1')
$catalog=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'catalog.json') -Raw | ConvertFrom-Json
function Send-Status([string]$stage,[string]$message,[int]$percent=-1) {
    $line=@{stage=$stage;message=$message;percent=$percent;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Compress
    [Console]::WriteLine($line)
    if($script:logPath){Add-Content -LiteralPath $script:logPath -Value $line -Encoding UTF8}
}
function Check-Cancel {if($CancelFile -and (Test-Path -LiteralPath $CancelFile)){throw 'Установка остановлена. Повторный запуск продолжит загрузку.'}}
function Select-Profile([double]$RamGiB,[double]$GpuMiB,[double]$Compute,[double]$FreeGiB,[bool]$Platform) {
    if(!$Platform){return 'unsupported'}
    if($RamGiB -lt 31 -or $GpuMiB -lt 23000 -or $Compute -lt 7.5 -or $FreeGiB -lt 40){return 'app-only'}
    if($GpuMiB -ge 31000){return 'glimmer-q4-f16'}
    return 'glimmer-q4-q8'
}
function Get-Hardware([string]$Target) {
    $ram=0; $gpu=0; $compute=0; $gpuName='Не подтверждена совместимая NVIDIA GPU'
    try{$ram=[math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory/1GB,1)}catch{}
    $smi=Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue
    if(!$smi){$candidate=Join-Path $env:WINDIR 'System32\nvidia-smi.exe';if(Test-Path -LiteralPath $candidate){$smi=[pscustomobject]@{Source=$candidate}}}
    if($smi){try{foreach($line in (& $smi.Source --query-gpu=name,memory.total,compute_cap --format=csv,noheader,nounits 2>$null)){
        $parts=$line.Split(','); $mem=[double]::Parse($parts[1].Trim(),[Globalization.CultureInfo]::InvariantCulture);$cap=[double]::Parse($parts[2].Trim(),[Globalization.CultureInfo]::InvariantCulture)
        if($mem -gt $gpu -and $cap -ge 7.5){$gpu=$mem;$compute=$cap;$gpuName=$parts[0].Trim()}
    }}catch{}}
    $free=0;try{$free=[math]::Round(([IO.DriveInfo]::new([IO.Path]::GetPathRoot($Target))).AvailableFreeSpace/1GB,1)}catch{}
    $build=[Environment]::OSVersion.Version.Build
    $cpuArchitecture=$env:PROCESSOR_ARCHITECTURE
    $framework=0;try{$framework=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release}catch{}
    $platform=[Environment]::Is64BitOperatingSystem -and $cpuArchitecture -eq 'AMD64' -and $build -ge 19041 -and $framework -ge 528040
    $allocated=0
    foreach($spec in @($catalog.engine)+@($catalog.files)){
        $cached=Join-Path $Target 'downloads\ollama.zip'
        if($spec -ne $catalog.engine){$cached=Join-Path $Target ('data\models\blobs\sha256-'+$spec.sha256)}
        foreach($candidate in @($cached,($cached+'.part'))){if(Test-Path -LiteralPath $candidate){$allocated+=[math]::Min((Get-Item -LiteralPath $candidate).Length,$spec.bytes)}}
    }
    $profile=Select-Profile $ram $gpu $compute ($free+$allocated/1GB) $platform
    return @{ramGiB=$ram;gpuMiB=$gpu;gpu=$gpuName;compute=$compute;freeGiB=$free;profile=$profile;windowsBuild=$build;frameworkRelease=$framework;platform=$platform}
}
function Get-VerifiedFile($Spec,[string]$Path) {
    Check-Cancel
    if(Test-Path -LiteralPath $Path){
        Send-Status 'verify' ('Проверяю '+[IO.Path]::GetFileName($Path))
        if((Get-Item -LiteralPath $Path).Length -eq $Spec.bytes -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Spec.sha256){return}
        Remove-Item -LiteralPath $Path -Force
        Send-Status 'repair' 'Повреждённый файл будет загружен заново'
    }
    $part=$Path+'.part';New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($Path)) -Force | Out-Null
    $curl=Join-Path $env:WINDIR 'System32\curl.exe';if(!(Test-Path -LiteralPath $curl)){throw 'Не найден curl.exe. Требуется актуальная Windows 10/11.'}
    if(Test-Path -LiteralPath $part){if((Get-Item -LiteralPath $part).Length -gt $Spec.bytes){Remove-Item -LiteralPath $part -Force}}
    if(!(Test-Path -LiteralPath $part) -or (Get-Item -LiteralPath $part).Length -lt $Spec.bytes){
        $info=[Diagnostics.ProcessStartInfo]::new($curl)
        $info.Arguments='--fail --location --proto =https --proto-redir =https --retry 3 --connect-timeout 30 --speed-limit 1024 --speed-time 120 --silent --show-error --continue-at - --output "'+$part+'" "'+$Spec.url+'"'
        $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
        $info.RedirectStandardError=$true
        $process=[Diagnostics.Process]::Start($info);$errors=$process.StandardError.ReadToEndAsync()
        try{while(!$process.WaitForExit(500)){
            Check-Cancel;$received=0;if(Test-Path -LiteralPath $part){$received=(Get-Item -LiteralPath $part).Length}
            Send-Status 'download' ('Загрузка: {0:N2} / {1:N2} ГБ' -f ($received/1GB),($Spec.bytes/1GB)) ([math]::Min(99,[int](100*$received/$Spec.bytes)))
        };if($process.ExitCode -ne 0){throw ('Загрузка прервана: '+$errors.Result)}}
        finally{if(!$process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
    }
    Check-Cancel;Send-Status 'verify' 'Проверка SHA-256. Это может занять несколько минут.'
    if((Get-Item -LiteralPath $part).Length -ne $Spec.bytes -or (Get-FileHash -LiteralPath $part -Algorithm SHA256).Hash -ne $Spec.sha256){Remove-Item -LiteralPath $part -Force;throw 'Контрольная сумма не совпала. Нажмите «Повторить», чтобы скачать файл заново.'}
    Move-Item -LiteralPath $part -Destination $Path
}
function Write-Blob([string]$Text,[string]$Directory) {
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes($Text);$sha=[Security.Cryptography.SHA256]::Create()
    try{$hash=([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
    [IO.File]::WriteAllBytes((Join-Path $Directory ('sha256-'+$hash)),$bytes)
    return @{digest='sha256:'+$hash;size=$bytes.Length}
}
function Expand-SafeZip([string]$Archive,[string]$Root) {
    Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
    $full=[IO.Path]::GetFullPath($Root).TrimEnd('\')+'\';New-Item -ItemType Directory -Path $Root -Force | Out-Null
    $zip=[IO.Compression.ZipFile]::OpenRead($Archive)
    try{foreach($entry in $zip.Entries){Check-Cancel;$target=[IO.Path]::GetFullPath((Join-Path $Root $entry.FullName))
        if(!$target.StartsWith($full,[StringComparison]::OrdinalIgnoreCase)){throw 'Недопустимый путь в архиве.'}
        if(!$entry.Name){New-Item -ItemType Directory -Path $target -Force | Out-Null;continue}
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$true)
    }}finally{$zip.Dispose()}
}
if($LibraryOnly){return}
try{
    if(!$Destination){$Destination=Join-Path $env:LOCALAPPDATA 'Programs\Muse Desk'}
    $Destination=[IO.Path]::GetFullPath($Destination)
    $hardware=Get-Hardware $Destination
    if($Probe){[Console]::WriteLine(($hardware|ConvertTo-Json -Compress));exit 0}
    if(!$hardware.platform){throw 'Требуется Windows 10 (2004+) или Windows 11, x64. ARM64 пока не поддерживается.'}
    if(!$AppOnly -and $hardware.profile -eq 'app-only'){throw 'Автоматическая установка Glimmer не рекомендована: нужны NVIDIA 24 ГБ VRAM, 32 ГБ RAM, Compute Capability 7.5+ и 40 ГБ свободного места.'}
    if($hardware.freeGiB -lt 1){throw 'Недостаточно места для приложения.'}
    if(Test-Path -LiteralPath $Destination){
        if(!(Test-Path -LiteralPath (Join-Path $Destination 'musedesk-install.json')) -and @(Get-ChildItem -LiteralPath $Destination -Force).Count -gt 0){throw 'Выбранная папка не пуста и не принадлежит установщику Muse Desk. Выберите другую.'}
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $script:logPath=Join-Path $Destination 'installation.log'
    @{schema=1;status='installing';profile=$hardware.profile}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Destination 'musedesk-install.json') -Encoding UTF8
    Install-SystemComponents $hardware $Destination
    Send-Status 'app' 'Устанавливаю Muse Desk'
    Expand-SafeZip (Join-Path $PSScriptRoot 'application.zip') $Destination
    if(!$AppOnly){
        Check-EngineRelease $catalog.engine
        $cache=Join-Path $Destination 'downloads';$archive=Join-Path $cache 'ollama.zip'
        Get-VerifiedFile $catalog.engine $archive
        Send-Status 'engine' 'Распаковываю локальный движок'
        Expand-SafeZip $archive (Join-Path $Destination ('runtime\ollama\v'+$catalog.engine.version))
        $blobs=Join-Path $Destination 'data\models\blobs';New-Item -ItemType Directory -Path $blobs -Force | Out-Null
        $textModel=$catalog.files[0];$projector=$catalog.files[1];if($hardware.profile -eq 'glimmer-q4-f16'){$projector=$catalog.files[2]}
        foreach($file in @($textModel,$projector)){Send-Status 'model' $file.file;Get-VerifiedFile $file (Join-Path $blobs ('sha256-'+$file.sha256))}
        Send-Status 'register' 'Подключаю модель к Muse Desk'
        $config=Write-Blob '{"model_format":"gguf","model_family":"muse-glimmer","model_families":["muse-glimmer"],"model_type":"27.9B","file_type":"Q4_K_M","renderer":"glimmer","parser":"glimmer","requires":"0.32.8","architecture":"amd64","os":"linux"}' $blobs
        $parameters=Write-Blob '{"temperature":1,"top_k":64,"top_p":0.95,"num_ctx":8192}' $blobs
        $manifest=@{schemaVersion=2;mediaType='application/vnd.docker.distribution.manifest.v2+json';config=@{mediaType='application/vnd.docker.container.image.v1+json';digest=$config.digest;size=$config.size};layers=@(
            @{mediaType='application/vnd.ollama.image.model';digest='sha256:'+$textModel.sha256;size=$textModel.bytes},
            @{mediaType='application/vnd.ollama.image.projector';digest='sha256:'+$projector.sha256;size=$projector.bytes},
            @{mediaType='application/vnd.ollama.image.params';digest=$parameters.digest;size=$parameters.size})}
        $manifestPath=Join-Path $Destination 'data\models\manifests\registry.ollama.ai\acc100\muse-glimmer-heretic\latest'
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($manifestPath)) -Force | Out-Null
        [IO.File]::WriteAllText($manifestPath,($manifest|ConvertTo-Json -Depth 8 -Compress),[Text.UTF8Encoding]::new($false))
    }
    Check-Cancel
    if(!$NoShortcuts){
        $shell=New-Object -ComObject WScript.Shell
        foreach($folder in @([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('Programs'))){
            $shortcut=$shell.CreateShortcut((Join-Path $folder 'Muse Desk.lnk'));$shortcut.TargetPath=Join-Path $Destination 'release\MuseDesk.exe';$shortcut.WorkingDirectory=$Destination;$shortcut.IconLocation=$shortcut.TargetPath+',0';$shortcut.Save()
        }
    }
    @{schema=1;status='complete';profile=$(if($AppOnly){'app-only'}else{$hardware.profile});source=$catalog.source;revision=$catalog.revision;installedUtc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $Destination 'musedesk-install.json') -Encoding UTF8
    Send-Status 'complete' $(if($AppOnly){'Muse Desk установлен. Модель не загружена.'}else{'Muse Desk и Glimmer установлены. Модель загрузится при первом запуске.'}) 100
    exit 0
}catch{Send-Status 'error' $_.Exception.Message;exit 1}
