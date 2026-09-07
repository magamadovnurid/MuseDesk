param([switch]$Live)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$speech = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll'
$output = Join-Path $root ('test-output\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$references = @('System','System.Core','System.Drawing','System.IO.Compression','System.IO.Compression.FileSystem','System.Net.Http','System.Web.Extensions','System.Windows.Forms','System.Xml','System.Xml.Linq') | ForEach-Object { '/reference:' + $framework + '\' + $_ + '.dll' }
& "$framework\csc.exe" /nologo /target:exe /platform:x64 /main:MuseDeskNative.TestRunner "/out:$output\MuseDesk.Tests.exe" $references "/reference:$speech" "$root\windows-native\MuseDesk.cs" "$root\windows-native\Support.cs" "$root\windows-native\Design.cs" "$root\windows-native\Brand.cs" "$root\windows-native\WorkspaceDetails.cs" "$root\windows-native\ModernScroll.cs" "$root\windows-native\StreamingView.cs" "$root\windows-native\Permissions.cs" "$root\windows-native\AgentWorkflow.cs" "$root\windows-native\TaskSummary.cs" "$root\windows-native\TextEncoding.cs" "$root\windows-native\ContextBudget.cs" "$root\windows-native\ProjectControls.cs" "$root\windows-native\Preview.cs" "$root\windows-native\Tests.cs" "$root\windows-native\IconAssets.cs" "/resource:$root\windows-native\assets\icons\icons.zip,MuseDesk.Icons.zip"
if ($LASTEXITCODE -ne 0) { throw 'Tests could not compile.' }
Copy-Item -LiteralPath "$root\windows-native\MuseDesk.exe.config" -Destination "$output\MuseDesk.Tests.exe.config"
if ($Live) { & "$output\MuseDesk.Tests.exe" $output --live | Tee-Object -FilePath "$output\results.txt" }
else { & "$output\MuseDesk.Tests.exe" $output | Tee-Object -FilePath "$output\results.txt" }
if ($LASTEXITCODE -ne 0) { throw "Tests failed. See $output" }
Write-Output "Test artifacts: $output"
