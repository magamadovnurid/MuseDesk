param([switch]$SkipModel)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not [Environment]::Is64BitOperatingSystem) { throw 'Muse Desk requires Windows x64.' }
$lock = Get-Content -LiteralPath (Join-Path $root 'runtime.lock.json') -Raw | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'build-windows.ps1')
$runtime = Join-Path $root ('runtime\ollama\v' + $lock.ollama.version)
$exe = Join-Path $runtime 'ollama.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    $downloads = Join-Path $root '.build\downloads'
    New-Item -ItemType Directory -Path $downloads -Force | Out-Null
    $zip = Join-Path $downloads ('ollama-' + $lock.ollama.version + '-windows-amd64.zip')
    if (-not (Test-Path -LiteralPath $zip)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Write-Host 'Downloading the pinned Ollama engine (large download)...'
        Invoke-WebRequest -UseBasicParsing -Uri $lock.ollama.url -OutFile ($zip + '.part')
        Move-Item -LiteralPath ($zip + '.part') -Destination $zip -Force
    }
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $lock.ollama.sha256) {
        throw "Engine checksum mismatch. Remove this archive and retry: $zip"
    }
    New-Item -ItemType Directory -Path $runtime -Force | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $runtime -Force
}
if (-not (Test-Path -LiteralPath $exe)) { throw 'Ollama executable missing after extraction.' }
if ($SkipModel) { Write-Host 'Engine and application ready. Model download skipped.'; return }
$models = Join-Path $root 'data\models'
$manifest = Join-Path $models $lock.model.manifestPath
New-Item -ItemType Directory -Path $models -Force | Out-Null
# A separate loopback-only server does not change another Ollama installation.
$probe = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
$probe.Start()
$port = $probe.LocalEndpoint.Port
$probe.Stop()
$hostAddress = '127.0.0.1:' + $port
$oldHost = $env:OLLAMA_HOST
$oldModels = $env:OLLAMA_MODELS
$server = $null
try {
    $env:OLLAMA_HOST = $hostAddress
    $env:OLLAMA_MODELS = $models
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $exe
    $start.Arguments = 'serve'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.EnvironmentVariables['OLLAMA_NO_CLOUD'] = 'true'
    $server = [Diagnostics.Process]::Start($start)
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        if ($server.HasExited) { throw 'The temporary Ollama server exited.' }
        try { $null = Invoke-RestMethod -Uri ('http://' + $hostAddress + '/api/version') -TimeoutSec 2; $ready = $true; break } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'The temporary Ollama server did not become ready.' }
    Write-Host 'Downloading/verifying Muse model (~20.8 GB); existing blobs are reused.'
    & $exe pull $lock.model.name
    if ($LASTEXITCODE -ne 0) { throw 'Model download failed. Rerun setup to resume.' }
    if (-not (Test-Path -LiteralPath $manifest)) { throw 'Model manifest missing after download.' }
    if ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -ne $lock.model.manifestSha256) {
        throw 'The upstream model tag changed. The download is not the recorded model version. Review runtime.lock.json before accepting an update.'
    }
} finally {
    if ($server -and -not $server.HasExited) { $server.Kill(); $server.WaitForExit(5000) | Out-Null }
    $env:OLLAMA_HOST = $oldHost
    $env:OLLAMA_MODELS = $oldModels
}
Write-Host ('Ready. Launch: ' + (Join-Path $root 'release\MuseDesk.exe'))
