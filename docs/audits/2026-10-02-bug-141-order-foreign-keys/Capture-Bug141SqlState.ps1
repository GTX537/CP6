param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,79}$')][string]$Stage,
    [ValidateSet(138,139)][int]$ExpectedHistoryCount = 138
)
$ErrorActionPreference = 'Stop'
$receiptPath = 'D:\CP6\tmp\db-compat.wp2-before-sql-final-upgrade-owned.json'
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ($receipt.Task -cne 'DB-COMPAT-01-WP2' -or $receipt.Owner -cnotmatch '^[a-f0-9]{32}$' -or
    $receipt.SqlServerDatabase -cne 'CP6Compat_WP2_20261002_36ef9cae') { throw 'Exact backup task SQL receipt required.' }
$reportPath = "D:\CP6\tmp\bug-141-sql-$Stage.json"
$queryPath = "D:\CP6\tmp\bug-141-sql-$Stage.capture.sql"
if ((Test-Path -LiteralPath $reportPath) -or (Test-Path -LiteralPath $queryPath)) { throw 'Existing capture evidence must not be overwritten; use a new stage.' }
$query = @'
SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
IF DB_NAME() <> N'CP6Compat_WP2_20261002_36ef9cae'
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'__OWNER__')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51000,'Exact owned isolated backup SQL database required.',1;
IF (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) <> __HISTORY_COUNT__
 THROW 51002,'Expected canonical Core138/Core139 history count required.',1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
DECLARE @tables TABLE([Schema] sysname,[Name] sysname,[Rows] bigint,[ContentSha256] varchar(64));
DECLARE @schema sysname,@name sysname,@object int,@keys nvarchar(max),@command nvarchar(max),@rows bigint,@digest varchar(64);
DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR
 SELECT s.name,t.name,t.object_id FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
 WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name;
OPEN table_cursor;
FETCH NEXT FROM table_cursor INTO @schema,@name,@object;
WHILE @@FETCH_STATUS=0
BEGIN
 SET @keys=NULL;
 SELECT @keys=STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(c.name)),N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
 JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
 WHERE i.object_id=@object AND i.is_primary_key=1 AND ic.key_ordinal>0;
 IF @keys IS NULL THROW 51001,'Every captured table must have a deterministic primary-key order.',1;
 SET @command=N'SELECT @rows=COUNT_BIG(*) FROM '+QUOTENAME(@schema)+N'.'+QUOTENAME(@name)+N' WITH(HOLDLOCK)'
  +N'; SELECT @digest=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',COALESCE((SELECT * FROM '+QUOTENAME(@schema)+N'.'+QUOTENAME(@name)
  +N' WITH(HOLDLOCK) ORDER BY '+@keys+N' FOR JSON PATH, INCLUDE_NULL_VALUES),N''[]'')),2);';
 EXEC sys.sp_executesql @command,N'@rows bigint OUTPUT,@digest varchar(64) OUTPUT',@rows OUTPUT,@digest OUTPUT;
 INSERT @tables VALUES(@schema,@name,@rows,@digest);
 FETCH NEXT FROM table_cursor INTO @schema,@name,@object;
END;
CLOSE table_cursor; DEALLOCATE table_cursor;
DECLARE @report nvarchar(max)=(SELECT DB_NAME() AS [Database],CONVERT(varchar(40),SERVERPROPERTY('ProductVersion')) AS [Version],
 JSON_QUERY((SELECT [Schema],[Name],[Rows],[ContentSha256] FROM @tables ORDER BY [Schema],[Name] FOR JSON PATH)) AS [Tables],
 JSON_QUERY((SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH)) AS [CoreHistory],
 JSON_QUERY((SELECT s.name AS [Schema],t.name AS [Table],i.name AS [Name],i.index_id AS [Id],i.type AS [Type],i.is_unique AS [Unique],
  i.is_primary_key AS [PrimaryKey],i.is_unique_constraint AS [UniqueConstraint],i.is_disabled AS [Disabled],i.is_hypothetical AS [Hypothetical],
  i.ignore_dup_key AS [IgnoreDuplicateKey],i.has_filter AS [HasFilter],i.filter_definition AS [Filter],
  JSON_QUERY((SELECT c.name AS [Name],ic.index_column_id AS [IndexColumnId],ic.key_ordinal AS [KeyOrdinal],
   ic.is_included_column AS [Included],ic.is_descending_key AS [Descending],ic.partition_ordinal AS [PartitionOrdinal]
   FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
   WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id ORDER BY ic.index_column_id FOR JSON PATH)) AS [Columns]
  FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
  WHERE t.is_ms_shipped=0 AND i.index_id>0 ORDER BY s.name,t.name,i.name FOR JSON PATH)) AS [Indexes],
 JSON_QUERY((SELECT fs.name AS [Schema],ft.name AS [Table],f.name AS [Name],f.object_id AS [Id],
  ps.name AS [PrincipalSchema],pt.name AS [PrincipalTable],ki.name AS [PrincipalIndex],f.key_index_id AS [PrincipalIndexId],
  f.delete_referential_action AS [DeleteAction],f.delete_referential_action_desc AS [DeleteActionName],
  f.update_referential_action AS [UpdateAction],f.update_referential_action_desc AS [UpdateActionName],
  f.is_disabled AS [Disabled],f.is_not_trusted AS [NotTrusted],f.is_not_for_replication AS [NotForReplication],f.is_system_named AS [SystemNamed],
  JSON_QUERY((SELECT fc.constraint_column_id AS [Ordinal],dc.name AS [DependentColumn],pc.name AS [PrincipalColumn]
   FROM sys.foreign_key_columns fc JOIN sys.columns dc ON dc.object_id=fc.parent_object_id AND dc.column_id=fc.parent_column_id
   JOIN sys.columns pc ON pc.object_id=fc.referenced_object_id AND pc.column_id=fc.referenced_column_id
   WHERE fc.constraint_object_id=f.object_id ORDER BY fc.constraint_column_id FOR JSON PATH)) AS [Columns]
  FROM sys.foreign_keys f JOIN sys.tables ft ON ft.object_id=f.parent_object_id JOIN sys.schemas fs ON fs.schema_id=ft.schema_id
  JOIN sys.tables pt ON pt.object_id=f.referenced_object_id JOIN sys.schemas ps ON ps.schema_id=pt.schema_id
  JOIN sys.indexes ki ON ki.object_id=f.referenced_object_id AND ki.index_id=f.key_index_id
  WHERE ft.is_ms_shipped=0 ORDER BY fs.name,ft.name,f.name FOR JSON PATH)) AS [ForeignKeys]
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
ROLLBACK TRANSACTION;
SELECT @report;
'@
$query = $query.Replace('__OWNER__', $receipt.Owner).Replace('__HISTORY_COUNT__', [string]$ExpectedHistoryCount)
[IO.File]::WriteAllText($queryPath, $query, [Text.UTF8Encoding]::new($false))
$nativeOutput = & sqlcmd -S 'localhost\KOUSQLSERVER' -E -C -d $receipt.SqlServerDatabase -l 15 -t 120 -b -y 0 -w 65535 -f 65001 -i $queryPath 2>&1
$nativeExitCode = $LASTEXITCODE
if ($nativeExitCode -ne 0) { throw 'Native SQL141 capture failed; no row values or connection details emitted.' }
$jsonStarted = $false
$jsonParts = foreach ($nativeLine in $nativeOutput) {
    $textLine = [string]$nativeLine
    if (-not $jsonStarted -and $textLine.TrimStart().StartsWith('{')) { $jsonStarted = $true }
    # Preserve spaces inside wrapped JSON string values; trim only the complete payload.
    if ($jsonStarted) { $textLine }
}
$state = (($jsonParts -join '').Trim()) | ConvertFrom-Json -Depth 40
if ($state.Database -cne $receipt.SqlServerDatabase -or @($state.Tables).Count -ne 352 -or
    @($state.CoreHistory).Count -ne $ExpectedHistoryCount -or @($state.Indexes).Count -eq 0 -or @($state.ForeignKeys).Count -eq 0 -or
    @($state.Tables | Where-Object { $_.Rows -lt 0 -or $_.ContentSha256 -cnotmatch '^[0-9A-F]{64}$' }).Count -ne 0) {
    throw 'Incomplete or unexpected SQL141 state capture.'
}
$expectedLast = if ($ExpectedHistoryCount -eq 138) { '20261002175000_RestoreMissingOrderModelIndexes' } else { '20261002184500_RestoreMissingOrderModelForeignKeys' }
if ($state.CoreHistory[-1].MigrationId -cne $expectedLast) { throw 'Exact accepted canonical history tail required.' }
$report = [ordered]@{
    Scope = 'BUG141 native 352-table PK-ordered UTF16 JSON SHA256/counts with empty-table NULL normalized to []; full indexes/FKs and canonical Core history; no row values or credentials returned'
    Stage = $Stage; CapturedUtc = [DateTime]::UtcNow.ToString('O'); Database = $state.Database; Version = $state.Version
    OwnerVerified = $true; ReceiptSha256 = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
    ExpectedHistoryCount = $ExpectedHistoryCount; Tables = @($state.Tables); CoreHistory = @($state.CoreHistory)
    Indexes = @($state.Indexes); ForeignKeys = @($state.ForeignKeys)
    QuerySha256 = (Get-FileHash -LiteralPath $queryPath -Algorithm SHA256).Hash
}
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 40), [Text.UTF8Encoding]::new($false))
Write-Output "Captured $(@($state.Tables).Count) tables, $(@($state.Indexes).Count) indexes, $(@($state.ForeignKeys).Count) foreign keys and $ExpectedHistoryCount history rows; business values represented by hashes only."
