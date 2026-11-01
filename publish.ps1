param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$version = '1.0.0'
$output = Join-Path $PSScriptRoot "artifacts\Ferah-$version-$Runtime"

Push-Location $PSScriptRoot
try {
    & .\build.ps1 -Configuration Release
    New-Item -ItemType Directory -Path $output -Force | Out-Null

    dotnet publish .\src\Ferah.App\Ferah.App.csproj -c Release -r $Runtime `
        --self-contained true -o $output `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishTrimmed=false `
        -p:DebugSymbols=false `
        -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw 'Dagitim olusturulamadi.' }

    Copy-Item .\README.md, .\SECURITY.md, .\LICENSE, .\THIRD-PARTY-NOTICES.md -Destination $output -Force
    $hash = (Get-FileHash (Join-Path $output 'Ferah.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $output 'SHA256.txt') -Value "$hash  Ferah.exe" -Encoding Ascii

    $zip = Join-Path $PSScriptRoot "artifacts\Ferah-$version-$Runtime.zip"
    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal -Force
    Write-Host "Dagitim hazir: $output" -ForegroundColor Green
    Write-Host "Arsiv hazir: $zip" -ForegroundColor Green
}
finally {
    Pop-Location
}
