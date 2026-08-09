[CmdletBinding()]
param(
    [ValidateSet('Inspect', 'Drill', 'Cleanup')]
    [string]$Mode = 'Drill',
    [string]$BackupPath,
    [string]$EvidencePath,
    [string]$StatePath,
    [switch]$RunFullVerification,
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Import-Module (Join-Path $PSScriptRoot 'OaP0StagingDrill.Common.psm1') -Force

$containerName = 'cp6-db'
$script:connectionString = $null
$script:saPassword = $null

function Protect-OaP0Text {
    param([AllowNull()][object]$Value)

    $text = [string]$Value
    if (-not [string]::IsNullOrEmpty($script:connectionString)) {
        $text = $text.Replace($script:connectionString, '<redacted-connection>')
    }
    if (-not [string]::IsNullOrEmpty($script:saPassword)) {
        $text = $text.Replace($script:saPassword, '<redacted-secret>')
    }
    return $text
}

function Write-OaP0JsonFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object]$Value
    )

    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $json = $Value | ConvertTo-Json -Depth 16
    [System.IO.File]::WriteAllText(
        $Path,
        $json,
        [System.Text.UTF8Encoding]::new($false))
}

function Resolve-OaP0OutputPath {
    param(
        [AllowNull()][string]$RequestedPath,
        [Parameter(Mandatory)][string]$DefaultRelativePath
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($RequestedPath)) {
        Join-Path $repoRoot $DefaultRelativePath
    }
    elseif ([System.IO.Path]::IsPathRooted($RequestedPath)) {
        $RequestedPath
    }
    else {
        Join-Path $repoRoot $RequestedPath
    }
    return [System.IO.Path]::GetFullPath($candidate)
}

function Invoke-OaP0ContainerSql {
    param(
        [Parameter(Mandatory)][string]$Query,
        [string]$Database = 'master',
        [switch]$Headers
    )

    if ($Database -cne 'master' -and -not (Test-OaP0StageDatabaseName $Database)) {
        throw "Refusing sqlcmd against unsafe database name '$Database'."
    }

    $headerArgs = if ($Headers) { '-W' } else { '-h -1 -W' }
    $shellCommand =
        'exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa ' +
        '-P "$MSSQL_SA_PASSWORD" -C -b -r 1 -w 65535 -s ^ ' +
        "$headerArgs -d $Database"
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = @($Query | & docker exec -i $containerName sh -c $shellCommand 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
    $safe = @($raw | ForEach-Object { Protect-OaP0Text $_ })
    if ($exitCode -ne 0) {
        throw "sqlcmd failed for database '$Database': $($safe -join [Environment]::NewLine)"
    }
    return @($safe)
}

function Get-OaP0SqlScalar {
    param(
        [Parameter(Mandatory)][string]$Query,
        [string]$Database = 'master',
        [switch]$AllowEmpty
    )

    $lines = @(
        Invoke-OaP0ContainerSql -Query $Query -Database $Database |
            ForEach-Object { ([string]$_).Trim() } |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                $_ -notmatch '^\([0-9]+ rows? affected\)$' -and
                $_ -notmatch '^Changed database context to '
            }
    )
    if ($lines.Count -eq 0 -and $AllowEmpty) { return $null }
    if ($lines.Count -ne 1) {
        throw "Expected one scalar value from '$Database' but received $($lines.Count)."
    }
    return $lines[0]
}

function Quote-OaP0SqlLiteral {
    param([Parameter(Mandatory)][string]$Value)
    return $Value.Replace("'", "''")
}

function Test-OaP0DatabaseExists {
    param([Parameter(Mandatory)][string]$DatabaseName)

    if (-not (Test-OaP0StageDatabaseName $DatabaseName)) {
        throw "Unsafe database existence check was blocked: '$DatabaseName'."
    }
    $literal = Quote-OaP0SqlLiteral $DatabaseName
    return [int](Get-OaP0SqlScalar `
        -Query "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$literal') IS NULL THEN 0 ELSE 1 END;") -eq 1
}

function Test-OaP0ContainerPathExists {
    param([Parameter(Mandatory)][string]$ContainerPath)

    $null = & docker exec $containerName test -e $ContainerPath 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -eq 0) { return $true }
    if ($exitCode -eq 1) { return $false }
    throw "Could not inspect the scoped container path '$ContainerPath'."
}

function Remove-OaP0CopiedBackup {
    param([Parameter(Mandatory)][string]$ContainerPath)

    if (-not (Test-OaP0ContainerBackupPath $ContainerPath)) {
        throw "Refusing to remove unsafe container backup path '$ContainerPath'."
    }
    if (-not (Test-OaP0ContainerPathExists $ContainerPath)) {
        return [pscustomobject]@{ removed = $false; verifiedAbsent = $true }
    }

    $output = @(& docker exec $containerName rm -f -- $ContainerPath 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to remove scoped copied backup: $((@($output | ForEach-Object { Protect-OaP0Text $_ })) -join ' ')"
    }
    $absent = -not (Test-OaP0ContainerPathExists $ContainerPath)
    if (-not $absent) {
        throw 'Scoped copied backup still exists after cleanup.'
    }
    return [pscustomobject]@{ removed = $true; verifiedAbsent = $true }
}

function Get-OaP0DatabaseMarker {
    param(
        [Parameter(Mandatory)][string]$DatabaseName
    )

    $query = @'
SET NOCOUNT ON;
SELECT CONVERT(nvarchar(64), [value])
FROM sys.extended_properties
WHERE class = 0 AND [name] = N'OaP0StageRunId';
'@
    return Get-OaP0SqlScalar -Query $query -Database $DatabaseName -AllowEmpty
}

function Remove-OaP0StageDatabase {
    param(
        [Parameter(Mandatory)][object]$State,
        [switch]$CreatedByThisInvocation
    )

    Assert-OaP0CleanupState -State $State
    $databaseName = [string]$State.databaseName
    if (-not (Test-OaP0DatabaseExists $databaseName)) {
        return [pscustomobject]@{
            dropped = $false
            singleUserUsed = $false
            verifiedAbsent = $true
        }
    }

    $marker = Get-OaP0DatabaseMarker -DatabaseName $databaseName
    $markerMatches = -not [string]::IsNullOrWhiteSpace($marker) -and
        $marker -ceq [string]$State.runId
    if (-not $markerMatches -and -not $CreatedByThisInvocation) {
        throw "Database '$databaseName' did not carry this run's cleanup marker."
    }
    if (-not $markerMatches -and $CreatedByThisInvocation -and
        -not [bool]$State.restoreAttempted) {
        throw "Database '$databaseName' cannot be proven to have been created by this invocation."
    }

    $literal = Quote-OaP0SqlLiteral $databaseName
    $activeSessions = [int](Get-OaP0SqlScalar -Query @"
SET NOCOUNT ON;
SELECT COUNT(*)
FROM sys.dm_exec_sessions
WHERE database_id = DB_ID(N'$literal');
"@)
    $singleUserUsed = $activeSessions -gt 0
    $dropSql = if ($singleUserUsed) {
        "ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$databaseName];"
    }
    else {
        "DROP DATABASE [$databaseName];"
    }

    try {
        $null = Invoke-OaP0ContainerSql -Query $dropSql
    }
    catch {
        if ($singleUserUsed) { throw }
        $singleUserUsed = $true
        $null = Invoke-OaP0ContainerSql -Query (
            "ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            "DROP DATABASE [$databaseName];")
    }

    $verifiedAbsent = -not (Test-OaP0DatabaseExists $databaseName)
    if (-not $verifiedAbsent) {
        throw "Database '$databaseName' still exists after exact-name cleanup."
    }
    return [pscustomobject]@{
        dropped = $true
        singleUserUsed = $singleUserUsed
        verifiedAbsent = $true
    }
}

function Get-OaP0BackupMetadata {
    param([Parameter(Mandatory)][string]$ContainerBackupPath)

    if (-not (Test-OaP0ContainerBackupPath $ContainerBackupPath)) {
        throw 'Backup metadata inspection received an unsafe copied-backup path.'
    }
    $literal = Quote-OaP0SqlLiteral $ContainerBackupPath
    $headerLines = Invoke-OaP0ContainerSql `
        -Query "RESTORE HEADERONLY FROM DISK = N'$literal';" -Headers
    $headers = @(ConvertFrom-OaP0SqlCmdTable -Lines $headerLines)
    $fullBackups = @(
        $headers |
            Where-Object { [int]$_.BackupType -eq 1 } |
            Sort-Object { [datetime]::Parse([string]$_.BackupFinishDate) } -Descending
    )
    if ($fullBackups.Count -eq 0) {
        throw 'Backup file contained no full database backup set.'
    }
    $selected = $fullBackups[0]
    $position = [int]$selected.Position

    $null = Invoke-OaP0ContainerSql `
        -Query "RESTORE VERIFYONLY FROM DISK = N'$literal' WITH FILE = $position;"
    $fileLines = Invoke-OaP0ContainerSql `
        -Query "RESTORE FILELISTONLY FROM DISK = N'$literal' WITH FILE = $position;" -Headers
    $logicalFiles = @(ConvertFrom-OaP0SqlCmdTable -Lines $fileLines)
    if ($logicalFiles.Count -eq 0) {
        throw 'Backup file list was empty.'
    }
    $unsupported = @($logicalFiles | Where-Object { $_.Type -notin @('D', 'L') })
    if ($unsupported.Count -gt 0) {
        throw 'Backup contains a logical file type outside the supported data/log set.'
    }

    return [pscustomobject]@{
        Position = $position
        BackupFinishTime = ([datetime]::Parse([string]$selected.BackupFinishDate)).ToUniversalTime()
        OriginalDatabaseName = [string]$selected.DatabaseName
        LogicalFiles = $logicalFiles
        DataFileCount = @($logicalFiles | Where-Object { $_.Type -eq 'D' }).Count
        LogFileCount = @($logicalFiles | Where-Object { $_.Type -eq 'L' }).Count
    }
}

function Restore-OaP0StageDatabase {
    param(
        [Parameter(Mandatory)][object]$State,
        [Parameter(Mandatory)][object]$BackupMetadata
    )

    Assert-OaP0CleanupState -State $State
    $databaseName = [string]$State.databaseName
    if (Test-OaP0DatabaseExists $databaseName) {
        throw "Generated database '$databaseName' unexpectedly already exists."
    }

    $moves = [System.Collections.Generic.List[string]]::new()
    $dataIndex = 0
    $logIndex = 0
    foreach ($logicalFile in @($BackupMetadata.LogicalFiles)) {
        $logicalName = Quote-OaP0SqlLiteral ([string]$logicalFile.LogicalName)
        if ([string]$logicalFile.Type -eq 'D') {
            $dataIndex++
            $extension = if ($dataIndex -eq 1) { 'mdf' } else { 'ndf' }
            $destination = "/var/opt/mssql/data/${databaseName}_data${dataIndex}.${extension}"
        }
        else {
            $logIndex++
            $destination = "/var/opt/mssql/data/${databaseName}_log${logIndex}.ldf"
        }
        if (Test-OaP0ContainerPathExists $destination) {
            throw "Generated restore destination already exists: $destination"
        }
        $destinationLiteral = Quote-OaP0SqlLiteral $destination
        $moves.Add("MOVE N'$logicalName' TO N'$destinationLiteral'")
    }

    $backupLiteral = Quote-OaP0SqlLiteral ([string]$State.containerBackupPath)
    $restoreSql = @"
SET NOCOUNT ON;
IF DB_ID(N'$databaseName') IS NOT NULL
    THROW 51000, 'Generated isolated database already exists.', 1;
RESTORE DATABASE [$databaseName]
FROM DISK = N'$backupLiteral'
WITH FILE = $([int]$BackupMetadata.Position),
     $($moves -join ",`n     "),
     RECOVERY,
     STATS = 10;
"@
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $null = Invoke-OaP0ContainerSql -Query $restoreSql
    $watch.Stop()
    if (-not (Test-OaP0DatabaseExists $databaseName)) {
        throw "Restore completed without creating '$databaseName'."
    }

    $runLiteral = Quote-OaP0SqlLiteral ([string]$State.runId)
    $null = Invoke-OaP0ContainerSql -Database $databaseName -Query @"
EXEC sys.sp_addextendedproperty
    @name = N'OaP0StageRunId',
    @value = N'$runLiteral';
"@
    $marker = Get-OaP0DatabaseMarker -DatabaseName $databaseName
    if ($marker -cne [string]$State.runId) {
        throw "Restore marker verification failed for '$databaseName'."
    }
    return $watch.ElapsedMilliseconds
}

function Get-OaP0DatabaseInventory {
    param([Parameter(Mandatory)][string]$DatabaseName)

    $migrationHead = Get-OaP0SqlScalar -Database $DatabaseName -Query @'
SET NOCOUNT ON;
DECLARE @head nvarchar(150) = NULL;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql
        N'SELECT @value = MAX(MigrationId) FROM dbo.__EFMigrationsHistory;',
        N'@value nvarchar(150) OUTPUT',
        @value = @head OUTPUT;
END;
SELECT COALESCE(@head, N'<none>');
'@
    if ($migrationHead -eq '<none>') { $migrationHead = $null }

    $tableLines = Invoke-OaP0ContainerSql -Database $DatabaseName -Query @'
SET NOCOUNT ON;
SELECT t.[name],
       CONVERT(bigint, SUM(CASE WHEN p.index_id IN (0, 1) THEN p.[rows] ELSE 0 END))
FROM sys.tables AS t
LEFT JOIN sys.partitions AS p ON p.object_id = t.object_id
WHERE t.[name] LIKE N'Wf[_]%' OR t.[name] LIKE N'Pur[_]%'
GROUP BY t.[name]
ORDER BY t.[name];
'@
    $rowsByTable = @{}
    foreach ($line in @($tableLines)) {
        $text = ([string]$line).Trim()
        if ([string]::IsNullOrWhiteSpace($text) -or $text -notmatch '\^') { continue }
        $parts = @($text -split '\^' | ForEach-Object { $_.Trim() })
        if ($parts.Count -ne 2 -or $parts[1] -notmatch '^[0-9]+$') {
            throw "Unexpected count-only inventory row: '$text'."
        }
        $rowsByTable[$parts[0]] = [long]$parts[1]
    }

    function Read-Count([string]$Name) {
        if ($rowsByTable.ContainsKey($Name)) { return [long]$rowsByTable[$Name] }
        return [long]0
    }

    $wfNames = @($rowsByTable.Keys | Where-Object { $_ -like 'Wf_*' })
    $purNames = @($rowsByTable.Keys | Where-Object { $_ -like 'Pur_*' })
    $wfRows = [long](($wfNames | ForEach-Object { $rowsByTable[$_] } | Measure-Object -Sum).Sum)
    $purRows = [long](($purNames | ForEach-Object { $rowsByTable[$_] } | Measure-Object -Sum).Sum)
    $critical = [ordered]@{
        flowDefs = Read-Count 'Wf_FlowDef'
        formDefs = Read-Count 'Wf_FormDef'
        flowInstances = Read-Count 'Wf_FlowInstance'
        flowTasks = Read-Count 'Wf_FlowTask'
        flowHistories = Read-Count 'Wf_FlowHistory'
        formData = Read-Count 'Wf_FormData'
        approvalBindings = Read-Count 'Wf_ApprovalBinding'
        purchaseRequests = Read-Count 'Pur_PurchaseRequest'
        purchaseRequestLines = Read-Count 'Pur_PurchaseRequestLine'
    }
    $hasWfDefinitions = $critical.flowDefs -gt 0
    $hasOaRuntime = ($critical.flowInstances + $critical.flowTasks +
        $critical.flowHistories + $critical.formData) -gt 0
    $hasPurData = ($critical.purchaseRequests + $critical.purchaseRequestLines) -gt 0

    return [pscustomobject]@{
        migrationHead = $migrationHead
        aggregate = [pscustomobject]@{
            wfTableCount = $wfNames.Count
            wfRowCount = $wfRows
            purTableCount = $purNames.Count
            purRowCount = $purRows
        }
        criticalCounts = [pscustomobject]$critical
        representativeLegacyData = [pscustomobject]@{
            hasWfDefinitions = $hasWfDefinitions
            hasOaRuntime = $hasOaRuntime
            hasPurData = $hasPurData
            representativeAcrossWfOaPur = $hasWfDefinitions -and $hasOaRuntime -and $hasPurData
        }
    }
}

function Get-OaP0CompatibilitySnapshot {
    param([Parameter(Mandatory)][string]$DatabaseName)

    $legacyColumns = [int](Get-OaP0SqlScalar -Database $DatabaseName -Query @'
SET NOCOUNT ON;
SELECT COUNT(*)
FROM sys.columns
WHERE object_id = OBJECT_ID(N'dbo.Wf_FlowDef')
  AND [name] IN (N'FlowKey', N'SchemaJson', N'Version', N'Enable');
'@)
    $expandedTables = [int](Get-OaP0SqlScalar -Database $DatabaseName -Query @'
SET NOCOUNT ON;
SELECT COUNT(*)
FROM sys.tables
WHERE [name] IN (
    N'Wf_FlowDefVersion',
    N'Wf_FormDefVersion',
    N'Wf_FlowDefVersionDependency',
    N'Wf_FormFlowBinding',
    N'Wf_FormDraft');
'@)
    return [pscustomobject]@{
        legacyFlowHeadColumnsPresent = $legacyColumns
        requiredLegacyFlowHeadColumns = 4
        expandedTablesPresent = $expandedTables
        requiredExpandedTables = 5
    }
}

function Get-OaP0SyntheticPinEvidence {
    param([Parameter(Mandatory)][string]$DatabaseName)

    $lines = Invoke-OaP0ContainerSql -Database $DatabaseName -Query @'
SET NOCOUNT ON;
SELECT N'syntheticFlowHeads', COUNT_BIG(*)
FROM Wf_FlowDef
WHERE FlowKey LIKE N'oa-p0-pin-%'
UNION ALL
SELECT N'publishedVersions', COUNT_BIG(*)
FROM Wf_FlowDefVersion AS v
INNER JOIN Wf_FlowDef AS d ON d.Id = v.FlowDefId AND d.TenantId = v.TenantId
WHERE d.FlowKey LIKE N'oa-p0-pin-%' AND v.Status = 1
UNION ALL
SELECT N'pinnedInstances', COUNT_BIG(*)
FROM Wf_FlowInstance
WHERE FlowKey LIKE N'oa-p0-pin-%' AND FlowDefVersionId IS NOT NULL
UNION ALL
SELECT N'distinctPinnedVersions', COUNT_BIG(DISTINCT i.FlowDefVersionId)
FROM Wf_FlowInstance AS i
WHERE i.FlowKey LIKE N'oa-p0-pin-%'
UNION ALL
SELECT N'disabledLegacyHeadsReadable', COUNT_BIG(*)
FROM Wf_FlowDef
WHERE FlowKey LIKE N'oa-p0-pin-%'
  AND Enable = 0
  AND SchemaJson IS NOT NULL
  AND Version >= 2
UNION ALL
SELECT N'completedV1Instances', COUNT_BIG(*)
FROM Wf_FlowInstance AS i
INNER JOIN Wf_FlowDefVersion AS v ON v.Id = i.FlowDefVersionId AND v.TenantId = i.TenantId
WHERE i.FlowKey LIKE N'oa-p0-pin-%' AND v.Version = 1 AND i.Status = 1
UNION ALL
SELECT N'newV2Instances', COUNT_BIG(*)
FROM Wf_FlowInstance AS i
INNER JOIN Wf_FlowDefVersion AS v ON v.Id = i.FlowDefVersionId AND v.TenantId = i.TenantId
WHERE i.FlowKey LIKE N'oa-p0-pin-%' AND v.Version = 2;
'@
    $metrics = [ordered]@{}
    foreach ($line in @($lines)) {
        $parts = @(([string]$line).Trim() -split '\^' | ForEach-Object { $_.Trim() })
        if ($parts.Count -eq 2 -and $parts[1] -match '^[0-9]+$') {
            $metrics[$parts[0]] = [long]$parts[1]
        }
    }
    foreach ($required in @(
        'syntheticFlowHeads',
        'publishedVersions',
        'pinnedInstances',
        'distinctPinnedVersions',
        'disabledLegacyHeadsReadable',
        'completedV1Instances',
        'newV2Instances')) {
        if (-not $metrics.Contains($required)) {
            throw "Synthetic pin evidence is missing '$required'."
        }
    }
    if ($metrics.syntheticFlowHeads -lt 1 -or
        $metrics.publishedVersions -lt 2 -or
        $metrics.pinnedInstances -lt 2 -or
        $metrics.distinctPinnedVersions -lt 2 -or
        $metrics.disabledLegacyHeadsReadable -lt 1 -or
        $metrics.completedV1Instances -lt 1 -or
        $metrics.newV2Instances -lt 1) {
        throw 'Synthetic pin/feature-rollback aggregate assertions were not satisfied.'
    }
    return [pscustomobject]$metrics
}

function Get-OaP0HostConnection {
    param([Parameter(Mandatory)][string]$DatabaseName)

    $portOutput = @(& docker port $containerName '1433/tcp' 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not resolve the local SQL Server port.'
    }
    $port = $null
    foreach ($line in $portOutput) {
        if ([string]$line -match ':([0-9]+)$') {
            $port = $Matches[1]
            break
        }
    }
    if ($null -eq $port) {
        throw 'The local SQL Server container has no published 1433/tcp port.'
    }

    $secretOutput = @(
        & docker exec $containerName sh -c 'printf %s "$MSSQL_SA_PASSWORD"' 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not obtain the process-local local-SQL credential.'
    }
    $script:saPassword = ($secretOutput -join '').Trim()
    if ([string]::IsNullOrWhiteSpace($script:saPassword)) {
        throw 'The local SQL credential was empty.'
    }
    $escaped = $script:saPassword.Replace('"', '""')
    $script:connectionString =
        "Server=127.0.0.1,$port;Database=$DatabaseName;User Id=sa;" +
        "Password=`"$escaped`";TrustServerCertificate=True;Encrypt=False;" +
        'MultipleActiveResultSets=True'
    return $script:connectionString
}

function Invoke-OaP0External {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [string]$WorkingDirectory = $repoRoot
    )

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    Push-Location $WorkingDirectory
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $raw = @(& $FilePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    catch {
        $raw = @($_)
        $exitCode = 1
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
        Pop-Location
        $watch.Stop()
    }
    $safeOutput = @($raw | ForEach-Object { Protect-OaP0Text $_ })
    Write-Host "[$Name] exit=$exitCode durationMs=$($watch.ElapsedMilliseconds)"
    $safeOutput | ForEach-Object { Write-Host $_ }
    return [pscustomobject]@{
        name = $Name
        exitCode = $exitCode
        durationMs = $watch.ElapsedMilliseconds
        output = $safeOutput
    }
}

function Get-OaP0JsonReport {
    param([Parameter(Mandatory)][object]$CommandResult)

    for ($index = $CommandResult.output.Count - 1; $index -ge 0; $index--) {
        $line = ([string]$CommandResult.output[$index]).Trim()
        if ($line.StartsWith('{') -and $line.EndsWith('}')) {
            try { return $line | ConvertFrom-Json }
            catch { continue }
        }
    }
    throw "Command '$($CommandResult.name)' did not emit a one-line JSON report."
}

function Get-OaP0TestCounts {
    param([Parameter(Mandatory)][object]$CommandResult)

    $joined = $CommandResult.output -join [Environment]::NewLine
    $passed = $null
    $failed = $null
    $skipped = $null
    if ($joined -match 'Passed:\s+([0-9]+)') { $passed = [int]$Matches[1] }
    if ($joined -match 'Failed:\s+([0-9]+)') { $failed = [int]$Matches[1] }
    if ($joined -match 'Skipped:\s+([0-9]+)') { $skipped = [int]$Matches[1] }
    return [pscustomobject]@{
        passed = $passed
        failed = $failed
        skipped = $skipped
    }
}

function Invoke-OaP0CleanupMode {
    if ([string]::IsNullOrWhiteSpace($StatePath)) {
        throw '-StatePath is required in Cleanup mode.'
    }
    $resolvedStatePath = Resolve-OaP0OutputPath `
        -RequestedPath $StatePath -DefaultRelativePath 'tmp/unused.json'
    if (-not (Test-Path -LiteralPath $resolvedStatePath -PathType Leaf)) {
        throw "Cleanup state file does not exist: $resolvedStatePath"
    }
    $state = Get-Content -LiteralPath $resolvedStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-OaP0CleanupState -State $state
    Write-Host "Resolved isolated database for cleanup: $($state.databaseName)"

    $databaseCleanup = Remove-OaP0StageDatabase -State $state
    $backupCleanup = Remove-OaP0CopiedBackup -ContainerPath $state.containerBackupPath
    $state.cleanupCompleted = $true
    $state | Add-Member -NotePropertyName databaseVerifiedAbsent `
        -NotePropertyValue ([bool]$databaseCleanup.verifiedAbsent) -Force
    $state | Add-Member -NotePropertyName containerBackupVerifiedAbsent `
        -NotePropertyValue ([bool]$backupCleanup.verifiedAbsent) -Force
    Write-OaP0JsonFile -Path $resolvedStatePath -Value $state
    [pscustomobject]@{
        databaseName = $state.databaseName
        databaseVerifiedAbsent = $databaseCleanup.verifiedAbsent
        containerBackupVerifiedAbsent = $backupCleanup.verifiedAbsent
    } | ConvertTo-Json
}

if ($Mode -eq 'Cleanup') {
    Invoke-OaP0CleanupMode
    exit 0
}

if ([string]::IsNullOrWhiteSpace($BackupPath)) {
    throw '-BackupPath is required in Inspect and Drill modes.'
}
$resolvedBackupPath = (Resolve-Path -LiteralPath $BackupPath -ErrorAction Stop).Path
$backupFile = Get-Item -LiteralPath $resolvedBackupPath -ErrorAction Stop
if ($backupFile.PSIsContainer -or $backupFile.Extension -cne '.bak') {
    throw 'BackupPath must resolve to one explicit .bak file.'
}

$identity = New-OaP0StageIdentity
$resolvedEvidencePath = Resolve-OaP0OutputPath -RequestedPath $EvidencePath `
    -DefaultRelativePath "artifacts/oa-p0-stage-$($identity.RunId).json"
$resolvedStatePath = Resolve-OaP0OutputPath -RequestedPath $StatePath `
    -DefaultRelativePath "tmp/oa-p0-stage-$($identity.RunId).state.json"

$state = [pscustomobject]@{
    runId = $identity.RunId
    databaseName = $identity.DatabaseName
    containerBackupPath = $identity.ContainerBackupPath
    restoreAttempted = $false
    databaseCreated = $false
    copiedBackupCreated = $false
    cleanupCompleted = $false
    databaseVerifiedAbsent = $false
    containerBackupVerifiedAbsent = $false
}
Assert-OaP0CleanupState -State $state

$evidence = [ordered]@{
    schemaVersion = 1
    executionSurface = 'Codex CLI'
    mode = $Mode
    runId = $identity.RunId
    databaseName = $identity.DatabaseName
    startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    outcome = 'RUNNING'
    backup = [ordered]@{
        sourceFilename = $backupFile.Name
        sourceSizeBytes = [long]$backupFile.Length
        sourceLastWriteTimeUtc = $backupFile.LastWriteTimeUtc.ToString('O')
        sourceUnchanged = $false
    }
    timingsMs = [ordered]@{}
    migration = [ordered]@{}
    preflight = $null
    backfill = $null
    pinDrill = $null
    featureRollback = $null
    verification = [ordered]@{}
    cleanup = [ordered]@{}
}

$backupLengthBefore = [long]$backupFile.Length
$backupWriteTimeBefore = $backupFile.LastWriteTimeUtc
$copiedByRun = $false
$databaseCreatedByRun = $false
$failureMessage = $null
$cleanupFailure = $null
$oldDefaultConnection = $env:ConnectionStrings__DefaultConnection
$oldSqlGate = $env:CP6_TEST_SQLSERVER
$oldSharedStage = $env:CP6_OA_P0_SHARED_STAGE

Write-Host "Resolved isolated database before mutation: $($identity.DatabaseName)"
Write-Host "Backup source: $($backupFile.Name)"
Write-OaP0JsonFile -Path $resolvedStatePath -Value $state

try {
    if (Test-OaP0ContainerPathExists $identity.ContainerBackupPath) {
        throw 'Generated copied-backup target unexpectedly already exists.'
    }
    $copyOutput = @(
        & docker cp $resolvedBackupPath "${containerName}:$($identity.ContainerBackupPath)" 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw "docker cp failed: $((@($copyOutput | ForEach-Object { Protect-OaP0Text $_ })) -join ' ')"
    }
    if (-not (Test-OaP0ContainerPathExists $identity.ContainerBackupPath)) {
        throw 'Copied backup could not be verified inside the local SQL container.'
    }
    $copiedByRun = $true
    $state.copiedBackupCreated = $true
    Write-OaP0JsonFile -Path $resolvedStatePath -Value $state

    $metadataWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $backupMetadata = Get-OaP0BackupMetadata -ContainerBackupPath $identity.ContainerBackupPath
    $metadataWatch.Stop()
    $evidence.timingsMs.backupMetadata = $metadataWatch.ElapsedMilliseconds
    $evidence.backup.backupFinishTimeUtc =
        ([DateTimeOffset]$backupMetadata.BackupFinishTime).ToString('O')
    $evidence.backup.originalDatabaseName = $backupMetadata.OriginalDatabaseName
    $evidence.backup.backupSetPosition = $backupMetadata.Position
    $evidence.backup.logicalFileCounts = [ordered]@{
        data = $backupMetadata.DataFileCount
        log = $backupMetadata.LogFileCount
    }

    $state.restoreAttempted = $true
    Write-OaP0JsonFile -Path $resolvedStatePath -Value $state
    $restoreDuration = Restore-OaP0StageDatabase -State $state -BackupMetadata $backupMetadata
    $databaseCreatedByRun = $true
    $state.databaseCreated = $true
    Write-OaP0JsonFile -Path $resolvedStatePath -Value $state
    $evidence.timingsMs.restore = $restoreDuration

    $legacyInventory = Get-OaP0DatabaseInventory -DatabaseName $identity.DatabaseName
    $evidence.migration.beforeHead = $legacyInventory.migrationHead
    $evidence.backup.legacyInventory = $legacyInventory

    if ($Mode -eq 'Inspect') {
        $evidence.outcome = 'INSPECTED'
    }
    else {
        $connection = Get-OaP0HostConnection -DatabaseName $identity.DatabaseName
        $env:ConnectionStrings__DefaultConnection = $connection
        $env:CP6_TEST_SQLSERVER = $connection
        $env:CP6_OA_P0_SHARED_STAGE = '1'

        $migrationResult = Invoke-OaP0External -Name 'database-expand-to-head' `
            -FilePath 'dotnet' -Arguments @(
                'ef', 'database', 'update',
                '--project', 'CP6.Core',
                '--startup-project', 'CP6.WebApi',
                '--no-color')
        $evidence.timingsMs.expandToHead = $migrationResult.durationMs
        if ($migrationResult.exitCode -ne 0) {
            throw 'EF expand-to-head failed.'
        }

        $afterExpand = Get-OaP0DatabaseInventory -DatabaseName $identity.DatabaseName
        $evidence.migration.afterHead = $afterExpand.migrationHead
        $evidence.migration.afterExpandInventory = $afterExpand
        if ([string]::IsNullOrWhiteSpace([string]$afterExpand.migrationHead)) {
            throw 'Migration head was empty after expand.'
        }

        $preflightResult = Invoke-OaP0External -Name 'oa-p0-preflight' `
            -FilePath 'dotnet' -Arguments @(
                'run', '--project', 'CP6.WebApi', '--no-build', '--',
                '--oa-p0-preflight')
        $evidence.timingsMs.preflight = $preflightResult.durationMs
        if ($preflightResult.exitCode -ne 0) {
            throw 'Read-only OA P0 preflight failed closed.'
        }
        $preflight = Get-OaP0JsonReport -CommandResult $preflightResult
        if (-not [bool]$preflight.SafeToBackfill) {
            throw 'Read-only OA P0 preflight reported unsafe historical data.'
        }
        $evidence.preflight = $preflight

        $backfillOneResult = Invoke-OaP0External -Name 'oa-p0-backfill-1' `
            -FilePath 'dotnet' -Arguments @(
                'run', '--project', 'CP6.WebApi', '--no-build', '--',
                '--oa-p0-backfill')
        $evidence.timingsMs.backfillFirst = $backfillOneResult.durationMs
        if ($backfillOneResult.exitCode -ne 0) { throw 'First OA P0 backfill failed.' }
        $backfillOne = Get-OaP0JsonReport -CommandResult $backfillOneResult
        Assert-OaP0BackfillHasNoErrors -Report $backfillOne

        $backfillTwoResult = Invoke-OaP0External -Name 'oa-p0-backfill-2' `
            -FilePath 'dotnet' -Arguments @(
                'run', '--project', 'CP6.WebApi', '--no-build', '--',
                '--oa-p0-backfill')
        $evidence.timingsMs.backfillSecond = $backfillTwoResult.durationMs
        if ($backfillTwoResult.exitCode -ne 0) { throw 'Second OA P0 backfill failed.' }
        $backfillTwo = Get-OaP0JsonReport -CommandResult $backfillTwoResult
        Assert-OaP0SecondBackfillIsIdempotent -Report $backfillTwo
        $evidence.backfill = [ordered]@{
            first = $backfillOne
            second = $backfillTwo
            secondRunInsertedZero = $true
        }

        $driftResult = Invoke-OaP0External -Name 'has-pending-model-changes' `
            -FilePath 'dotnet' -Arguments @(
                'ef', 'migrations', 'has-pending-model-changes',
                '--project', 'CP6.Core',
                '--startup-project', 'CP6.WebApi',
                '--no-color')
        if ($driftResult.exitCode -ne 0) {
            throw 'EF has-pending-model-changes gate failed.'
        }
        $evidence.verification.modelDrift = [ordered]@{
            exitCode = $driftResult.exitCode
            durationMs = $driftResult.durationMs
        }

        $pinResult = Invoke-OaP0External -Name 'historical-sql-pin-and-feature-rollback' `
            -FilePath 'dotnet' -Arguments @(
                'test', 'CP6.Tests\CP6.Tests.csproj',
                '--no-restore', '--nologo',
                '--filter', 'FullyQualifiedName~OaP0HistoricalSqlServerTests')
        if ($pinResult.exitCode -ne 0) {
            throw 'Historical SQL pin/feature rollback drill failed.'
        }
        $pinCounts = Get-OaP0TestCounts -CommandResult $pinResult
        if ($pinCounts.failed -ne 0 -or $pinCounts.skipped -ne 0 -or $pinCounts.passed -lt 1) {
            throw 'Historical SQL pin/feature rollback drill did not execute cleanly.'
        }
        $pinEvidence = Get-OaP0SyntheticPinEvidence -DatabaseName $identity.DatabaseName
        $compatibility = Get-OaP0CompatibilitySnapshot -DatabaseName $identity.DatabaseName
        if ($compatibility.legacyFlowHeadColumnsPresent -ne
            $compatibility.requiredLegacyFlowHeadColumns -or
            $compatibility.expandedTablesPresent -ne $compatibility.requiredExpandedTables) {
            throw 'Feature rollback compatibility schema assertions failed.'
        }
        $evidence.pinDrill = [ordered]@{
            passed = $true
            tests = $pinCounts
            aggregateAssertions = $pinEvidence
            v1RemainedPinnedAfterV2Publish = $true
            newInstancePinnedV2 = $true
        }
        $evidence.featureRollback = [ordered]@{
            passed = $true
            newEntryDisabled = $true
            pinnedV1CompletedAfterDisable = $true
            legacyCompatibleHeadRead = $true
            expandedSchemaAndVersionRowsPreserved = $true
            schemaAssertions = $compatibility
            actualPreviousApplicationBinaryAvailable = $false
            fullPreviousBinaryRollbackRehearsalClaimed = $false
        }

        $sqlGateResult = Invoke-OaP0External -Name 'real-sql-gate' `
            -FilePath 'powershell' -Arguments @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass',
                '-File', 'scripts/verify-oa-p0.ps1',
                '-Stage', 'SqlServer')
        if ($sqlGateResult.exitCode -ne 0) { throw 'Real SQL Server gate failed.' }
        $evidence.verification.realSqlGate = [ordered]@{
            exitCode = $sqlGateResult.exitCode
            durationMs = $sqlGateResult.durationMs
            tests = Get-OaP0TestCounts -CommandResult $sqlGateResult
        }

        if ($RunFullVerification) {
            $allResult = Invoke-OaP0External -Name 'verify-all' `
                -FilePath 'powershell' -Arguments @(
                    '-NoProfile', '-ExecutionPolicy', 'Bypass',
                    '-File', 'scripts/verify-oa-p0.ps1',
                    '-Stage', 'All')
            if ($allResult.exitCode -ne 0) { throw 'verify-oa-p0 -Stage All failed.' }
            $evidence.verification.verifyAll = [ordered]@{
                exitCode = $allResult.exitCode
                durationMs = $allResult.durationMs
            }
        }

        $postInventory = Get-OaP0DatabaseInventory -DatabaseName $identity.DatabaseName
        $evidence.migration.finalHead = $postInventory.migrationHead
        $evidence.migration.finalInventory = $postInventory
        if ([string]$postInventory.migrationHead -cne [string]$afterExpand.migrationHead) {
            throw 'Migration head changed during the non-destructive feature rollback drill.'
        }
        $evidence.outcome = 'PASS'
    }
}
catch {
    $failureMessage = Protect-OaP0Text $_.Exception.Message
    $evidence.outcome = 'FAIL'
    $evidence.failure = $failureMessage
}
finally {
    if ($null -eq $oldDefaultConnection) {
        Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
    }
    else { $env:ConnectionStrings__DefaultConnection = $oldDefaultConnection }
    if ($null -eq $oldSqlGate) {
        Remove-Item Env:CP6_TEST_SQLSERVER -ErrorAction SilentlyContinue
    }
    else { $env:CP6_TEST_SQLSERVER = $oldSqlGate }
    if ($null -eq $oldSharedStage) {
        Remove-Item Env:CP6_OA_P0_SHARED_STAGE -ErrorAction SilentlyContinue
    }
    else { $env:CP6_OA_P0_SHARED_STAGE = $oldSharedStage }

    try {
        $databaseCleanup = Remove-OaP0StageDatabase -State $state `
            -CreatedByThisInvocation:$databaseCreatedByRun
        $evidence.cleanup.database = [ordered]@{
            exactName = $identity.DatabaseName
            dropped = $databaseCleanup.dropped
            singleUserUsed = $databaseCleanup.singleUserUsed
            verifiedAbsent = $databaseCleanup.verifiedAbsent
        }
    }
    catch {
        $cleanupFailure = Protect-OaP0Text $_.Exception.Message
        $evidence.cleanup.database = [ordered]@{
            exactName = $identity.DatabaseName
            verifiedAbsent = $false
            error = $cleanupFailure
        }
    }

    try {
        $backupCleanup = Remove-OaP0CopiedBackup -ContainerPath $identity.ContainerBackupPath
        $evidence.cleanup.copiedBackup = [ordered]@{
            exactPath = $identity.ContainerBackupPath
            removed = $backupCleanup.removed
            verifiedAbsent = $backupCleanup.verifiedAbsent
        }
    }
    catch {
        $message = Protect-OaP0Text $_.Exception.Message
        if ($null -eq $cleanupFailure) { $cleanupFailure = $message }
        $evidence.cleanup.copiedBackup = [ordered]@{
            exactPath = $identity.ContainerBackupPath
            verifiedAbsent = $false
            error = $message
        }
    }

    $backupAfter = Get-Item -LiteralPath $resolvedBackupPath -ErrorAction Stop
    $sourceUnchanged = [long]$backupAfter.Length -eq $backupLengthBefore -and
        $backupAfter.LastWriteTimeUtc -eq $backupWriteTimeBefore
    $evidence.backup.sourceUnchanged = $sourceUnchanged
    if (-not $sourceUnchanged -and $null -eq $failureMessage) {
        $failureMessage = 'Read-only source backup metadata changed during the drill.'
        $evidence.outcome = 'FAIL'
        $evidence.failure = $failureMessage
    }

    $state.cleanupCompleted = $null -eq $cleanupFailure
    $state.databaseCreated = $false
    $state.copiedBackupCreated = $false
    $state.databaseVerifiedAbsent =
        [bool]$evidence.cleanup.database.verifiedAbsent
    $state.containerBackupVerifiedAbsent =
        [bool]$evidence.cleanup.copiedBackup.verifiedAbsent
    Write-OaP0JsonFile -Path $resolvedStatePath -Value $state

    if ($null -ne $cleanupFailure) {
        $evidence.outcome = 'FAIL'
        $evidence.cleanupFailure = $cleanupFailure
    }
    $evidence.finishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $evidence.credentialsPersisted = $false
    $evidence.connectionStringRecorded = $false
    Write-OaP0JsonFile -Path $resolvedEvidencePath -Value $evidence
    Write-Host "Secret-free evidence: $resolvedEvidencePath"
}

$script:connectionString = $null
$script:saPassword = $null
if ($null -ne $failureMessage -or $null -ne $cleanupFailure) {
    Write-Error (Protect-OaP0Text (
        @($failureMessage, $cleanupFailure | Where-Object { $null -ne $_ }) -join ' | '))
    exit 1
}

if (-not $Quiet) {
    $evidence | ConvertTo-Json -Depth 16
}
exit 0
