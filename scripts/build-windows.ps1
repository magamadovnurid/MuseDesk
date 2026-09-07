$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$speech = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll'
$release = Join-Path $root "release"
$build = Join-Path $root ".build"

if (-not (Test-Path -LiteralPath $compiler)) { throw ".NET Framework compiler not found: $compiler" }
if (-not (Test-Path -LiteralPath $speech)) { throw "Windows speech assembly not found: $speech" }
New-Item -ItemType Directory -Path $release -Force | Out-Null
New-Item -ItemType Directory -Path $build -Force | Out-Null
& $compiler /nologo /target:exe /reference:"$framework\System.Drawing.dll" /out:"$build\BuildIcon.exe" "$root\windows-native\BuildIcon.cs" "$root\windows-native\Brand.cs"
if ($LASTEXITCODE -ne 0) { throw 'Icon generator compilation failed.' }
& "$build\BuildIcon.exe" "$release\MuseDesk.ico"
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }

& $compiler `
    /nologo `
    /target:winexe `
    /platform:x64 `
    /optimize+ `
    /debug- `
    /out:"$release\MuseDesk.exe" `
    /win32manifest:"$root\windows-native\app.manifest" `
    /win32icon:"$release\MuseDesk.ico" `
    "/resource:$root\windows-native\assets\icons\icons.zip,MuseDesk.Icons.zip" `
    /reference:"$framework\System.dll" `
    /reference:"$framework\System.Core.dll" `
    /reference:"$framework\System.Drawing.dll" `
    /reference:"$framework\System.IO.Compression.dll" `
    /reference:"$framework\System.IO.Compression.FileSystem.dll" `
    /reference:"$framework\System.Net.Http.dll" `
    /reference:"$framework\System.Web.Extensions.dll" `
    /reference:"$framework\System.Xml.dll" `
    /reference:"$framework\System.Xml.Linq.dll" `
    /reference:"$framework\System.Windows.Forms.dll" `
    /reference:"$speech" `
    "$root\windows-native\MuseDesk.cs" `
    "$root\windows-native\Design.cs" `
    "$root\windows-native\Brand.cs" `
    "$root\windows-native\WorkspaceDetails.cs" `
    "$root\windows-native\ModernScroll.cs" `
    "$root\windows-native\StreamingView.cs" `
    "$root\windows-native\Permissions.cs" `
    "$root\windows-native\AgentWorkflow.cs" `
    "$root\windows-native\TaskSummary.cs" `
    "$root\windows-native\TextEncoding.cs" `
    "$root\windows-native\ContextBudget.cs" `
    "$root\windows-native\ProjectControls.cs" `
    "$root\windows-native\IconAssets.cs" `
    "$root\windows-native\Support.cs"

if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE" }
Copy-Item -LiteralPath "$root\windows-native\MuseDesk.exe.config" -Destination "$release\MuseDesk.exe.config" -Force
Write-Host "Built: $release\MuseDesk.exe"
