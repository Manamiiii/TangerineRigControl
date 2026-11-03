# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Manamiiii
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OldSetup,
    [Parameter(Mandatory = $true)][string]$NewSetup
)
$ErrorActionPreference = 'Stop'
$OldSetup = (Resolve-Path -LiteralPath $OldSetup).Path
$NewSetup = (Resolve-Path -LiteralPath $NewSetup).Path
$name = 'TangerineRigControl.InstallerTest'
foreach ($setup in @($OldSetup, $NewSetup)) {
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($setup).ProductName -ne $name) { throw 'Refusing to install a production identity in fixture tests.' }
}
$install = Join-Path $env:LOCALAPPDATA ('Programs/' + $name)
$data = Join-Path $env:LOCALAPPDATA $name
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) ($name + '.lnk')
$key = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{7F88F898-795F-4D59-8E60-369D5A59630A}'
$run = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'
if ((Test-Path -LiteralPath $install) -or (Test-Path -LiteralPath $data) -or (Test-Path -LiteralPath $key) -or (Test-Path -LiteralPath $shortcut) -or
    ($null -ne (Get-ItemProperty -LiteralPath $run -Name $name -ErrorAction SilentlyContinue))) {
    throw 'Fixture identity already exists. Preserve it and inspect the previous test before rerunning.'
}
$script:process = $null
$checks = 0
function Assert-Test([bool]$condition, [string]$label) {
    if (-not $condition) { throw ('FAIL ' + $label) }
    $script:checks++
    Write-Output ('PASS ' + $label)
}
function Run-Setup([string]$path, [string[]]$arguments, [int]$expected) {
    $p = Start-Process -FilePath $path -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(60000)) { throw 'Installer timeout; no process was terminated.' }
    $p.WaitForExit()
    Assert-Test ($p.ExitCode -eq $expected) ('installer exit code ' + $expected)
}
function Start-Fixture([string]$mode) {
    foreach ($file in @('ready', 'stop')) { $path = Join-Path $data $file; if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path } }
    $script:process = Start-Process -FilePath (Join-Path $install 'TangerineRigControl.exe') -ArgumentList $mode -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path -LiteralPath (Join-Path $data 'ready')) -and [DateTime]::UtcNow -lt $deadline -and -not $script:process.HasExited) {
        Start-Sleep -Milliseconds 100
    }
    Assert-Test ((Test-Path -LiteralPath (Join-Path $data 'ready')) -and -not $script:process.HasExited) 'installed application shows its window and restores synthetic settings'
}
function Stop-Fixture {
    if ($script:process -and -not $script:process.HasExited) {
        Set-Content -LiteralPath (Join-Path $data 'stop') -Value 'stop'
        if (-not $script:process.WaitForExit(15000)) { throw 'Fixture did not exit gracefully; no process was terminated.' }
        $script:process.WaitForExit()
        Assert-Test ($script:process.ExitCode -eq 0) 'application exits gracefully'
    }
    $script:process = $null
}
try {
    Run-Setup $OldSetup @('/S') 0
    Assert-Test ((Test-Path -LiteralPath $shortcut) -and (Test-Path -LiteralPath (Join-Path $install 'Uninstall.exe'))) 'Start menu and uninstaller installed'
    Assert-Test ((Get-ItemProperty -LiteralPath $key).InstallLocation -eq $install) 'per-user uninstall registration'
    Assert-Test ((Get-ItemProperty -LiteralPath $key).DisplayVersion -eq [Diagnostics.FileVersionInfo]::GetVersionInfo($OldSetup).FileVersion) 'old fixture version registered'
    $legacy = New-Object Threading.Mutex($false, ('Local\' + $name + '.SingleInstance'))
    try { Run-Setup $NewSetup @('/S') 20 } finally { $legacy.Dispose() }
    $lease = [IO.File]::Open((Join-Path $data '.lifecycle.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    try { Run-Setup $NewSetup @('/S') 20 } finally { $lease.Dispose() }
    Start-Fixture '--fixture-write'
    $settings = Join-Path $data 'settings.xml'
    $before = (Get-FileHash -LiteralPath $settings).Hash
    $exeHash = (Get-FileHash -LiteralPath (Join-Path $install 'TangerineRigControl.exe')).Hash
    Run-Setup $NewSetup @('/S') 20
    Run-Setup (Join-Path $install 'Uninstall.exe') @('/S', ('_?=' + $install)) 20
    Assert-Test (-not $script:process.HasExited) 'running application was not killed'
    Assert-Test ((Get-FileHash -LiteralPath $settings).Hash -eq $before -and (Get-FileHash -LiteralPath (Join-Path $install 'TangerineRigControl.exe')).Hash -eq $exeHash) 'blocked operations leave executable and data unchanged'
    Stop-Fixture
    $guard = New-Object Threading.Mutex($false, ('Local\' + $name + '.SingleInstance'))
    try { Run-Setup (Join-Path $install 'TangerineRigControl.exe') @('--fixture-read') 2 } finally { $guard.Dispose() }
    $lease = [IO.File]::Open((Join-Path $data '.lifecycle.lock'), 'Open', 'ReadWrite', 'None')
    try { Run-Setup (Join-Path $install 'TangerineRigControl.exe') @('--fixture-read') 3 } finally { $lease.Dispose() }
    $backup = (Get-FileHash -LiteralPath ($settings + '.bak')).Hash
    Start-Fixture '--fixture-read'
    Stop-Fixture
    Run-Setup $NewSetup @('/S') 0
    Assert-Test ((Get-ItemProperty -LiteralPath $key).DisplayVersion -eq [Diagnostics.FileVersionInfo]::GetVersionInfo($NewSetup).FileVersion) 'upgrade registers new fixture version'
    Assert-Test ((Get-FileHash -LiteralPath $settings).Hash -eq $before) 'upgrade preserves XML byte for byte'
    Assert-Test ((Get-FileHash -LiteralPath ($settings + '.bak')).Hash -eq $backup) 'upgrade preserves backup byte for byte'
    Run-Setup $OldSetup @('/S') 23
    Start-Fixture '--fixture-read'
    Stop-Fixture
    if (-not (Test-Path -LiteralPath $run)) { New-Item -Path $run | Out-Null }
    Set-ItemProperty -LiteralPath $run -Name $name -Value ('"' + (Join-Path $install 'TangerineRigControl.exe') + '" --startup')
    Set-Content -LiteralPath (Join-Path $install 'user-added.txt') -Value 'synthetic extra file'
    Run-Setup (Join-Path $install 'Uninstall.exe') @('/S', ('_?=' + $install)) 0
    Assert-Test (-not (Test-Path -LiteralPath $key) -and -not (Test-Path -LiteralPath $shortcut)) 'uninstall removes registration and shortcut'
    Assert-Test ($null -eq (Get-ItemProperty -LiteralPath $run -Name $name -ErrorAction SilentlyContinue)) 'uninstall removes its own startup entry'
    Assert-Test ((Test-Path -LiteralPath (Join-Path $install 'user-added.txt')) -and (Get-FileHash -LiteralPath $settings).Hash -eq $before) 'uninstall retains user-added file and data'
    Assert-Test ((Get-FileHash -LiteralPath ($settings + '.bak')).Hash -eq $backup) 'uninstall preserves backup byte for byte'
    Run-Setup $NewSetup @('/S') 0
    Start-Fixture '--fixture-read'
    Stop-Fixture
    Set-ItemProperty -LiteralPath $run -Name $name -Value 'fixture-unrelated-entry'
    Run-Setup (Join-Path $install 'Uninstall.exe') @('/S', ('_?=' + $install)) 0
    Assert-Test ((Get-ItemPropertyValue -LiteralPath $run -Name $name) -eq 'fixture-unrelated-entry') 'uninstall preserves unrelated startup command'
    Remove-ItemProperty -LiteralPath $run -Name $name
    # Only fixture-owned paths, verified against the exact expected identity, may be removed.
    foreach ($owned in @($install, $data)) {
        if (Test-Path -LiteralPath $owned) {
            $resolved = (Resolve-Path -LiteralPath $owned).Path
            $expected = [IO.Path]::GetFullPath($owned)
            if ($resolved -ne $expected -or [IO.Path]::GetFileName($resolved) -ne $name -or
                ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe fixture cleanup path.' }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
    Write-Output ('Installer fixture lifecycle passed: ' + $checks + ' checks. Production identity and clean Windows are not covered.')
} finally { Stop-Fixture }
