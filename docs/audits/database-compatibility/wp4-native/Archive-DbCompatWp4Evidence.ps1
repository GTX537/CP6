#requires -Version 7.2
[CmdletBinding()]
param(
    [string[]]$ExplicitAdditionalPaths = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Execute only after the root runner has stopped producing evidence and has
# checked the selected public originals for credential values. This script does
# not inspect receipts, load configuration, invoke processes, or access a DB.
$taskTmpRoot = [IO.Path]::GetFullPath('D:\CP6\tmp').TrimEnd('\')
$taskWorktreeRoot = [IO.Path]::GetFullPath('D:\CP6\tmp\worktrees\db-compat-wp4-20261003').TrimEnd('\')
$archiveRoot = Join-Path $taskWorktreeRoot 'docs\audits\database-compatibility\wp4-native'
$testsRoot = Join-Path $taskTmpRoot 'db-compat-wp4-tests'
$manifestPath = Join-Path $archiveRoot 'manifest.json'
$selection = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)

function Test-PathWithin([string]$Path, [string]$Parent) {
    return $Path.StartsWith($Parent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
}

function Test-ForbiddenSourceName([string]$FullPath) {
    $relative = [IO.Path]::GetRelativePath($taskTmpRoot, $FullPath).Replace('\', '/')
    $leaf = [IO.Path]::GetFileName($FullPath)
    return ($relative -match '(?i)(^|/)(private-diagnostics|private[^/]*|credentials?|secrets?)(/|$)' -or
        $leaf -match '(?i)(^|[._-])(private|receipt|credentials?|passwords?|secrets?|connectionstrings?)([._-]|$)' -or
        $leaf -match '(?i)^db-compat(?:[._-].*)?\.json$' -or
        $leaf -match '(?i)(\.local\.|^appsettings(?:\..*)?\.json$|^\.env(?:\..*)?$|^\.?pgpass(?:\.conf)?$|^pg_service\.conf$|^seed-state(?:[._-]|$))')
}

function Assert-NoReparsePoint([string]$Path, [string]$StopAt) {
    $cursor = $Path
    while ($true) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Evidence paths may not follow reparse points: $cursor"
            }
        }
        if ($cursor.Equals($StopAt, [StringComparison]::OrdinalIgnoreCase)) { break }
        $parent = [IO.Directory]::GetParent($cursor)
        if ($null -eq $parent) { throw 'Evidence path did not remain within its declared root.' }
        $cursor = $parent.FullName
    }
}

function Add-EvidenceSource([string]$SourcePath, [string]$Origin) {
    $full = [IO.Path]::GetFullPath($SourcePath)
    if (!(Test-PathWithin $full $taskTmpRoot) -or (Test-PathWithin $full $archiveRoot)) {
        throw "Evidence source must be a local tmp original outside the target: $full"
    }
    # Reject by path/name before opening the source, including explicit additions.
    if (Test-ForbiddenSourceName $full) { throw "Private/configuration/receipt source is forbidden: $full" }
    if (!(Test-Path -LiteralPath $full -PathType Leaf)) { throw "Required original is missing: $full" }
    Assert-NoReparsePoint $full $taskTmpRoot
    $relative = [IO.Path]::GetRelativePath($taskTmpRoot, $full).Replace('\', '/')
    if ($relative.Equals('manifest.json', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The archive manifest name is reserved.'
    }
    if (!$selection.ContainsKey($full)) {
        $selection.Add($full, [pscustomobject]@{
            SourcePath = $full
            RelativePath = $relative
            Origin = $Origin
        })
    }
}

function Get-ByteIdentity([string]$Path) {
    # FileShare.Read rejects originals still held open for writing. A changing
    # source or partial output is a hard failure, never silently excluded.
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        return [pscustomobject]@{
            Sha256 = [Convert]::ToHexString($hasher.ComputeHash($stream))
            Length = $stream.Length
        }
    }
    finally { $hasher.Dispose(); $stream.Dispose() }
}

if (!(Test-Path -LiteralPath $taskWorktreeRoot -PathType Container)) {
    throw 'The WP4 task worktree must exist before archiving.'
}
Assert-NoReparsePoint $archiveRoot $taskTmpRoot
if (Test-Path -LiteralPath $archiveRoot) {
    if (!(Test-Path -LiteralPath $archiveRoot -PathType Container) -or
        @(Get-ChildItem -LiteralPath $archiveRoot -Force).Count -ne 0) {
        throw 'The archive target already contains evidence; no file may be overwritten or deleted.'
    }
}

# The automatic set covers public validation inputs/outputs, including failures.
# Creation summaries and final cleanup/review/delivery proofs require an explicit
# root-selected addition; no MD draft is automatically discovered.
$explicitOnlyNames = '(?i)(^|[._-])(cleanup|review|delivery|closure|postmerge|remote|archive)([._-]|$)'
foreach ($file in Get-ChildItem -LiteralPath $taskTmpRoot -File -Force) {
    if ($file.Name -notmatch '(?i)^wp4-.+\.(json|log)$') { continue }
    if (Test-ForbiddenSourceName $file.FullName) { continue }
    if ($file.Name -match $explicitOnlyNames -or $file.Name -match '(?i)^wp4-(?:erp-|business-)?databases-created\.json$') { continue }
    Add-EvidenceSource $file.FullName 'PublicTopLevel'
}

if (!(Test-Path -LiteralPath $testsRoot -PathType Container)) { throw 'The WP4 tests evidence root is missing.' }
Assert-NoReparsePoint $testsRoot $taskTmpRoot
$pendingDirectories = [Collections.Generic.Stack[string]]::new()
$pendingDirectories.Push($testsRoot)
while ($pendingDirectories.Count -gt 0) {
    $directory = $pendingDirectories.Pop()
    foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
        if (Test-ForbiddenSourceName $item.FullName) { continue }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "WP4 evidence may not follow a reparse point: $($item.FullName)"
        }
        if ($item.PSIsContainer) { $pendingDirectories.Push($item.FullName); continue }
        $relative = [IO.Path]::GetRelativePath($testsRoot, $item.FullName).Replace('\', '/')
        if ($relative -match '(?i)(^|/)(process\.log|results\.trx)$' -or
            $relative -match '(?i)(^|/)evidence/(summary\.json|junit\.xml|[^/]+-proof\.json)$') {
            Add-EvidenceSource $item.FullName 'NativeRunOriginal'
        }
    }
}

foreach ($name in @(
    'Invoke-DbCompatWp4Case.ps1',
    'Invoke-DbCompatWp4Case.first-red.ps1',
    'Invoke-DbCompatWp4Case.before-identity-http.ps1',
    'New-DbCompatWp4Databases.ps1',
    'New-DbCompatWp4ErpDatabases.ps1',
    'New-DbCompatWp4BusinessDatabases.ps1'
)) { Add-EvidenceSource (Join-Path $taskTmpRoot $name) 'RequiredRunnerOrCreationScript' }

foreach ($additional in $ExplicitAdditionalPaths) {
    if ([string]::IsNullOrWhiteSpace($additional) -or ![IO.Path]::IsPathFullyQualified($additional)) {
        throw 'ExplicitAdditionalPaths must contain exact absolute filenames, not wildcards or directories.'
    }
    Add-EvidenceSource $additional 'RootExplicitAdditionalPath'
}

$plan = @($selection.Values | Sort-Object RelativePath)
if ($plan.Count -eq 0) { throw 'The selected evidence set is empty.' }
$snapshots = @(foreach ($entry in $plan) {
    $identity = Get-ByteIdentity $entry.SourcePath
    [pscustomobject]@{
        SourcePath = $entry.SourcePath
        RelativePath = $entry.RelativePath
        Sha256 = $identity.Sha256
        Length = $identity.Length
        Origin = $entry.Origin
    }
})

# Recheck immediately before the first mutation. All file creation is CreateNew;
# on any failure partial originals stay visible for inspection, with no cleanup.
if (Test-Path -LiteralPath $archiveRoot) {
    if (@(Get-ChildItem -LiteralPath $archiveRoot -Force).Count -ne 0) { throw 'The archive target became nonempty.' }
}
[IO.Directory]::CreateDirectory($archiveRoot) | Out-Null
foreach ($entry in $snapshots) {
    $destination = [IO.Path]::GetFullPath((Join-Path $archiveRoot $entry.RelativePath.Replace('/', '\')))
    if (!(Test-PathWithin $destination $archiveRoot)) { throw 'An archive destination escaped the WP4 target.' }
    $source = [IO.File]::Open($entry.SourcePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        $output = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $source.CopyTo($output) }
        finally { $output.Dispose() }
        $copied = Get-ByteIdentity $destination
        $source.Position = 0
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $stillSourceSha = [Convert]::ToHexString($hasher.ComputeHash($source)) }
        finally { $hasher.Dispose() }
        if ($copied.Sha256 -ne $entry.Sha256 -or $copied.Length -ne $entry.Length -or
            $stillSourceSha -ne $entry.Sha256 -or $source.Length -ne $entry.Length) {
            throw "Source changed or byte verification failed: $($entry.RelativePath)"
        }
    }
    finally { $source.Dispose() }
}

$manifest = [ordered]@{
    Task = 'DB-COMPAT-01-WP4'
    ArchivedUtc = [DateTime]::UtcNow.ToString('O')
    ArchiveRoot = $archiveRoot
    FileCount = $snapshots.Count
    TotalOriginalBytes = ($snapshots | Measure-Object -Property Length -Sum).Sum
    Scope = 'Byte-exact public local originals, including failures; no execution or acceptance is inferred from archiving.'
    Files = @($snapshots)
}
$manifestBytes = [Text.UTF8Encoding]::new($false).GetBytes(($manifest | ConvertTo-Json -Depth 7) + "`n")
$manifestOutput = [IO.File]::Open($manifestPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try { $manifestOutput.Write($manifestBytes, 0, $manifestBytes.Length) }
finally { $manifestOutput.Dispose() }
$manifestIdentity = Get-ByteIdentity $manifestPath
[pscustomobject]@{
    Status = 'ArchivedAndByteVerified'
    FileCount = $snapshots.Count
    TotalOriginalBytes = $manifest.TotalOriginalBytes
    ManifestPath = $manifestPath
    ManifestSha256 = $manifestIdentity.Sha256
    ManifestLength = $manifestIdentity.Length
} | ConvertTo-Json -Depth 3
