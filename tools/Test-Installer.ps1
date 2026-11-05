# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Manamiiii
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OldSetup,
    [Parameter(Mandatory = $true)][string]$NewSetup,
    [switch]$PreviousSuiteLayout,
    [switch]$Headless
)
$ErrorActionPreference = 'Stop'
$OldSetup = (Resolve-Path -LiteralPath $OldSetup).Path
$NewSetup = (Resolve-Path -LiteralPath $NewSetup).Path
$name = 'TangerineRigControl.InstallerTest'
foreach ($setup in @($OldSetup, $NewSetup)) {
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($setup).ProductName -ne $name) { throw 'Refusing to install a production identity in fixture tests.' }
}
$install = if ($PreviousSuiteLayout) { Join-Path $env:LOCALAPPDATA ('Programs/Tangerine/' + $name) } else { Join-Path $env:LOCALAPPDATA ('Programs/' + $name) }
$legacyInstall = $install
$newInstall = Join-Path $env:LOCALAPPDATA ('Tangerine/Apps/' + $name)
$legacyData = Join-Path $env:LOCALAPPDATA $name
$data = Join-Path $env:LOCALAPPDATA ('Tangerine/Data/' + $name)
$runtime = Join-Path $data 'Runtime'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) ($name + '.lnk')
$key = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{7F88F898-795F-4D59-8E60-369D5A59630A}'
$run = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'
if ((Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA ('Programs/' + $name))) -or (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA ('Programs/Tangerine/' + $name))) -or (Test-Path -LiteralPath $legacyData) -or (Test-Path -LiteralPath $newInstall) -or (Test-Path -LiteralPath $install) -or (Test-Path -LiteralPath $data) -or (Test-Path -LiteralPath $key) -or (Test-Path -LiteralPath $shortcut) -or
    ($null -ne (Get-ItemProperty -LiteralPath $run -Name $name -ErrorAction SilentlyContinue))) {
    throw 'Fixture identity already exists. Preserve it and inspect the previous test before rerunning.'
}
$existingMutex = $null
if ([Threading.Mutex]::TryOpenExisting(('Local\' + $name + '.SingleInstance'), [ref]$existingMutex)) {
    $existingMutex.Dispose()
    throw 'Fixture application/installer is already running; no process was stopped.'
}
if (@(Get-Process TangerineRigControl -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'An application process exists. Preserve it; run fixtures on an isolated runner instead.'
}
$script:process = $null
$checks = 0
function Assert-Test([bool]$condition, [string]$label) {
    if (-not $condition) { throw ('FAIL ' + $label) }
    $script:checks++
    Write-Output ('PASS ' + $label)
}
function Run-Uninstall([int]$expected) {
    # As in a normal uninstall, execute a temporary copy so the installed copy can be removed.
    $copy = Join-Path ([IO.Path]::GetTempPath()) ('rig-uninstall-' + [Guid]::NewGuid().ToString('N') + '.exe')
    Copy-Item -LiteralPath (Join-Path $install 'Uninstall.exe') -Destination $copy
    try { Run-Setup $copy @('/S', ('_?=' + $install)) $expected }
    finally { Remove-Item -LiteralPath $copy }
}
function Run-Setup([string]$path, [string[]]$arguments, [int]$expected) {
    $p = Start-Process -FilePath $path -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(60000)) { throw 'Installer timeout; no process was terminated.' }
    $p.WaitForExit()
    Assert-Test ($p.ExitCode -eq $expected) ('installer exit code ' + $expected)
}
function Start-Fixture([string]$mode) {
    if ($Headless) {
        $command = if ($mode -eq '--fixture-write') { '--fixture-save-only' } else { '--fixture-read-only' }
        Run-Setup (Join-Path $install 'TangerineRigControl.exe') @($command) 0
        $script:hold = [IO.File]::Open((Join-Path $runtime '.lifecycle.lock'), 'Open', 'ReadWrite', 'None')
        return
    }
    foreach ($file in @('ready', 'stop', 'tray-close-passed')) { $path = Join-Path $runtime $file; if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path } }
    $script:process = Start-Process -FilePath (Join-Path $install 'TangerineRigControl.exe') -ArgumentList $mode -WindowStyle Hidden -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path -LiteralPath (Join-Path $runtime 'ready')) -and [DateTime]::UtcNow -lt $deadline -and -not $script:process.HasExited) {
        Start-Sleep -Milliseconds 100
    }
    Assert-Test ((Test-Path -LiteralPath (Join-Path $runtime 'ready')) -and -not $script:process.HasExited) 'installed application shows its window and restores synthetic settings'
}
function Stop-Fixture {
    if ($Headless) { if ($script:hold) { $script:hold.Dispose(); $script:hold = $null }; return }
    if ($script:process -and -not $script:process.HasExited) {
        Set-Content -LiteralPath (Join-Path $runtime 'stop') -Value 'stop'
        if (-not $script:process.WaitForExit(15000)) { throw 'Fixture did not exit gracefully; no process was terminated.' }
        $script:process.WaitForExit()
        Assert-Test ($script:process.ExitCode -eq 0) 'application exits gracefully'
        Assert-Test (Test-Path -LiteralPath (Join-Path $runtime 'tray-close-passed')) 'window close hides to tray; tray exit releases the application'
    }
    $script:process = $null
}
try {
    New-Item -ItemType Directory -Path $legacyData | Out-Null
    $originalSettings = Join-Path $legacyData 'settings.xml'
    '<RigSettings><SchemaVersion>1</SchemaVersion><UseAuraSdk>false</UseAuraSdk><LConnect><InitialDelayMilliseconds>777</InitialDelayMilliseconds></LConnect></RigSettings>' | Set-Content -LiteralPath $originalSettings -Encoding UTF8
    Copy-Item -LiteralPath $originalSettings -Destination ($originalSettings + '.bak')
    $originalHash = (Get-FileHash -LiteralPath $originalSettings).Hash
    Run-Setup $OldSetup @('/S') 0
    Assert-Test ((Test-Path -LiteralPath $shortcut) -and (Test-Path -LiteralPath (Join-Path $install 'Uninstall.exe'))) 'Start menu and uninstaller installed'
    Assert-Test ((Get-ItemProperty -LiteralPath $key).InstallLocation -eq $install) 'per-user uninstall registration'
    Assert-Test ((Get-ItemProperty -LiteralPath $key).DisplayVersion -eq [Diagnostics.FileVersionInfo]::GetVersionInfo($OldSetup).FileVersion) 'old fixture version registered'
    Run-Setup (Join-Path $install 'TangerineRigControl.exe') @('--fixture-startup-test') 0
    Set-ItemProperty -LiteralPath $key -Name InstallLocation -Value (Split-Path $newInstall)
    try { Run-Setup $NewSetup @('/S') 22 }
    finally { Set-ItemProperty -LiteralPath $key -Name InstallLocation -Value $legacyInstall }
    $legacy = New-Object Threading.Mutex($false, ('Local\' + $name + '.SingleInstance'))
    try { Run-Setup $NewSetup @('/S') 20 } finally { $legacy.Dispose() }
    $lease = [IO.File]::Open((Join-Path $legacyData '.lifecycle.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    try { Run-Setup $NewSetup @('/S') 20 } finally { $lease.Dispose() }
    Start-Fixture '--fixture-write'
    $settings = Join-Path $data 'Config/settings.xml'
    Assert-Test ((Get-FileHash -LiteralPath $originalSettings).Hash -eq $originalHash -and (Test-Path -LiteralPath (Join-Path $runtime 'legacy-source.sha256'))) 'first launch migrates synthetic legacy data and preserves originals'
    $before = (Get-FileHash -LiteralPath $settings).Hash
    $exeHash = (Get-FileHash -LiteralPath (Join-Path $install 'TangerineRigControl.exe')).Hash
    Run-Setup $NewSetup @('/S') 20
    Run-Uninstall 20
    if (-not $Headless) { Assert-Test (-not $script:process.HasExited) 'running application was not killed' }
    Assert-Test ((Get-FileHash -LiteralPath $settings).Hash -eq $before -and (Get-FileHash -LiteralPath (Join-Path $install 'TangerineRigControl.exe')).Hash -eq $exeHash) 'blocked operations leave executable and data unchanged'
    Stop-Fixture
    $guard = New-Object Threading.Mutex($false, ('Local\' + $name + '.SingleInstance'))
    try { Run-Setup (Join-Path $install 'TangerineRigControl.exe') @('--fixture-read') 2 } finally { $guard.Dispose() }
    $lease = [IO.File]::Open((Join-Path $legacyData '.lifecycle.lock'), 'Open', 'ReadWrite', 'None')
    try { Run-Setup (Join-Path $install 'TangerineRigControl.exe') @('--fixture-read') 3 } finally { $lease.Dispose() }
    $backup = (Get-FileHash -LiteralPath (Join-Path $data 'Backups/settings.xml.bak')).Hash
    Start-Fixture '--fixture-read'
    Stop-Fixture
    Run-Setup $NewSetup @('/S') 0
    Assert-Test ((Get-ItemProperty -LiteralPath $key).DisplayVersion -eq [Diagnostics.FileVersionInfo]::GetVersionInfo($NewSetup).FileVersion) 'upgrade registers new fixture version'
    Assert-Test ((Get-ItemProperty -LiteralPath $key).InstallLocation -eq $legacyInstall -and -not (Test-Path -LiteralPath $newInstall)) 'registered legacy installation is upgraded in place'
    Assert-Test ((Get-FileHash -LiteralPath $settings).Hash -eq $before) 'upgrade preserves XML byte for byte'
    Assert-Test ((Get-FileHash -LiteralPath (Join-Path $data 'Backups/settings.xml.bak')).Hash -eq $backup) 'upgrade preserves backup byte for byte'
    Run-Setup $OldSetup @('/S') 23
    Start-Fixture '--fixture-read'
    Stop-Fixture
    if (-not (Test-Path -LiteralPath $run)) { New-Item -Path $run | Out-Null }
    Set-ItemProperty -LiteralPath $run -Name $name -Value ('"' + (Join-Path $install 'TangerineRigControl.exe') + '" --startup')
    Set-Content -LiteralPath (Join-Path $install 'user-added.txt') -Value 'synthetic extra file'
    Run-Uninstall 0
    Assert-Test (-not (Test-Path -LiteralPath $key) -and -not (Test-Path -LiteralPath $shortcut)) 'uninstall removes registration and shortcut'
    Assert-Test ($null -eq (Get-ItemProperty -LiteralPath $run -Name $name -ErrorAction SilentlyContinue)) 'uninstall removes its own startup entry'
    Assert-Test ((Test-Path -LiteralPath (Join-Path $install 'user-added.txt')) -and (Get-FileHash -LiteralPath $settings).Hash -eq $before) 'uninstall retains user-added file and data'
    Assert-Test ((Get-FileHash -LiteralPath (Join-Path $data 'Backups/settings.xml.bak')).Hash -eq $backup) 'uninstall preserves backup byte for byte'
    $install = $newInstall
    Run-Setup $NewSetup @('/S') 0
    Assert-Test ((Get-ItemProperty -LiteralPath $key).InstallLocation -eq $newInstall) 'fresh install after legacy uninstall selects suite directory'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $exe = Join-Path $install 'TangerineRigControl.exe'
    Assert-Test ($link.TargetPath -eq $exe -and $link.Arguments -eq '' -and $link.WorkingDirectory -eq $install -and $link.IconLocation -eq ($exe + ',0')) 'shortcut launches the installed binary with its own icon and working directory'
    Start-Fixture '--fixture-read'
    Stop-Fixture
    $sourceBytes = [IO.File]::ReadAllBytes($originalSettings)
    Add-Content -LiteralPath $originalSettings -Value ' '
    try { Run-Setup (Join-Path $install 'TangerineRigControl.exe') @('--fixture-read-only') 4 }
    finally { [IO.File]::WriteAllBytes($originalSettings, $sourceBytes) }
    Assert-Test ((Get-FileHash -LiteralPath $settings).Hash -eq $before) 'old-version edit blocks the new app without changing new data'
    Set-ItemProperty -LiteralPath $run -Name $name -Value 'fixture-unrelated-entry'
    Run-Uninstall 0
    Assert-Test ((Get-ItemPropertyValue -LiteralPath $run -Name $name) -eq 'fixture-unrelated-entry') 'uninstall preserves unrelated startup command'
    Remove-ItemProperty -LiteralPath $run -Name $name
    # Only fixture-owned paths, verified against the exact expected identity, may be removed.
    Assert-Test (Test-Path -LiteralPath (Split-Path $newInstall)) 'shared suite parent survives uninstall'
    foreach ($owned in @($legacyInstall, $newInstall, $data, $legacyData)) {
        if (Test-Path -LiteralPath $owned) {
            $resolved = (Resolve-Path -LiteralPath $owned).Path
            $expected = [IO.Path]::GetFullPath($owned)
            if ($resolved -ne $expected -or [IO.Path]::GetFileName($resolved) -ne $name -or
                ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe fixture cleanup path.' }
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
    if ($Headless) { Write-Output 'Headless mode: runtime lock simulation; actual window/tray/running application assertions were not executed.' }
    Write-Output ('Installer fixture lifecycle passed: ' + $checks + ' checks. Production identity and clean Windows are not covered.')
} finally { Stop-Fixture }
