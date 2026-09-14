$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'

New-Item -ItemType Directory -Force -Path 'data/tessdata' | Out-Null
$downloads = @{
    'data/tessdata/eng.traineddata' = 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/eng.traineddata'
    'data/tessdata/LICENSE.txt' = 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main/LICENSE'
    'data/wordnet.zip' = 'https://raw.githubusercontent.com/nltk/nltk_data/gh-pages/packages/corpora/wordnet.zip'
}
foreach ($destination in $downloads.Keys) {
    if (-not (Test-Path -LiteralPath $destination)) {
        Write-Host "Downloading $destination"
        Invoke-WebRequest -Uri $downloads[$destination] -OutFile "$destination.download"
        Move-Item -LiteralPath "$destination.download" -Destination $destination
    }
}
$expectedHashes = @{
    'data/tessdata/eng.traineddata' = '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2'
    'data/wordnet.zip' = 'CBDA5EA6EEF7F36A97A43D4A75F85E07FCCBB4F23657D27B4CCBC93E2646AB59'
}
foreach ($dataFile in $expectedHashes.Keys) {
    if ((Get-FileHash -LiteralPath $dataFile -Algorithm SHA256).Hash -ne $expectedHashes[$dataFile]) {
        throw "Unexpected data version or incomplete download: $dataFile. Verify its upstream source before updating the recorded hash."
    }
}
dotnet build 'ScreenEnglish/ScreenEnglish.csproj' -p:Platform=x64 -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Check the .NET 8 SDK and Windows SDK installation.' }
Write-Host 'Setup complete. Run .\run.ps1 to start the tray app.'
