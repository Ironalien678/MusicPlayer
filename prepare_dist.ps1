<#
Build + prepare + package script (CI-friendly)
This script will:
 - build the solution in Release
 - prepare a `dist` folder containing the Release output and other Output/ClickOnce folders
 - call Inno Setup command-line compiler (`ISCC.exe`) to build the installer (if available)

Usage (PowerShell):
    .\prepare_dist.ps1 [-Configuration Release] [-ISCCPath 'C:\Path\To\ISCC.exe']
#>
param(
    [string]$Configuration = 'Release',
    [string]$ISCCPath = ''
)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionFile = Get-ChildItem -Path $scriptRoot -Filter "*.sln" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $solutionFile) { Write-Host "Solution file not found in workspace root. Ensure you're running this from the repo root." -ForegroundColor Red; exit 1 }

# Build solution
Write-Host "Building solution: $($solutionFile.FullName) (Configuration=$Configuration)" -ForegroundColor Cyan
$msbuild = "& 'C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\MSBuild\\Current\\Bin\\MSBuild.exe'"
# Try to find msbuild
$msbuildFound = Get-Command msbuild -ErrorAction SilentlyContinue
if ($msbuildFound) { $msbuild = "& msbuild" }

$buildCmd = "$msbuild `"$($solutionFile.FullName)`" /p:Configuration=$Configuration /m"
Write-Host "Running: $buildCmd"
Invoke-Expression $buildCmd

# If solution build didn't produce an exe, attempt to build the project file directly
$csproj = Get-ChildItem -Path $scriptRoot -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if ($csproj) {
    Write-Host "Found project: $($csproj.FullName)" -ForegroundColor Cyan
} else {
    Write-Host "No .csproj found in workspace." -ForegroundColor Yellow
}

# Prepare dist
$csproj = Get-ChildItem -Path $scriptRoot -Filter '*.csproj' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if ($csproj) {
    $projectFolder = Split-Path -Parent $csproj.FullName
} else {
    # fallback to common path
    $projectFolder = Join-Path $scriptRoot 'MusicPlayer'
}

$releaseFolder = Join-Path $projectFolder "bin\$Configuration"
$outputFolder = Join-Path $projectFolder 'Output'
$clickOnceFolder = Join-Path $outputFolder 'Application Files\MusicPlayer_1_0_0_0'
$distFolder = Join-Path $scriptRoot 'dist'

if (Test-Path $distFolder) { Remove-Item -Recurse -Force $distFolder }
New-Item -ItemType Directory -Path $distFolder | Out-Null

if (Test-Path $releaseFolder) {
    Write-Host "Copying from Release folder: $releaseFolder" -ForegroundColor Green
    Copy-Item -Path (Join-Path $releaseFolder '*') -Destination $distFolder -Recurse -Force
} else {
    Write-Host "Release folder not found at: $releaseFolder" -ForegroundColor Yellow
}
if (Test-Path $outputFolder) {
    Write-Host "Copying from Output folder: $outputFolder" -ForegroundColor Green
    Copy-Item -Path (Join-Path $outputFolder '*') -Destination $distFolder -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "Output folder not found at: $outputFolder" -ForegroundColor Yellow
}
if (Test-Path $clickOnceFolder) {
    Write-Host "Copying from ClickOnce folder: $clickOnceFolder" -ForegroundColor Green
    Copy-Item -Path (Join-Path $clickOnceFolder '*') -Destination $distFolder -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "ClickOnce folder not found at: $clickOnceFolder" -ForegroundColor Yellow
}

# Ensure MusicPlayer.exe is in dist root
$exeName = 'MusicPlayer.exe'
$exeInRoot = Join-Path $distFolder $exeName
if (-not (Test-Path $exeInRoot)) {
    Write-Host "Searching for $exeName inside dist..." -ForegroundColor Cyan
    $found = Get-ChildItem -Path $distFolder -Recurse -Filter $exeName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) {
        Write-Host "Found in dist: $($found.FullName). Copying to dist root..." -ForegroundColor Green
        Copy-Item -Path $found.FullName -Destination $exeInRoot -Force
    } else {
        Write-Host "Not found in dist. Searching workspace for $exeName..." -ForegroundColor Yellow
        $foundWs = Get-ChildItem -Path $scriptRoot -Recurse -Filter $exeName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($foundWs) {
            Write-Host "Found in workspace: $($foundWs.FullName). Copying to dist root..." -ForegroundColor Green
            Copy-Item -Path $foundWs.FullName -Destination $exeInRoot -Force
        } else {
            Write-Host "MusicPlayer.exe not found in workspace after build. Attempting to locate built exe in project Release folder..." -ForegroundColor Yellow
            # try to find exe by project name or any exe in release folder
            if ($csproj) {
                $projectName = [System.IO.Path]::GetFileNameWithoutExtension($csproj.FullName)
                $candidate = Join-Path $releaseFolder "$projectName.exe"
                if (Test-Path $candidate) {
                    Write-Host "Found built exe by project name: $candidate" -ForegroundColor Green
                    Copy-Item -Path $candidate -Destination $exeInRoot -Force
                } else {
                    $anyExe = Get-ChildItem -Path $releaseFolder -Filter '*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
                    if ($anyExe) {
                        Write-Host "Found an exe in release folder: $($anyExe.FullName). Copying to dist root..." -ForegroundColor Green
                        Copy-Item -Path $anyExe.FullName -Destination $exeInRoot -Force
                    } else {
                        Write-Host "No exe found in release folder. Attempting to build project directly and search again..." -ForegroundColor Yellow
                        $msbuildCmd = "msbuild `"$($csproj.FullName)`" /p:Configuration=$Configuration /m"
                        Write-Host "Running: $msbuildCmd" -ForegroundColor Cyan
                        Invoke-Expression $msbuildCmd
                        Start-Sleep -Seconds 1
                        $foundAfter = Get-ChildItem -Path $releaseFolder -Filter '*.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
                        if ($foundAfter) {
                            Write-Host "Found after building project: $($foundAfter.FullName). Copying to dist root..." -ForegroundColor Green
                            Copy-Item -Path $foundAfter.FullName -Destination $exeInRoot -Force
                        } else {
                            Write-Host "Still couldn't find MusicPlayer.exe after building project." -ForegroundColor Red
                        }
                    }
                }
            } else {
                Write-Host "No .csproj available to inspect or build." -ForegroundColor Red
            }
        }
    }
}

Write-Host "Dist prepared at: $distFolder" -ForegroundColor Green

# Build installer if ISCC provided or available
if (-not [string]::IsNullOrWhiteSpace($ISCCPath) -and (Test-Path $ISCCPath)) {
    Write-Host "Building installer using ISCC: $ISCCPath" -ForegroundColor Cyan
    & $ISCCPath (Join-Path $scriptRoot 'MusicPlayerInstaller.iss')
} else {
    $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($iscc) {
        Write-Host "Building installer using ISCC from PATH" -ForegroundColor Cyan
        & ISCC.exe (Join-Path $scriptRoot 'MusicPlayerInstaller.iss')
    } else {
        Write-Host "ISCC not found. Skipping installer build. Open MusicPlayerInstaller.iss in Inno Setup and compile manually." -ForegroundColor Yellow
    }
}
