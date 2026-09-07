function Get-FrameworkTarget([int]$Build,[int]$Release) {
    if($Build -ge 19044 -and $Release -lt 533320){return '4.8.1'}
    return $null
}
function Assert-MicrosoftSignature($Signature) {
    if($Signature.Status -ne 'Valid' -or $Signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {throw 'Не удалось подтвердить подпись Microsoft. Компонент не будет запущен.'}
}
function Assert-ComponentExit([int]$Code) {
    if($Code -eq 3010 -or $Code -eq 1641){return 'restart'}
    if($Code -ne 0){throw "Установка компонента завершилась с кодом $Code. Повторите установку."}
    return 'complete'
}
function Install-SystemComponents($Hardware,[string]$Root) {
    Send-Status 'components' 'Проверяю системные компоненты Windows'
    $target=Get-FrameworkTarget $Hardware.windowsBuild $Hardware.frameworkRelease
    if(!$target){Send-Status 'components' '.NET Framework уже установлен и совместим.';return}
    # This official permalink serves the current 4.8.1 runtime. Always validate Authenticode before elevation.
    $file=Join-Path $Root 'downloads\net481.exe';New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($file))|Out-Null
    Send-Status 'components' 'Скачиваю .NET Framework 4.8.1 с сайта Microsoft'
    $curl=Join-Path $env:WINDIR 'System32\curl.exe'
    & $curl --fail --location --proto '=https' --proto-redir '=https' --retry 3 --connect-timeout 30 --max-time 600 --silent --show-error --output ($file+'.part') 'https://go.microsoft.com/fwlink/?linkid=2203305'
    if($LASTEXITCODE -ne 0){throw 'Не удалось скачать .NET Framework. Проверьте подключение и повторите установку.'}
    Check-Cancel
    Assert-MicrosoftSignature (Get-AuthenticodeSignature -LiteralPath ($file+'.part'))
    Move-Item -LiteralPath ($file+'.part') -Destination $file -Force
    Send-Status 'components' 'Устанавливаю .NET Framework. Windows запросит разрешение администратора.'
    $process=Start-Process -FilePath $file -ArgumentList @('/q','/norestart') -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    $result=Assert-ComponentExit $process.ExitCode
    if($result -eq 'restart'){Send-Status 'restart' 'Для завершения установки компонента перезагрузите Windows и снова запустите этот установщик. Автоматической перезагрузки не будет.';exit 3010}
    $actual=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release
    if($actual -lt 533320){throw 'Проверка .NET Framework после установки не пройдена.'}
}
function Check-EngineRelease($Spec) {
    Send-Status 'components' 'Проверяю последний выпуск движка на GitHub'
    try {
        [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
        $release=Invoke-RestMethod 'https://api.github.com/repos/ollama/ollama/releases/latest' -TimeoutSec 10 -Headers @{'User-Agent'='MuseDesk-Setup';Accept='application/vnd.github+json'}
        if($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^v\d+\.\d+\.\d+$'){throw 'Invalid release metadata'}
        $latest=$release.tag_name.Substring(1)
        if($latest -eq $Spec.version){
            $asset=$release.assets|Where-Object name -eq ([IO.Path]::GetFileName($Spec.url))|Select-Object -First 1
            if(!$asset -or $asset.size -ne $Spec.bytes -or $asset.digest -ne ('sha256:'+$Spec.sha256)){throw 'Release metadata differs from catalog'}
            Send-Status 'components' ('Устанавливаю актуальный проверенный движок '+$latest)
        }else{Send-Status 'components' ('Доступен движок '+$latest+'. Для этой версии приложения устанавливаю проверенный '+$Spec.version+'.')}
    }catch{Send-Status 'components' ('Каталог обновлений недоступен. Использую проверенный движок '+$Spec.version+'.')}
}
