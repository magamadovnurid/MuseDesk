param([string]$ApplicationRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$lock = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'runtime.lock.json') -Raw | ConvertFrom-Json
$spec = $lock.additionalModels | Where-Object { $_.name -eq 'huihui_ai/Qwen3.8-abliterated:27b' } | Select-Object -First 1
if (-not $spec) { throw 'Qwen entry missing in runtime.lock.json.' }
$model = $spec.name
$runtime = Join-Path $ApplicationRoot 'runtime\ollama'
$ollama = Get-ChildItem -LiteralPath $runtime -Filter ollama.exe -Recurse | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $ollama) { throw 'Ollama runtime missing. Run setup-windows.ps1 first.' }
$null = Invoke-RestMethod 'http://127.0.0.1:11436/api/version' -TimeoutSec 5
$oldHost = $env:OLLAMA_HOST
try {
    $env:OLLAMA_HOST = '127.0.0.1:11436'
    & $ollama.FullName pull $model
    if ($LASTEXITCODE -ne 0) { throw 'Qwen download failed. Run this script again to resume.' }
} finally { $env:OLLAMA_HOST = $oldHost }
$tags = Invoke-RestMethod 'http://127.0.0.1:11436/api/tags' -TimeoutSec 10
$installed = $tags.models | Where-Object { $_.name -eq $model } | Select-Object -First 1
if (-not $installed -or $installed.digest -ne $spec.manifestSha256) {
    throw 'Downloaded model differs from runtime.lock.json. Review the publisher update before accepting it.'
}
Write-Host ('Installed and digest verified: ' + $model + '. Select it in the Muse Desk model menu.')
