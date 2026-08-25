[CmdletBinding()]
param(
    [PSCredential]$Credential,
    [string]$BackupRoot = 'C:\CP6Backups\CP6_DEV',
    [string]$EvidencePath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($null -eq $Credential) {
    $Credential = Get-Credential `
        -UserName 'cp6_dev_backup' `
        -Message 'Enter the dedicated CP6_DEV backup credential.'
}
if ($Credential.UserName -ne 'cp6_dev_backup') {
    throw "Only the dedicated 'cp6_dev_backup' account may export CP6_DEV snapshots."
}

$previousPassword = [Environment]::GetEnvironmentVariable(
    'CP6_DEV_DB_BACKUP_PASSWORD',
    'Process')
try {
    [Environment]::SetEnvironmentVariable(
        'CP6_DEV_DB_BACKUP_PASSWORD',
        $Credential.GetNetworkCredential().Password,
        'Process')
    & (Join-Path $PSScriptRoot 'Backup-Cp6DevDatabase.ps1') `
        -BackupRoot $BackupRoot `
        -EvidencePath $EvidencePath
    if ($LASTEXITCODE -ne 0) {
        throw 'CP6_DEV snapshot export failed.'
    }
}
finally {
    [Environment]::SetEnvironmentVariable(
        'CP6_DEV_DB_BACKUP_PASSWORD',
        $previousPassword,
        'Process')
}
