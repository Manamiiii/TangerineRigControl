# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Manamiiii

[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$ExecutablePath,
    [string[]]$Files
)

$ErrorActionPreference = 'Stop'
if (-not $RepositoryRoot) { $RepositoryRoot = Join-Path $PSScriptRoot '..' }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$failures = New-Object 'System.Collections.Generic.List[string]'
# Report only the rule and file, never the matched private value.
$patterns = [ordered]@{
    'absolute Windows path' = '(?<![A-Za-z0-9])[A-Za-z]:[\\/](?!/)[\p{L}\p{N}_. \\/-]{3,}'
    'personal home path' = '/(?:Users|home)/[^/\s"<>]+'
    'private key' = '-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY-----'
    'access token' = '(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}|AKIA[A-Z0-9]{16})'
}

function Test-PublicBytes([byte[]]$Bytes, [string]$Label) {
    # UTF-16 strings can be embedded in Windows executables and resources.
    $texts = @([Text.Encoding]::UTF8.GetString($Bytes), [Text.Encoding]::Unicode.GetString($Bytes))
    foreach ($rule in $patterns.Keys) {
        foreach ($text in $texts) {
            if ([regex]::IsMatch($text, $patterns[$rule])) {
                $failures.Add($Label + ': ' + $rule)
                break
            }
        }
    }
}

if (-not $Files) {
    $Files = @(& git -C $RepositoryRoot -c core.quotepath=false ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked files.' }
}
foreach ($file in $Files) {
    $name = [IO.Path]::GetFileName($file)
    $localOnly = $file -match '(^|/)(?:bin|obj|artifacts|captures|private|\.vs|\.idea)/' -or
        $name -match '^settings\.xml($|\.)|\.local\.xml$|\.(?:log|pdb|dmp|pfx|p12|key|user|suo)$' -or
        ($name -match '^\.env($|\.)' -and $name -ne '.env.example')
    if ($localOnly) { $failures.Add($file + ': local-only file is tracked') }
    $path = Join-Path $RepositoryRoot $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'A tracked file is missing from the checkout.' }
    Test-PublicBytes ([IO.File]::ReadAllBytes($path)) $file
}
if ($ExecutablePath) {
    $executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
    Test-PublicBytes ([IO.File]::ReadAllBytes($executable)) 'portable executable'
}
if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Output $failure }
    throw 'Public content check failed. Keep local data outside Git and rebuild Release binaries without debug paths.'
}
Write-Output ('Public content check passed: {0} files; executable checked: {1}.' -f $Files.Count, [bool]$ExecutablePath)
