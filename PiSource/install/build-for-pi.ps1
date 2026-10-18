param(
    [ValidateSet('framework-dependent', 'self-contained')]
    [string]$BuildType = 'self-contained'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

Push-Location $repoRoot
try {
    dotnet restore 'PiSource/PiSource.sln'
    dotnet build 'PiSource/PiSource.sln' --configuration Release --no-restore

    $publishArgs = @(
        'publish',
        'PiSource/TerrariumController/TerrariumController.csproj',
        '--configuration', 'Release',
        '--runtime', 'linux-arm64',
        '--output', 'publish'
    )

    if ($BuildType -eq 'self-contained') {
        $publishArgs += @('--self-contained', 'true')
    }
    else {
        $publishArgs += @('--self-contained', 'false')
    }

    dotnet @publishArgs
}
finally {
    Pop-Location
}