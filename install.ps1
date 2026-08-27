# ==============================================================================
#  SingamDB Native Windows PowerShell Installer (Zero-Prerequisite)
#  Usage:
#    irm https://raw.githubusercontent.com/unknown001dk/singamdb/main/install.ps1 | iex
# ==============================================================================

$ErrorActionPreference = "Stop"

Write-Host @"
   ____  _                            ____  ____  
  / ___|(_)_ __   __ _  __ _ _ __ ___ |  _ \| __ ) 
  \___ \| | '_ \ / _` |/ _` | '_ ` _ \| | | |  _ \ 
   ___) | | | | | (_| | (_| | | | | | | |_| | |_) |
  |____/|_|_| |_|\__, |\__,_|_| |_| |_|____/|____/ 
                 |___/                             
"@ -ForegroundColor Cyan

Write-Host "High-Performance Storage Engine & Wire Protocol Database Server" -ForegroundColor White
Write-Host "===================================================================" -ForegroundColor Cyan

$InstallDir = "$env:USERPROFILE\.singam\bin"
$LibDir = "$env:USERPROFILE\.singam\lib"
$TempDir = "$env:TEMP\singamdb_install_$(Get-Random)"
$DotnetDir = "$env:USERPROFILE\.dotnet"

Write-Host "Detected Platform: Windows (x64)" -ForegroundColor Green
Write-Host "Installing to:     $InstallDir" -ForegroundColor Cyan
Write-Host ""

# Ensure install dirs exist
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
New-Item -ItemType Directory -Force -Path $LibDir | Out-Null

# Check if dotnet is installed; if not, automatically install .NET 8 SDK
$HasDotnet = $false
try {
    $v = & dotnet --version 2>$null
    if ($v) {
        Write-Host "[OK] Detected .NET: $v" -ForegroundColor Green
        $HasDotnet = $true
    }
} catch {}

if (-not $HasDotnet) {
    if (Test-Path "$DotnetDir\dotnet.exe") {
        $env:Path = "$DotnetDir;$env:Path"
        $HasDotnet = $true
        Write-Host "[OK] Using existing .NET in $DotnetDir" -ForegroundColor Green
    }
}

if (-not $HasDotnet) {
    Write-Host "[*] .NET SDK not found. Automatically installing .NET 8 SDK (portable, no admin required)..." -ForegroundColor Yellow
    $installerScript = "$env:TEMP\dotnet-install.ps1"
    try {
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installerScript -UseBasicParsing
        & powershell -NoProfile -ExecutionPolicy Bypass -File $installerScript -Channel 8.0 -InstallDir $DotnetDir
        $env:Path = "$DotnetDir;$env:Path"
        
        # Add .dotnet to user PATH permanently
        $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
        if ($userPath -notlike "*$DotnetDir*") {
            [Environment]::SetEnvironmentVariable("Path", "$DotnetDir;$userPath", "User")
        }
        Write-Host "[OK] .NET 8 SDK installed successfully!" -ForegroundColor Green
    } catch {
        Write-Host "[ERROR] Could not auto-install .NET: $_" -ForegroundColor Red
        Write-Host "Please download .NET 8 from: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
        exit 1
    }
}

# Check if running from local repository
$IsSourceDir = $false
if ((Test-Path "SingamDB.sln") -and (Test-Path "SingamDB.Server")) {
    $IsSourceDir = $true
}

if ($IsSourceDir) {
    Write-Host "[1/3] Compiling SingamDB from local source..." -ForegroundColor Yellow
    & dotnet publish SingamDB.Server/SingamDB.Server.csproj -c Release -o "$LibDir\server" --nologo -v q
    & dotnet publish SingamDB.Cli/SingamDB.Cli.csproj -c Release -o "$LibDir\cli" --nologo -v q
} else {
    Write-Host "[1/3] Cloning latest SingamDB source from GitHub..." -ForegroundColor Yellow
    try {
        git clone --depth 1 https://github.com/unknown001dk/singamdb.git "$TempDir"
        & dotnet publish "$TempDir\SingamDB.Server\SingamDB.Server.csproj" -c Release -o "$LibDir\server" --nologo -v q
        & dotnet publish "$TempDir\SingamDB.Cli\SingamDB.Cli.csproj" -c Release -o "$LibDir\cli" --nologo -v q
        Remove-Item -Recurse -Force "$TempDir" -ErrorAction SilentlyContinue
    } catch {
        Write-Host "[ERROR] Build failed: $_" -ForegroundColor Red
        Remove-Item -Recurse -Force "$TempDir" -ErrorAction SilentlyContinue
        exit 1
    }
}

# Create Windows CMD Wrappers in bin folder
Write-Host "[2/3] Creating Windows executable launchers..." -ForegroundColor Yellow

$serverCmd = @"
@echo off
set "PATH=%USERPROFILE%\.dotnet;%PATH%"
dotnet "%USERPROFILE%\.singam\lib\server\SingamDB.Server.dll" %*
"@
Set-Content -Path "$InstallDir\singam-server.cmd" -Value $serverCmd

$cliCmd = @"
@echo off
set "PATH=%USERPROFILE%\.dotnet;%PATH%"
dotnet "%USERPROFILE%\.singam\lib\cli\SingamDB.Cli.dll" %*
"@
Set-Content -Path "$InstallDir\singam.cmd" -Value $cliCmd
Set-Content -Path "$InstallDir\singam-cli.cmd" -Value $cliCmd

# Create PowerShell launcher scripts
$serverPs1 = @"
`$env:Path = "`$env:USERPROFILE\.dotnet;`$env:Path"
& dotnet "`$env:USERPROFILE\.singam\lib\server\SingamDB.Server.dll" @args
"@
Set-Content -Path "$InstallDir\singam-server.ps1" -Value $serverPs1

$cliPs1 = @"
`$env:Path = "`$env:USERPROFILE\.dotnet;`$env:Path"
& dotnet "`$env:USERPROFILE\.singam\lib\cli\SingamDB.Cli.dll" @args
"@
Set-Content -Path "$InstallDir\singam.ps1" -Value $cliPs1

# Add to User PATH
Write-Host "[3/3] Configuring Environment PATH..." -ForegroundColor Yellow

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -notlike "*$InstallDir*") {
    $newPath = "$InstallDir;$userPath"
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    $env:Path = "$InstallDir;$env:Path"
    Write-Host "  Added $InstallDir to User PATH." -ForegroundColor Green
} else {
    Write-Host "  $InstallDir is already in User PATH." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "===================================================================" -ForegroundColor Green
Write-Host " [SUCCESS] SingamDB installed successfully on Windows!" -ForegroundColor Green
Write-Host "===================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "To start using SingamDB immediately:" -ForegroundColor White
Write-Host "  1. Close and reopen PowerShell (or run `$env:Path = [Environment]::GetEnvironmentVariable('Path','User'))" -ForegroundColor DarkGray
Write-Host "  2. Start DB Server:      singam-server" -ForegroundColor Cyan
Write-Host "  3. Open Interactive CLI: singam" -ForegroundColor Cyan
Write-Host ""
