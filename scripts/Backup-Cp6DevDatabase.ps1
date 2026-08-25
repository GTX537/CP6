[CmdletBinding()]
param(
    [ValidateSet('CP6_DEV')]
    [string]$Database = 'CP6_DEV',

    [string]$ServerInstance = 'localhost\KOUSQLSERVER',

    [ValidateSet('cp6_dev_backup')]
    [string]$BackupAccount = 'cp6_dev_backup',

    [string]$PasswordEnvironmentVariable = 'CP6_DEV_DB_BACKUP_PASSWORD',

    [string]$BackupRoot = 'C:\CP6Backups\CP6_DEV',

    [string]$EvidencePath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd was not found. Install Microsoft sqlcmd on the dedicated deployment agent.'
}

$password = [Environment]::GetEnvironmentVariable($PasswordEnvironmentVariable, 'Process')
if ([string]::IsNullOrWhiteSpace($password)) {
    throw "Required backup Secret '$PasswordEnvironmentVariable' is missing."
}

$resolvedBackupRoot = [IO.Path]::GetFullPath($BackupRoot)
$pathRoot = [IO.Path]::GetPathRoot($resolvedBackupRoot)
if (-not [IO.Path]::IsPathRooted($resolvedBackupRoot) -or
    $resolvedBackupRoot.Equals($pathRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'BackupRoot must be a dedicated absolute directory, not a drive root.'
}
[IO.Directory]::CreateDirectory($resolvedBackupRoot) | Out-Null

$timestamp = [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss_fff')
$uniqueSuffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$backupPath = Join-Path $resolvedBackupRoot "CP6_DEV_${timestamp}_${uniqueSuffix}_UTC.bak"
$escapedBackupPath = $backupPath.Replace("'", "''")
$query = @"
SET NOCOUNT ON;
IF DB_ID(N'CP6_DEV') IS NULL THROW 51000, 'CP6_DEV does not exist.', 1;
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'CP6_DEV' AND state_desc <> N'ONLINE')
    THROW 51001, 'CP6_DEV is not ONLINE.', 1;
BACKUP DATABASE [CP6_DEV]
    TO DISK = N'$escapedBackupPath'
    WITH COPY_ONLY, INIT, COMPRESSION, CHECKSUM, STATS = 10;
RESTORE VERIFYONLY
    FROM DISK = N'$escapedBackupPath'
    WITH CHECKSUM;
"@

$previousSqlCmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
try {
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $password, 'Process')
    & sqlcmd `
        -S $ServerInstance `
        -U $BackupAccount `
        -C `
        -b `
        -V 16 `
        -Q $query
    if ($LASTEXITCODE -ne 0) {
        throw "CP6_DEV backup or RESTORE VERIFYONLY failed with sqlcmd exit code $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $previousSqlCmdPassword, 'Process')
}

if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
    throw "SQL Server reported success but the backup file is not readable at '$backupPath'."
}

$backupFile = Get-Item -LiteralPath $backupPath
$hash = Get-FileHash -LiteralPath $backupPath -Algorithm SHA256
$evidence = [ordered]@{
    schemaVersion = 1
    database = 'CP6_DEV'
    createdAtUtc = [DateTime]::UtcNow.ToString('o')
    backupPath = $backupFile.FullName
    lengthBytes = $backupFile.Length
    sha256 = $hash.Hash.ToLowerInvariant()
    checksum = 'BACKUP CHECKSUM'
    verifyOnly = 'passed'
}

if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $resolvedEvidencePath = [IO.Path]::GetFullPath($EvidencePath)
    [IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedEvidencePath)) | Out-Null
    $evidence | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $resolvedEvidencePath -Encoding utf8
}

[pscustomobject]$evidence
