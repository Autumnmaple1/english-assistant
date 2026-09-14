$ErrorActionPreference = 'Stop'
$executablePath = Join-Path $PSScriptRoot 'ScreenEnglish/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/ScreenEnglish.exe'
if (-not (Test-Path -LiteralPath $executablePath)) {
    & (Join-Path $PSScriptRoot 'setup.ps1')
}
if (-not (Test-Path -LiteralPath $executablePath)) { throw 'Build output not found. Run setup.ps1 first.' }
Start-Process -FilePath $executablePath -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
