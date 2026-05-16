#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:ProgramFiles\LengLeng\KeyboardLayoutIndicator",
    [string]$SettingsDir = "$env:ProgramData\LengLeng\KeyboardLayoutIndicator"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'LengLengKeyboardLayoutIndicator'
$DisplayName = 'LengLeng Keyboard Layout Indicator'
$ProcessName = 'LengLeng.KeyboardLayoutIndicator'

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', "`"$PSCommandPath`"",
        '-InstallDir', "`"$InstallDir`"",
        '-SettingsDir', "`"$SettingsDir`""
    )
    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs
    exit 0
}

$PackageRoot = Split-Path -Parent $PSCommandPath
$SourceApp = Join-Path $PackageRoot 'app'
$SourceConfig = Join-Path $PackageRoot 'config\appsettings.json'
$ExePath = Join-Path $InstallDir "$ProcessName.exe"
$SettingsPath = Join-Path $SettingsDir 'appsettings.json'
$StopSignalPath = Join-Path $SettingsDir 'agent.stop'
$StartMenuFolderName = 'LengLeng Keyboard Layout Indicator'
$StartMenuDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)) $StartMenuFolderName
$DesktopShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)) 'LengLeng Keyboard Layout Indicator.lnk'

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$TargetPath,

        [string]$Arguments = '',
        [string]$WorkingDirectory = '',
        [string]$IconLocation = '',
        [string]$Description = ''
    )

    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $parent | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $TargetPath
    $shortcut.Arguments = $Arguments
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $shortcut.WorkingDirectory = $WorkingDirectory
    }
    if (-not [string]::IsNullOrWhiteSpace($IconLocation)) {
        $shortcut.IconLocation = $IconLocation
    }
    if (-not [string]::IsNullOrWhiteSpace($Description)) {
        $shortcut.Description = $Description
    }
    $shortcut.Save()
}

if (-not (Test-Path -LiteralPath (Join-Path $SourceApp "$ProcessName.exe"))) {
    throw "Application files were not found in $SourceApp. Run build-installer.ps1 first."
}

New-Item -ItemType Directory -Force -Path $InstallDir, $SettingsDir | Out-Null

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $service -and $service.Status -ne 'Stopped') {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
}

if (Test-Path -LiteralPath $StopSignalPath) {
    Remove-Item -LiteralPath $StopSignalPath -Force
}

Start-Sleep -Seconds 2
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Copy-Item -Path (Join-Path $SourceApp '*') -Destination $InstallDir -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PackageRoot 'uninstall.ps1') -Destination (Join-Path $InstallDir 'uninstall.ps1') -Force
Copy-Item -LiteralPath (Join-Path $PackageRoot 'Uninstall.cmd') -Destination (Join-Path $InstallDir 'Uninstall.cmd') -Force

if (-not (Test-Path -LiteralPath $SettingsPath)) {
    Copy-Item -LiteralPath $SourceConfig -Destination $SettingsPath -Force
}

& icacls.exe $SettingsDir /grant '*S-1-5-32-545:(OI)(CI)M' /T /C | Out-Host
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Settings/log directory permissions were not fully updated. icacls exit code: $LASTEXITCODE"
}

$binPath = "`"$ExePath`" --config `"$SettingsPath`""
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($null -ne $existing) {
    & sc.exe delete $ServiceName | Out-Host
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 500
        $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($null -eq $existing) {
            break
        }
    }
}

if ($null -ne (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    throw "Cannot remove existing service $ServiceName before reinstall."
}

New-Service `
    -Name $ServiceName `
    -BinaryPathName $binPath `
    -DisplayName $DisplayName `
    -Description 'Scroll Lock keyboard layout indicator for interactive user sessions.' `
    -StartupType Automatic | Out-Host

& sc.exe failure $ServiceName reset= 86400 actions= 'restart/5000/restart/5000/""/0' | Out-Host
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Service was installed, but failure restart policy was not configured. sc.exe exit code: $LASTEXITCODE"
}

Start-Service -Name $ServiceName

$quotedSettingsPath = "`"$SettingsPath`""
$shortcutWorkingDirectory = $InstallDir
$shortcutIcon = "$ExePath,0"

if (Test-Path -LiteralPath $StartMenuDir) {
    Remove-Item -LiteralPath $StartMenuDir -Recurse -Force
}

if (Test-Path -LiteralPath $DesktopShortcutPath) {
    Remove-Item -LiteralPath $DesktopShortcutPath -Force
}

New-Shortcut `
    -Path $DesktopShortcutPath `
    -TargetPath $ExePath `
    -Arguments "--start-service --config $quotedSettingsPath" `
    -WorkingDirectory $shortcutWorkingDirectory `
    -IconLocation $shortcutIcon `
    -Description 'Start LengLeng Keyboard Layout Indicator service.'

New-Shortcut `
    -Path (Join-Path $StartMenuDir 'Start LengLeng.lnk') `
    -TargetPath $ExePath `
    -Arguments "--start-service --config $quotedSettingsPath" `
    -WorkingDirectory $shortcutWorkingDirectory `
    -IconLocation $shortcutIcon `
    -Description 'Start LengLeng Keyboard Layout Indicator service.'

New-Shortcut `
    -Path (Join-Path $StartMenuDir 'Stop service and exit.lnk') `
    -TargetPath $ExePath `
    -Arguments "--stop-service --config $quotedSettingsPath" `
    -WorkingDirectory $shortcutWorkingDirectory `
    -IconLocation $shortcutIcon `
    -Description 'Stop LengLeng Keyboard Layout Indicator service and exit the tray agent.'

New-Shortcut `
    -Path (Join-Path $StartMenuDir 'Settings.lnk') `
    -TargetPath 'notepad.exe' `
    -Arguments $quotedSettingsPath `
    -WorkingDirectory $SettingsDir `
    -IconLocation 'notepad.exe,0' `
    -Description 'Open LengLeng Keyboard Layout Indicator settings.'

New-Shortcut `
    -Path (Join-Path $StartMenuDir 'Uninstall LengLeng.lnk') `
    -TargetPath (Join-Path $InstallDir 'Uninstall.cmd') `
    -WorkingDirectory $env:TEMP `
    -IconLocation (Join-Path $InstallDir 'Uninstall.cmd') `
    -Description 'Uninstall LengLeng Keyboard Layout Indicator.'

Write-Host "Installed and started: $DisplayName"
Write-Host "Settings: $SettingsPath"
Write-Host "Logs: $(Join-Path $SettingsDir 'logs\service.log')"
Write-Host "Start menu: $StartMenuDir"
Write-Host "Desktop shortcut: $DesktopShortcutPath"
