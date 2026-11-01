param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

Push-Location $PSScriptRoot
try {
    dotnet restore .\Ferah.slnx
    if ($LASTEXITCODE -ne 0) { throw 'Paket geri yukleme basarisiz oldu.' }

    dotnet build .\Ferah.slnx -c $Configuration --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Derleme basarisiz oldu.' }

    dotnet run --project .\tests\Ferah.Tests\Ferah.Tests.csproj -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Testler basarisiz oldu.' }

    Write-Host 'Ferah basariyla derlendi ve tum testler gecti.' -ForegroundColor Green
}
finally {
    Pop-Location
}
