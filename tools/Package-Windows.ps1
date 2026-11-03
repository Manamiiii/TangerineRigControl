# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Manamiiii
[CmdletBinding()]
param(
    [string]$ExecutablePath = 'src/TangerineRigControl/bin/Release/TangerineRigControl.exe',
    [string]$OutputDirectory = 'artifacts/windows',
    [string]$MakensisPath,
    [switch]$Fixture,
    [string]$FixtureVersion
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not [IO.Path]::IsPathRooted($ExecutablePath)) { $ExecutablePath = Join-Path $root $ExecutablePath }
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $root $OutputDirectory }
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $MakensisPath) {
    $cache = Join-Path $env:LOCALAPPDATA 'electron-builder/Cache/nsis-3.0.4.1'
    if (Test-Path -LiteralPath $cache) {
        $MakensisPath = Get-ChildItem -LiteralPath $cache -Recurse -Filter makensis.exe |
            Where-Object { $_.Directory.Name -ne 'Bin' } | Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $MakensisPath) { throw 'Supply -MakensisPath for the pinned portable NSIS compiler; see docs/INSTALLER.md.' }
$MakensisPath = (Resolve-Path -LiteralPath $MakensisPath).Path
if ((Get-FileHash -LiteralPath $MakensisPath -Algorithm SHA256).Hash -ne 'F2B2B7726AC0D4E720DFF52BFCA11A5518D550FC75ED34A48DC47921527293F0') {
    throw 'Unexpected NSIS compiler. Use the pinned nsis-3.0.4.1 distribution.'
}
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo($ExecutablePath)
$bytes = [IO.File]::ReadAllBytes($ExecutablePath)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
if ($peOffset -lt 0 -or $peOffset + 26 -gt $bytes.Length -or
    [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550 -or
    [BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664) { throw 'Setup requires a Windows x64 application binary.' }
$appName = if ($Fixture) { 'TangerineRigControl.InstallerTest' } else { 'TangerineRigControl' }
if ($info.ProductName -ne $appName) { throw 'Application identity does not match the requested installer identity.' }
if (-not $Fixture -and $FixtureVersion) { throw 'Only isolated fixtures may override the installer version.' }
$numeric = if ($FixtureVersion) { $FixtureVersion } else { $info.FileVersion }
if ($numeric -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid numeric installer version.' }
$label = if ($Fixture) { $numeric + '-fixture' } else { $info.ProductVersion }
$appGuid = if ($Fixture) { '{7F88F898-795F-4D59-8E60-369D5A59630A}' } else { '{D9BC8BD8-3A19-4798-A233-18C4B5B62BE4}' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$setup = Join-Path $OutputDirectory ($appName + '-' + $label + '-windows-x64-Setup.exe')
if ((Test-Path -LiteralPath $setup) -or (Test-Path -LiteralPath ($setup + '.sha256'))) { throw 'Installer already exists; use a new output directory.' }
& (Join-Path $PSScriptRoot 'Package-Portable.ps1') -ExecutablePath $ExecutablePath -OutputDirectory $OutputDirectory
$archive = Join-Path $OutputDirectory ('TangerineRigControl-' + $info.ProductVersion + '-windows-portable.zip')
$work = Join-Path $OutputDirectory ('setup-staging-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
try {
    Expand-Archive -LiteralPath $archive -DestinationPath $payload
    $licensePage = Join-Path $work 'license-utf16.txt'
    [IO.File]::WriteAllText($licensePage, [IO.File]::ReadAllText((Join-Path $payload 'LICENSE'), [Text.Encoding]::UTF8), [Text.Encoding]::Unicode)
    $payloadFiles = @(Get-ChildItem -LiteralPath $payload -File -Recurse)
    $payloadDirs = @(Get-ChildItem -LiteralPath $payload -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending)
    function Relative-NSIS([string]$path) {
        $relative = $path.Substring($payload.Length + 1).Replace('/', '\')
        if ($relative -match '[\r\n"$]' -or $relative.Contains('..')) { throw 'Unsafe payload filename.' }
        return $relative
    }
    $remove = Join-Path $work 'remove.nsh'
    @(
        'ClearErrors'
        foreach ($file in $payloadFiles) { 'Delete "$INSTDIR\' + (Relative-NSIS $file.FullName) + '"' }
        '${If} ${Errors}'
        '  !insertmacro Fail 24 "Some installed files could not be removed. Close the application and retry; your data is retained."'
        '${EndIf}'
        foreach ($dir in $payloadDirs) { 'RMDir "$INSTDIR\' + (Relative-NSIS $dir.FullName) + '"' }
    ) | Set-Content -LiteralPath $remove -Encoding UTF8
    $check = Join-Path $work 'directories.nsh'
    @(
        foreach ($dir in $payloadDirs) {
            'System::Call ''kernel32::GetFileAttributesW(w "$INSTDIR\' + (Relative-NSIS $dir.FullName) + '") i .r0'''
            '${If} $0 != -1'
            '  IntOp $1 $0 & 0x400'
            '  ${If} $1 != 0'
            '    !insertmacro Fail 22 "A payload directory is a link. Installation/uninstallation has stopped."'
            '  ${EndIf}'
            '${EndIf}'
        }
    ) | Set-Content -LiteralPath $check -Encoding UTF8
    $arguments = @('/V2', '/INPUTCHARSET', 'UTF8', "/DAPP_NAME=$appName", "/DAPP_GUID=$appGuid", "/DNUMERIC_VERSION=$numeric",
        "/DPAYLOAD=$payload", "/DOUTPUT_FILE=$setup", "/DREMOVE_MANIFEST=$remove", "/DDIRECTORY_CHECKS=$check", "/DLICENSE_PAGE=$licensePage",
        (Join-Path $root 'packaging/windows/installer.nsi'))
    & $MakensisPath @arguments
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setup)) { throw 'NSIS build failed.' }
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    ($hash + '  ' + [IO.Path]::GetFileName($setup)) | Set-Content -LiteralPath ($setup + '.sha256') -Encoding ASCII
    Write-Output $setup
    Write-Output ($setup + '.sha256')
} finally {
    $resolved = (Resolve-Path -LiteralPath $work).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne $OutputDirectory -or [IO.Path]::GetFileName($resolved) -notmatch '^setup-staging-[a-f0-9]{32}$') {
        throw 'Refusing to clean an unexpected staging directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
