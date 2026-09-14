$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'

$dist = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'dist'))
$app = [System.IO.Path]::GetFullPath((Join-Path $dist 'ScreenEnglish'))
if ([System.IO.Path]::GetDirectoryName($app) -ne $dist) { throw 'Refusing to clean a directory outside dist.' }

New-Item -ItemType Directory -Force -Path $dist | Out-Null
Remove-Item -LiteralPath $app -Recurse -Force -ErrorAction SilentlyContinue

Write-Host 'Publishing a self-contained x64 build (no .NET or Windows App SDK install required)'
dotnet publish 'ScreenEnglish/ScreenEnglish.csproj' -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $app -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Publish failed. See the dotnet output above.' }
foreach ($required in 'ScreenEnglish.exe', 'data/tessdata/eng.traineddata', 'data/wordnet.zip') {
    if (-not (Test-Path -LiteralPath (Join-Path $app $required))) { throw "The published app is missing $required." }
}

# Trim the published payload. This app is x64-only, uses no Windows App SDK AI/ML/Widgets component, and its own
# interface strings are English, so the 32-bit native libraries, the NPU/ML payload and the non-English
# localisation folders are dead weight. Keep en-us (fallback) and zh-CN (this machine).
$before = (Get-ChildItem $app -Recurse -File | Measure-Object Length -Sum).Sum
Get-ChildItem $app -Directory | Where-Object { $_.Name -match '^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$' -and $_.Name -notin @('en-us', 'zh-CN') } | Remove-Item -Recurse -Force
foreach ($item in 'x86', 'NpuDetect', 'onnxruntime.dll', 'onnxruntime_providers_shared.dll', 'DirectML.dll', 'Microsoft.ML.OnnxRuntime.dll') {
    Remove-Item -LiteralPath (Join-Path $app $item) -Recurse -Force -ErrorAction SilentlyContinue
}
Get-ChildItem $app -File | Where-Object { $_.Name -like 'Microsoft.Windows.AI.*' -or $_.Name -like 'Microsoft.Windows.Widgets.*' } | Remove-Item -Force
$after = (Get-ChildItem $app -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host "Trimmed         : $([math]::Round(($before - $after)/1MB,1)) MB of unused files removed; app folder is now $([math]::Round($after/1MB,1)) MB"
# The XAML compiler writes the app's compiled resources into the build output (ScreenEnglish.pri carries App.xaml).
# dotnet publish does not carry them across, and without them every window loses its styles at run time.
$build = Join-Path $PSScriptRoot 'ScreenEnglish/bin/x64/Release/net8.0-windows10.0.19041.0/win-x64'
Get-ChildItem -LiteralPath $build -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'ScreenEnglish.pri' -or $_.Extension -eq '.xbf' } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $app $_.Name) -Force }
$version = (Get-Item (Join-Path $app 'ScreenEnglish.exe')).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($version)) { $version = '1.0.0' }
$version = $version.Split('+')[0].Trim()

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $dist "ScreenEnglish-$version-portable-win-x64.zip"
Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
[System.IO.Compression.ZipFile]::CreateFromDirectory($app, $archive, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Portable folder : dist\ScreenEnglish ($([math]::Round((Get-ChildItem $app -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)) MB)"
Write-Host "Portable archive: dist\$(Split-Path $archive -Leaf)"

function WriteSums {
    $artifacts = @(Get-ChildItem -Path (Join-Path $dist 'ScreenEnglish-*.zip'), (Join-Path $dist 'ScreenEnglish-Setup-*.exe') -File -ErrorAction SilentlyContinue | Sort-Object Name)
    if (-not $artifacts) { return }
    $lines = foreach ($artifact in $artifacts) { (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $artifact.Name }
    [System.IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $lines)
    Write-Host "Checksums       : dist\SHA256SUMS.txt"
}
$candidates = @((Join-Path $PSScriptRoot '.tools/InnoSetup/ISCC.exe'))
if (${env:ProgramFiles(x86)}) { $candidates += (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe') }
if ($env:ProgramFiles) { $candidates += (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe') }
$iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Host 'Inno Setup was not found, so no Setup.exe was produced. Install Inno Setup 6, then run pack.ps1 again.'
    WriteSums
    return
}
& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer/ScreenEnglish.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup could not compile the installer.' }
Write-Host "Installer       : dist\ScreenEnglish-Setup-$version.exe"
WriteSums
