#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:ProgramFiles\LengLeng\KeyboardLayoutIndicator",
    [string]$SettingsDir = "$env:ProgramData\LengLeng\KeyboardLayoutIndicator",
    [switch]$RemoveSettings
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ServiceName = 'LengLengKeyboardLayoutIndicator'
$ProcessName = 'LengLeng.KeyboardLayoutIndicator'
$StartMenuFolderName = 'LengLeng Keyboard Layout Indicator'
$StartMenuDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)) $StartMenuFolderName
$DesktopShortcutPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)) 'LengLeng Keyboard Layout Indicator.lnk'

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Remove-DirectorySafely {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathToRemove
    )

    if (-not (Test-Path -LiteralPath $PathToRemove)) {
        return
    }

    $resolvedTarget = [System.IO.Path]::GetFullPath($PathToRemove)
    if ((Split-Path -Leaf $resolvedTarget) -ne 'KeyboardLayoutIndicator') {
        throw "Refusing to delete unexpected directory: $resolvedTarget"
    }

    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

if (-not (Test-Administrator)) {
    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', "`"$PSCommandPath`"",
        '-InstallDir', "`"$InstallDir`"",
        '-SettingsDir', "`"$SettingsDir`""
    )
    if ($RemoveSettings) {
        $arguments += '-RemoveSettings'
    }

    Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs
    exit 0
}

$StopSignalPath = Join-Path $SettingsDir 'agent.stop'
New-Item -ItemType Directory -Force -Path $SettingsDir | Out-Null
Set-Content -LiteralPath $StopSignalPath -Value (Get-Date).ToString('O')

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
    }

    & sc.exe delete $ServiceName | Out-Host
}

Start-Sleep -Seconds 2
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path -LiteralPath $DesktopShortcutPath) {
    Remove-Item -LiteralPath $DesktopShortcutPath -Force
}

if (Test-Path -LiteralPath $StartMenuDir) {
    Remove-Item -LiteralPath $StartMenuDir -Recurse -Force
}

Set-Location -LiteralPath $env:TEMP
Remove-DirectorySafely -PathToRemove $InstallDir

if ($RemoveSettings) {
    Remove-DirectorySafely -PathToRemove $SettingsDir
}

Write-Host "Uninstalled $ServiceName."
if (-not $RemoveSettings) {
    Write-Host "Settings kept: $SettingsDir"
}
