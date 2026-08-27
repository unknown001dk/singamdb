# ==============================================================================
#  SingamDB Native Windows PowerShell Installer (Zero-Prerequisite)
#  Usage:
#    irm "https://raw.githubusercontent.com/unknown001dk/singamdb/main/install.ps1?$(Get-Random)" | iex
# ==============================================================================

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

# Locate or Install .NET SDK 8.0
$DotnetExecutable = "dotnet"

if (Test-Path "$DotnetDir\dotnet.exe") {
    $DotnetExecutable = "$DotnetDir\dotnet.exe"
    $env:DOTNET_ROOT = $DotnetDir
    $env:Path = "$DotnetDir;$env:Path"
}

$DotnetFound = $false
try {
    $check = & $DotnetExecutable --version 2>$null
    if ($check) {
        Write-Host "[OK] Detected .NET SDK: $check" -ForegroundColor Green
        $DotnetFound = $true
    }
} catch {}

if (-not $DotnetFound) {
    Write-Host "[*] .NET SDK not detected. Downloading portable .NET 8 SDK..." -ForegroundColor Yellow
    $installerScript = "$env:TEMP\dotnet-install.ps1"
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installerScript -UseBasicParsing
        
        Write-Host "[*] Installing .NET 8 SDK to $DotnetDir..." -ForegroundColor Yellow
        & powershell -NoProfile -ExecutionPolicy Bypass -File $installerScript -Channel 8.0 -InstallDir $DotnetDir
        
        $env:DOTNET_ROOT = $DotnetDir
        $env:Path = "$DotnetDir;$env:Path"
        $DotnetExecutable = "$DotnetDir\dotnet.exe"
        
        # Add .dotnet permanently to User environment Path
        $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
        if ($userPath -notlike "*$DotnetDir*") {
            [Environment]::SetEnvironmentVariable("Path", "$DotnetDir;$userPath", "User")
        }
        
        Write-Host "[OK] .NET 8 SDK configured successfully!" -ForegroundColor Green
    } catch {
        Write-Host "[ERROR] Auto-install failed: $_" -ForegroundColor Red
        Write-Host "Please download .NET 8 SDK manually from: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
        return
    }
}

# Check if running from local repository
$IsSourceDir = $false
if ((Test-Path "SingamDB.sln") -and (Test-Path "SingamDB.Server")) {
    $IsSourceDir = $true
}

if ($IsSourceDir) {
    Write-Host "[1/3] Compiling SingamDB from local source..." -ForegroundColor Yellow
    & $DotnetExecutable publish SingamDB.Server/SingamDB.Server.csproj -c Release -o "$LibDir\server" --nologo -v q
    & $DotnetExecutable publish SingamDB.Cli/SingamDB.Cli.csproj -c Release -o "$LibDir\cli" --nologo -v q
} else {
    Write-Host "[1/3] Downloading latest SingamDB source from GitHub..." -ForegroundColor Yellow
    try {
        git clone --depth 1 https://github.com/unknown001dk/singamdb.git "$TempDir"
        & $DotnetExecutable publish "$TempDir\SingamDB.Server\SingamDB.Server.csproj" -c Release -o "$LibDir\server" --nologo -v q
        & $DotnetExecutable publish "$TempDir\SingamDB.Cli\SingamDB.Cli.csproj" -c Release -o "$LibDir\cli" --nologo -v q
        Remove-Item -Recurse -Force "$TempDir" -ErrorAction SilentlyContinue
    } catch {
        Write-Host "[ERROR] Build failed: $_" -ForegroundColor Red
        Remove-Item -Recurse -Force "$TempDir" -ErrorAction SilentlyContinue
        return
    }
}

# Create Windows Launchers in bin folder
Write-Host "[2/3] Creating Windows executable launchers..." -ForegroundColor Yellow

$serverCmd = @"
@echo off
set "PATH=%USERPROFILE%\.dotnet;%PATH%"
set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
dotnet "%USERPROFILE%\.singam\lib\server\SingamDB.Server.dll" %*
"@
Set-Content -Path "$InstallDir\singam-server.cmd" -Value $serverCmd

$cliCmd = @"
@echo off
set "PATH=%USERPROFILE%\.dotnet;%PATH%"
set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
dotnet "%USERPROFILE%\.singam\lib\cli\SingamDB.Cli.dll" %*
"@
Set-Content -Path "$InstallDir\singam.cmd" -Value $cliCmd
Set-Content -Path "$InstallDir\singam-cli.cmd" -Value $cliCmd

# Create PowerShell launcher scripts
$serverPs1 = @"
`$env:DOTNET_ROOT = "`$env:USERPROFILE\.dotnet"
`$env:Path = "`$env:USERPROFILE\.dotnet;`$env:Path"
& "`$env:USERPROFILE\.dotnet\dotnet.exe" "`$env:USERPROFILE\.singam\lib\server\SingamDB.Server.dll" @args
"@
Set-Content -Path "$InstallDir\singam-server.ps1" -Value $serverPs1

$cliPs1 = @"
`$env:DOTNET_ROOT = "`$env:USERPROFILE\.dotnet"
`$env:Path = "`$env:USERPROFILE\.dotnet;`$env:Path"
& "`$env:USERPROFILE\.dotnet\dotnet.exe" "`$env:USERPROFILE\.singam\lib\cli\SingamDB.Cli.dll" @args
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
Write-Host "To start using SingamDB:" -ForegroundColor White
Write-Host "  1. Close and reopen PowerShell" -ForegroundColor DarkGray
Write-Host "  2. Start DB Server:      singam-server" -ForegroundColor Cyan
Write-Host "  3. Open Interactive CLI: singam" -ForegroundColor Cyan
Write-Host ""
