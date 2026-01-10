#!/usr/bin/env pwsh
# Build and package Terrarium Controller for Raspberry Pi deployment
# Run this on your Windows dev machine before deploying to Pi

param(
    [ValidateSet('framework-dependent', 'self-contained', 'both')]
    [string]$BuildType = 'framework-dependent',
    
    [switch]$NoClean
)

$ErrorActionPreference = "Stop"

Write-Host "=== Terrarium Controller Build Script ===" -ForegroundColor Cyan
Write-Host ""

# Determine paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$piSourceDir = Split-Path -Parent $scriptDir
$projectDir = Join-Path $piSourceDir "TerrariumController"
$projectFile = Join-Path $projectDir "TerrariumController.csproj"
$outputDir = Join-Path $scriptDir "app"

# Verify project exists
if (-not (Test-Path $projectFile)) {
    Write-Host "ERROR: Project file not found: $projectFile" -ForegroundColor Red
    exit 1
}

Write-Host "Project: $projectFile" -ForegroundColor Gray
Write-Host "Output:  $outputDir" -ForegroundColor Gray
Write-Host ""

# Clean output directory by default (unless -NoClean specified)
if (-not $NoClean -and (Test-Path $outputDir)) {
    Write-Host "Cleaning output directory..." -ForegroundColor Yellow
    Remove-Item -Path $outputDir -Recurse -Force
}

# Create output directory
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

# Build function
function Build-App {
    param(
        [string]$Type,
        [string]$OutputPath
    )
    
    Write-Host "Building $Type app..." -ForegroundColor Green
    
    $publishArgs = @(
        'publish',
        $projectFile,
        '-c', 'Release',
        '-o', $OutputPath,
        '--nologo'
    )
    
    if ($Type -eq 'self-contained') {
        $publishArgs += @(
            '-r', 'linux-arm64',
            '--self-contained', 'true',
            '-p:PublishSingleFile=true',
            '-p:DebugType=None',
            '-p:DebugSymbols=false'
        )
    } else {
        $publishArgs += @(
            '-r', 'linux-arm64',
            '--no-self-contained'
        )
    }
    
    Write-Host "dotnet $($publishArgs -join ' ')" -ForegroundColor Gray
    & dotnet @publishArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# Build based on selected type
switch ($BuildType) {
    'framework-dependent' {
        Build-App -Type 'framework-dependent' -OutputPath $outputDir
    }
    'self-contained' {
        Build-App -Type 'self-contained' -OutputPath $outputDir
    }
    'both' {
        $fdDir = Join-Path $outputDir "framework-dependent"
        $scDir = Join-Path $outputDir "self-contained"
        
        New-Item -ItemType Directory -Path $fdDir -Force | Out-Null
        New-Item -ItemType Directory -Path $scDir -Force | Out-Null
        
        Build-App -Type 'framework-dependent' -OutputPath $fdDir
        Build-App -Type 'self-contained' -OutputPath $scDir
        
        Write-Host ""
        Write-Host "Both builds completed. Choose which to deploy:" -ForegroundColor Cyan
        Write-Host "  Framework-dependent: $fdDir" -ForegroundColor Gray
        Write-Host "  Self-contained:      $scDir" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Output directory: $outputDir" -ForegroundColor Cyan

# Show deployment instructions
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Copy this folder to your Raspberry Pi:" -ForegroundColor White
Write-Host "   scp -r '$scriptDir' pi@<pi-ip>:~/terrarium-install" -ForegroundColor Gray
Write-Host ""
Write-Host "2. On the Pi, run the setup:" -ForegroundColor White
Write-Host "   cd ~/terrarium-install" -ForegroundColor Gray
Write-Host "   sudo bash setup.sh" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Or manually deploy the app:" -ForegroundColor White
Write-Host "   sudo cp -R '$outputDir'/* /opt/terrarium/" -ForegroundColor Gray
Write-Host "   sudo chown -R terrarium:terrarium /opt/terrarium" -ForegroundColor Gray
Write-Host "   sudo systemctl restart terrarium" -ForegroundColor Gray

# Show file sizes
Write-Host ""
Write-Host "Build artifacts:" -ForegroundColor Cyan
Get-ChildItem -Path $outputDir -Recurse -File | 
    Where-Object { $_.Name -match '\.(dll|exe|so|json)$' -or $_.Name -eq 'TerrariumController' } |
    ForEach-Object {
        $size = if ($_.Length -gt 1MB) {
            "{0:N2} MB" -f ($_.Length / 1MB)
        } elseif ($_.Length -gt 1KB) {
            "{0:N2} KB" -f ($_.Length / 1KB)
        } else {
            "{0} B" -f $_.Length
        }
        Write-Host "  $($_.Name.PadRight(40)) $size" -ForegroundColor Gray
    }

$totalSize = (Get-ChildItem -Path $outputDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
$totalSizeMB = $totalSize / 1MB
Write-Host ""
Write-Host "Total size: $("{0:N2}" -f $totalSizeMB) MB" -ForegroundColor Cyan
