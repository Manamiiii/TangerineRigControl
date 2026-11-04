# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Manamiiii
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Setup,
    [Parameter(Mandatory = $true)][string]$ExecutablePath
)
$ErrorActionPreference = 'Stop'
# Never run this against a developer's real profile or a persistent self-hosted runner.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Production identity tests are restricted to disposable GitHub-hosted runners.'
}
$Setup = (Resolve-Path -LiteralPath $Setup).Path
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$name = 'TangerineRigControl'
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($Setup).ProductName -ne $name -or
    [Diagnostics.FileVersionInfo]::GetVersionInfo($ExecutablePath).ProductName -ne $name) { throw 'Expected production identity.' }
$install = Join-Path $env:LOCALAPPDATA ('Programs/Tangerine/' + $name)
$data = Join-Path $env:LOCALAPPDATA $name
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) ($name + '.lnk')
$key = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{D9BC8BD8-3A19-4798-A233-18C4B5B62BE4}'
$run = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Run'
foreach ($path in @($install, (Join-Path $env:LOCALAPPDATA ('Programs/' + $name)), $data, $shortcut, $key)) {
    if (Test-Path -LiteralPath $path) { throw 'Existing production environment detected. Nothing was replaced.' }
}
if ($null -ne (Get-ItemProperty -LiteralPath $run -Name $name -ErrorAction SilentlyContinue)) { throw 'Existing startup entry detected.' }
if (@(Get-Process TangerineRigControl -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Existing application process detected.' }
$existing = $null
if ([Threading.Mutex]::TryOpenExisting(('Local\' + $name + '.SingleInstance'), [ref]$existing)) {
    $existing.Dispose(); throw 'Existing application/installer mutex detected.'
}
$checks = 0
function Assert-Test([bool]$condition, [string]$label) {
    if (-not $condition) { throw ('FAIL ' + $label) }
    $script:checks++
    Write-Output ('PASS production: ' + $label)
}
function Run-Uninstall([int]$expected) {
    # As in a normal uninstall, execute a temporary copy so the installed copy can be removed.
    $copy = Join-Path ([IO.Path]::GetTempPath()) ('rig-uninstall-' + [Guid]::NewGuid().ToString('N') + '.exe')
    Copy-Item -LiteralPath (Join-Path $install 'Uninstall.exe') -Destination $copy
    try { Run-Checked $copy @('/S', ('_?=' + $install)) $expected }
    finally { Remove-Item -LiteralPath $copy }
}
function Run-Checked([string]$path, [string[]]$arguments, [int]$expected) {
    $p = Start-Process -FilePath $path -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(60000)) { throw 'Process timeout; no process was terminated.' }
    $p.WaitForExit()
    Assert-Test ($p.ExitCode -eq $expected) ('exit code ' + $expected)
}
New-Item -ItemType Directory -Path $data | Out-Null
$settings = Join-Path $data 'settings.xml'
$backup = $settings + '.bak'
# Only synthetic configuration. Tests do not start the normal app or any vendor software.
'<RigSettings><SchemaVersion>1</SchemaVersion><UseAuraSdk>false</UseAuraSdk><LConnect><InitialDelayMilliseconds>777</InitialDelayMilliseconds></LConnect></RigSettings>' |
    Set-Content -LiteralPath $settings -Encoding UTF8
Copy-Item -LiteralPath $settings -Destination $backup
$dataHash = (Get-FileHash -LiteralPath $settings).Hash
$binaryHash = (Get-FileHash -LiteralPath $ExecutablePath).Hash
Run-Checked $Setup @('/S') 0
Assert-Test ((Get-ItemProperty -LiteralPath $key).InstallLocation -eq $install -and (Test-Path -LiteralPath $shortcut)) 'per-user registration and Start menu shortcut'
Assert-Test ((Get-ItemProperty -LiteralPath $key).DisplayVersion -eq [Diagnostics.FileVersionInfo]::GetVersionInfo($Setup).FileVersion) 'candidate version registered'
Assert-Test ((Get-FileHash -LiteralPath (Join-Path $install 'TangerineRigControl.exe')).Hash -eq $binaryHash) 'installed executable matches tested production binary'
Assert-Test ($null -eq (Get-ItemProperty -LiteralPath $run -Name $name -ErrorAction SilentlyContinue)) 'installation does not enable startup'
$exe = Join-Path $install 'TangerineRigControl.exe'
$link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
Assert-Test ($link.TargetPath -eq $exe -and $link.IconLocation -eq ($exe + ',0') -and $link.WorkingDirectory -eq $install -and $link.Arguments -eq '') 'production shortcut target, icon and launch context'
python (Join-Path $PSScriptRoot 'verify_windows_icons.py') $Setup $exe (Join-Path $install 'Uninstall.exe')
if ($LASTEXITCODE -ne 0) { throw 'Embedded icon verification failed' }
Run-Checked $exe @('--self-test') 0
$guard = New-Object Threading.Mutex($false, ('Local\' + $name + '.SingleInstance'))
try {
    Run-Checked $Setup @('/S') 20
    Run-Uninstall 20
} finally { $guard.Dispose() }
$lease = [IO.File]::Open((Join-Path $data '.lifecycle.lock'), 'Open', 'ReadWrite', 'None')
try { Run-Checked $Setup @('/S') 20 } finally { $lease.Dispose() }
Run-Checked $Setup @('/S') 0
Assert-Test ((Get-FileHash -LiteralPath $settings).Hash -eq $dataHash -and (Get-FileHash -LiteralPath $backup).Hash -eq $dataHash) 'same-version reinstall preserves synthetic settings and backup'
if (-not (Test-Path -LiteralPath $run)) { New-Item -Path $run | Out-Null }
Set-ItemProperty -LiteralPath $run -Name $name -Value ('"' + (Join-Path $install 'TangerineRigControl.exe') + '" --startup')
Run-Uninstall 0
Assert-Test (-not (Test-Path -LiteralPath $key) -and -not (Test-Path -LiteralPath $shortcut) -and -not (Test-Path -LiteralPath (Join-Path $install 'TangerineRigControl.exe'))) 'uninstall removes app, registration and shortcut'
Assert-Test ($null -eq (Get-ItemProperty -LiteralPath $run -Name $name -ErrorAction SilentlyContinue)) 'uninstall removes only its own startup command'
Assert-Test ((Get-FileHash -LiteralPath $settings).Hash -eq $dataHash -and (Get-FileHash -LiteralPath $backup).Hash -eq $dataHash) 'uninstall preserves synthetic settings and backup'
Run-Checked $Setup @('/S') 0
Assert-Test ((Get-FileHash -LiteralPath $settings).Hash -eq $dataHash) 'reinstall retains synthetic data'
Set-ItemProperty -LiteralPath $run -Name $name -Value 'synthetic-other-portable-command'
Run-Uninstall 0
Assert-Test ((Get-ItemPropertyValue -LiteralPath $run -Name $name) -eq 'synthetic-other-portable-command') 'uninstall preserves another portable startup command'
Remove-ItemProperty -LiteralPath $run -Name $name
Write-Output ('Production installer passed: ' + $checks + ' checks. Disposable CI profile; no normal desktop launch, hardware or clean consumer Windows claim.')
# The runner is discarded. Deliberately avoid recursive deletion of a production-named directory.
