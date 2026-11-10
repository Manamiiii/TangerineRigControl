# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Manamiiii
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedExecutable,
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256,
    [string]$ShortcutPath,
    [string]$ExpectedArguments = ''
)
$ErrorActionPreference = 'Stop'
if (-not $ShortcutPath) {
    $ShortcutPath = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Tangerine/TangerineRigControl.lnk'
}
$ShortcutPath = (Resolve-Path -LiteralPath $ShortcutPath).Path
if ([IO.Path]::GetFileName($ShortcutPath) -ne 'TangerineRigControl.lnk') { throw 'Expected this project shortcut.' }
$ExpectedExecutable = (Resolve-Path -LiteralPath $ExpectedExecutable).Path
$assembly = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../src/TangerineRigControl/Properties/AssemblyInfo.cs') -Raw
$versionMatch = [regex]::Match($assembly, 'AssemblyInformationalVersion\("([^"]+)"\)')
if (-not $versionMatch.Success) { throw 'Source version missing.' }
$expectedInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($ExpectedExecutable)
if ($expectedInfo.ProductName -ne 'TangerineRigControl' -or $expectedInfo.ProductVersion -ne $versionMatch.Groups[1].Value -or
    (Get-FileHash -LiteralPath $ExpectedExecutable -Algorithm SHA256).Hash -ne $ExpectedSha256) {
    throw 'Expected executable does not match the source version and supplied verified hash.'
}
# Read only. In particular, never call Save(), run the target, or load settings XML.
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($ShortcutPath)
if (-not (Test-Path -LiteralPath $link.TargetPath -PathType Leaf)) { throw 'Shortcut target missing; inspect the launch chain.' }
$actualInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($link.TargetPath)
if ($actualInfo.ProductName -ne 'TangerineRigControl') { throw 'Indirect or unrecognized target; inspect it without launching.' }
$actualHash = (Get-FileHash -LiteralPath $link.TargetPath -Algorithm SHA256).Hash
$matches = $link.TargetPath -eq $ExpectedExecutable -and $actualHash -eq $ExpectedSha256 -and
    $actualInfo.ProductVersion -eq $expectedInfo.ProductVersion -and $link.Arguments -ceq $ExpectedArguments -and
    $link.WorkingDirectory -eq [IO.Path]::GetDirectoryName($ExpectedExecutable) -and $link.IconLocation -eq ($ExpectedExecutable + ',0')
$processes = @(Get-CimInstance Win32_Process -Filter "Name='TangerineRigControl.exe'")
$mutex = $null
$mutexExists = [Threading.Mutex]::TryOpenExisting('Local\TangerineRigControl.SingleInstance', [ref]$mutex)
if ($mutex) { $mutex.Dispose() }
[pscustomobject]@{
    Shortcut = $ShortcutPath
    Target = $link.TargetPath
    Arguments = $link.Arguments
    WorkingDirectory = $link.WorkingDirectory
    Icon = $link.IconLocation
    ProductVersion = $actualInfo.ProductVersion
    Sha256 = $actualHash.ToLowerInvariant()
    EntryMatchesExpected = $matches
    ProcessCount = $processes.Count
    ProcessPaths = @($processes | ForEach-Object { $_.ExecutablePath })
    MutexExists = $mutexExists
    LegacySettingsPresent = (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'TangerineRigControl/settings.xml')) -or
        (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'TangerineRigControl/settings.xml.bak'))
    CurrentSettingsPresent = Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Tangerine/Data/TangerineRigControl/Config/settings.xml')
    RealDataAndRunningVersionValidated = $false
}
if (-not $matches) { throw 'Desktop entry differs from the verified build. No entry or process was changed.' }
