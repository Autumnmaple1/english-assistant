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

$version = (Get-Item (Join-Path $app 'ScreenEnglish.exe')).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($version)) { $version = '1.0.0' }
$version = $version.Split('+')[0].Trim()

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $dist "ScreenEnglish-$version-portable-win-x64.zip"
Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
[System.IO.Compression.ZipFile]::CreateFromDirectory($app, $archive, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Portable folder : dist\ScreenEnglish ($([math]::Round((Get-ChildItem $app -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)) MB)"
Write-Host "Portable archive: dist\$(Split-Path $archive -Leaf)"

$candidates = @((Join-Path $PSScriptRoot '.tools/InnoSetup/ISCC.exe'))
if (${env:ProgramFiles(x86)}) { $candidates += (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe') }
if ($env:ProgramFiles) { $candidates += (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe') }
$iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Host 'Inno Setup was not found, so no Setup.exe was produced. Install Inno Setup 6, then run pack.ps1 again.'
    return
}
& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer/ScreenEnglish.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup could not compile the installer.' }
Write-Host "Installer       : dist\ScreenEnglish-Setup-$version.exe"
