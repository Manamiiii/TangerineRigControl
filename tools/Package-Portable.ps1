# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Manamiiii

[CmdletBinding()]
param(
    [string]$ExecutablePath = 'src/TangerineRigControl/bin/Release/TangerineRigControl.exe',
    [string]$OutputDirectory = 'artifacts/packages'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not [IO.Path]::IsPathRooted($ExecutablePath)) { $ExecutablePath = Join-Path $repositoryRoot $ExecutablePath }
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory }
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($ExecutablePath)
$version = $versionInfo.ProductVersion
if ($versionInfo.LegalCopyright -notmatch 'GPL-3.0-only') { throw 'Rebuild the GPL-3.0-only executable before packaging.' }
if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^[a-zA-Z0-9._-]+$') { throw 'Invalid product version.' }
if ([IO.Path]::GetFileName($ExecutablePath) -ne 'TangerineRigControl.exe') { throw 'Expected TangerineRigControl.exe.' }

# Explicit allowlist: never copy an entire build directory, local settings or vendor DLLs.
$documents = @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.md', 'docs/USAGE.md', 'docs/DEVELOPMENT.md', 'docs/INSTALLER.md', 'docs/NIGHTLY-DEVICE-TEST.md', 'docs/images/main-window.png', 'packaging/windows/NSIS-LICENSE.txt')
$projectPath = 'src/TangerineRigControl/TangerineRigControl.csproj'
[xml]$project = Get-Content -LiteralPath (Join-Path $repositoryRoot $projectPath) -Raw
$sourceFiles = $documents + @('TangerineRigControl.sln', $projectPath, 'tools/Package-Portable.ps1', 'tools/Package-Windows.ps1', 'tools/Test-Installer.ps1', 'tools/Check-PublicContent.ps1', 'tools/Generate-AppIcon.ps1', 'packaging/windows/installer.nsi')
foreach ($item in $project.SelectNodes("//*[local-name()='Compile' or local-name()='EmbeddedResource' or local-name()='None']")) {
    $sourceFiles += Join-Path ([IO.Path]::GetDirectoryName($projectPath)) $item.Include
}
$sourceFiles = @($sourceFiles | Select-Object -Unique)
foreach ($sourceFile in $sourceFiles) {
    $fullPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $sourceFile))
    if (-not $fullPath.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('Source path must stay inside the repository: ' + $sourceFile)
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw ('Missing package source or document: ' + $sourceFile) }
}
& (Join-Path $PSScriptRoot 'Check-PublicContent.ps1') -RepositoryRoot $repositoryRoot -Files $sourceFiles -ExecutablePath $ExecutablePath
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$archive = Join-Path $OutputDirectory ('TangerineRigControl-' + $version + '-windows-portable.zip')
if ((Test-Path -LiteralPath $archive) -or (Test-Path -LiteralPath ($archive + '.sha256'))) { throw 'Package already exists; choose another output directory.' }
$staging = Join-Path $OutputDirectory ('staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    Copy-Item -LiteralPath $ExecutablePath -Destination (Join-Path $staging 'TangerineRigControl.exe')
    foreach ($document in $documents) {
        $destination = Join-Path $staging $document
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $document) -Destination $destination
    }
    foreach ($sourceFile in $sourceFiles) {
        $destination = Join-Path (Join-Path $staging 'source') $sourceFile
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $sourceFile) -Destination $destination
    }
    $revision = 'unknown'
    $workingTree = 'unknown'
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $sourceRevision = & git -C $repositoryRoot rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0) { $revision = $sourceRevision }
        $changes = & git -C $repositoryRoot status --porcelain --untracked-files=normal 2>$null
        if ($LASTEXITCODE -eq 0) { $workingTree = if ($changes) { 'modified (local preview)' } else { 'clean' } }
    }
    @(
        'TangerineRigControl ' + $version
        'Source checkout: ' + $revision
        'Working tree: ' + $workingTree
        'Packaged UTC: ' + [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        'The executable must be built from this checkout and pass --self-test before packaging.'
        'Hardware compatibility has not been certified by this packaging step.'
        'License: GPL-3.0-only. Copyright (c) 2026 Manamiiii. See LICENSE.'
        'Corresponding project source and build instructions are included in source/.'
    ) | Set-Content -LiteralPath (Join-Path $staging 'BUILD-INFO.txt') -Encoding UTF8
    $executableHash = (Get-FileHash -LiteralPath (Join-Path $staging 'TangerineRigControl.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    ($executableHash + '  TangerineRigControl.exe') | Set-Content -LiteralPath (Join-Path $staging 'SHA256SUMS.txt') -Encoding ASCII
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive -CompressionLevel Optimal
    $archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    ($archiveHash + '  ' + [IO.Path]::GetFileName($archive)) | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ASCII
    Write-Output $archive
    Write-Output ($archive + '.sha256')
} finally {
    # Delete only the unique staging directory just created beneath the output directory.
    $resolvedStaging = (Resolve-Path -LiteralPath $staging).Path
    $resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).Path.TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::GetDirectoryName($resolvedStaging) -ne $resolvedOutput -or [IO.Path]::GetFileName($resolvedStaging) -notmatch '^staging-[a-f0-9]{32}$') {
        throw 'Refusing to clean an unexpected staging path.'
    }
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}
