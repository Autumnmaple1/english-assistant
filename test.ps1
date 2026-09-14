$ErrorActionPreference = 'Stop'
$executablePath = Join-Path $PSScriptRoot 'ScreenEnglish/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/ScreenEnglish.exe'
$resultPath = Join-Path $PSScriptRoot 'test-output/results.json'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$testProcess = Start-Process -FilePath $executablePath -ArgumentList '--self-test' -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru
if (-not $testProcess.WaitForExit(120000)) { throw 'Self-test did not finish within two minutes.' }
if (-not (Test-Path -LiteralPath $resultPath)) { throw 'Self-test exited without a results file. Check the application runtime.' }
$results = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$results | Select-Object name,passed | Format-Table -AutoSize
if ($results.Where({ -not $_.passed }).Count -gt 0) { throw "A check failed. See $resultPath" }
