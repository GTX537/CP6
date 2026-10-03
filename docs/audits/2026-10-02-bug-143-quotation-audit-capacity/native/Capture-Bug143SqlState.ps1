param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,79}$')][string]$Stage,
    [ValidateSet(139,140)][int]$ExpectedHistoryCount = 139,
    [Parameter(Mandatory)][string]$InputManifestPath
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$taskRoot = 'D:\CP6\tmp'
$receiptPath = Join-Path $taskRoot 'db-compat.wp2-before-sql-final-upgrade-owned.json'
$reportPath = Join-Path $taskRoot "bug-143-sql-$Stage.json"
$queryPath = Join-Path $taskRoot "bug-143-sql-$Stage.capture.sql"
if ((Test-Path -LiteralPath $reportPath) -or (Test-Path -LiteralPath $queryPath)) {
    throw 'Existing BUG143 capture evidence must not be overwritten; choose a new stage.'
}
try { $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json }
catch { throw 'Could not read the fixed owned SQL backup receipt.' }
if ($receipt.Task -cne 'DB-COMPAT-01-WP2' -or
    $receipt.Owner -cne '36ef9cae14704ac88eb994416f628e24' -or
    $receipt.SqlServerDatabase -cne 'CP6Compat_WP2_20261002_36ef9cae') {
    throw 'Exact SQL36ef backup task/owner/database receipt required.'
}
$inputAbsolute = [IO.Path]::GetFullPath($InputManifestPath)
if (-not $inputAbsolute.StartsWith('D:\CP6\tmp\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($inputAbsolute) -match '(?i)receipt|secret|credential' -or
    -not (Test-Path -LiteralPath $inputAbsolute -PathType Leaf)) {
    throw 'Existing non-secret task-owned input manifest required.'
}
$inputSha256 = (Get-FileHash -LiteralPath $inputAbsolute -Algorithm SHA256).Hash
$receiptSha256 = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
$query = @'
SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
IF DB_NAME() <> N'CP6Compat_WP2_20261002_36ef9cae'
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'36ef9cae14704ac88eb994416f628e24')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51000,'Exact owned isolated SQL36ef backup required.',1;
IF CONVERT(int,SERVERPROPERTY('ProductMajorVersion'))<>16
 OR (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory)<>__HISTORY_COUNT__
 OR NOT EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'__EXPECTED_LAST__' AND ProductVersion=N'8.0.30')
 THROW 51002,'Exact SQL16/Core139 or Core140 accepted history required.',1;
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
 SET @keys=NULL; SET @rows=NULL; SET @digest=NULL;
 SELECT @keys=STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(c.name)),N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
 JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
 WHERE i.object_id=@object AND i.is_primary_key=1 AND ic.key_ordinal>0;
 IF @keys IS NULL THROW 51001,'Every captured table needs deterministic PK ordering.',1;
 SET @command=N'SELECT @rows=COUNT_BIG(*) FROM '+QUOTENAME(@schema)+N'.'+QUOTENAME(@name)+N' WITH(HOLDLOCK)'
  +N'; SELECT @digest=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',COALESCE((SELECT * FROM '+QUOTENAME(@schema)+N'.'+QUOTENAME(@name)
  +N' WITH(HOLDLOCK) ORDER BY '+@keys+N' FOR JSON PATH, INCLUDE_NULL_VALUES),N''[]'')),2);';
 EXEC sys.sp_executesql @command,N'@rows bigint OUTPUT,@digest varchar(64) OUTPUT',@rows OUTPUT,@digest OUTPUT;
 IF @rows IS NULL OR @digest IS NULL THROW 51001,'NULL data hash is not a complete capture.',1;
 INSERT @tables VALUES(@schema,@name,@rows,@digest);
 FETCH NEXT FROM table_cursor INTO @schema,@name,@object;
END;
CLOSE table_cursor; DEALLOCATE table_cursor;
DECLARE @report nvarchar(max)=(SELECT DB_NAME() AS [Database],
 CONVERT(varchar(40),SERVERPROPERTY('ProductVersion')) AS [Version],
 CONVERT(varchar(128),DATABASEPROPERTYEX(DB_NAME(),'Collation')) AS [DatabaseCollation],
 JSON_QUERY((SELECT [Schema],[Name],[Rows],[ContentSha256] FROM @tables ORDER BY [Schema],[Name] FOR JSON PATH,INCLUDE_NULL_VALUES)) AS [Tables],
 JSON_QUERY((SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH,INCLUDE_NULL_VALUES)) AS [CoreHistory],
 JSON_QUERY((SELECT s.name AS [Schema],t.name AS [Table],t.*
  FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
  WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name FOR JSON PATH,INCLUDE_NULL_VALUES)) AS [TableMetadata],
 JSON_QUERY((SELECT s.name AS [Schema],t.name AS [Table],c.name AS [Column],c.*,
  ty.name AS [TypeName],ts.name AS [TypeSchema],ty.is_user_defined AS [TypeUserDefined],
  CONVERT(nvarchar(100),idc.seed_value) AS [IdentitySeed],
  CONVERT(nvarchar(100),idc.increment_value) AS [IdentityIncrement],
  CONVERT(nvarchar(100),idc.last_value) AS [IdentityLastValue],
  idc.is_not_for_replication AS [IdentityNotForReplication],
  cc.definition AS [ComputedSql],cc.is_persisted AS [ComputedPersisted],
  cc.uses_database_collation AS [ComputedUsesDatabaseCollation],
  dc.name AS [DefaultConstraint],dc.definition AS [DefaultSql],dc.is_system_named AS [DefaultSystemNamed],
  mc.masking_function AS [MaskingFunction]
  FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
  JOIN sys.columns c ON c.object_id=t.object_id
  JOIN sys.types ty ON ty.user_type_id=c.user_type_id JOIN sys.schemas ts ON ts.schema_id=ty.schema_id
  LEFT JOIN sys.identity_columns idc ON idc.object_id=c.object_id AND idc.column_id=c.column_id
  LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
  LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
  LEFT JOIN sys.masked_columns mc ON mc.object_id=c.object_id AND mc.column_id=c.column_id
  WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name,c.column_id FOR JSON PATH,INCLUDE_NULL_VALUES)) AS [Columns],
 JSON_QUERY((SELECT s.name AS [Schema],t.name AS [Table],i.*,
  JSON_QUERY((SELECT c.name AS [Column],ic.* FROM sys.index_columns ic
   JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
   WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id ORDER BY ic.index_column_id
   FOR JSON PATH,INCLUDE_NULL_VALUES)) AS [Columns]
  FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
  WHERE t.is_ms_shipped=0 AND i.index_id>0 ORDER BY s.name,t.name,i.name FOR JSON PATH,INCLUDE_NULL_VALUES)) AS [Indexes],
 JSON_QUERY((SELECT fs.name AS [Schema],ft.name AS [Table],f.*,
  ps.name AS [PrincipalSchema],pt.name AS [PrincipalTable],ki.name AS [PrincipalIndex],
  JSON_QUERY((SELECT dc.name AS [DependentColumn],pc.name AS [PrincipalColumn],fc.*
   FROM sys.foreign_key_columns fc JOIN sys.columns dc ON dc.object_id=fc.parent_object_id AND dc.column_id=fc.parent_column_id
   JOIN sys.columns pc ON pc.object_id=fc.referenced_object_id AND pc.column_id=fc.referenced_column_id
   WHERE fc.constraint_object_id=f.object_id ORDER BY fc.constraint_column_id
   FOR JSON PATH,INCLUDE_NULL_VALUES)) AS [Columns]
  FROM sys.foreign_keys f JOIN sys.tables ft ON ft.object_id=f.parent_object_id JOIN sys.schemas fs ON fs.schema_id=ft.schema_id
  JOIN sys.tables pt ON pt.object_id=f.referenced_object_id JOIN sys.schemas ps ON ps.schema_id=pt.schema_id
  JOIN sys.indexes ki ON ki.object_id=f.referenced_object_id AND ki.index_id=f.key_index_id
  WHERE ft.is_ms_shipped=0 ORDER BY fs.name,ft.name,f.name FOR JSON PATH,INCLUDE_NULL_VALUES)) AS [ForeignKeys],
 JSON_QUERY((SELECT s.name AS [Schema],t.name AS [Table],ck.* FROM sys.check_constraints ck
  JOIN sys.tables t ON t.object_id=ck.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
  WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name,ck.name FOR JSON PATH,INCLUDE_NULL_VALUES)) AS [Checks],
 JSON_QUERY((SELECT s.name AS [Schema],t.name AS [Table],tr.*,sm.definition AS [ModuleSql],
  sm.uses_ansi_nulls AS [UsesAnsiNulls],sm.uses_quoted_identifier AS [UsesQuotedIdentifier]
  FROM sys.triggers tr JOIN sys.tables t ON t.object_id=tr.parent_id JOIN sys.schemas s ON s.schema_id=t.schema_id
  LEFT JOIN sys.sql_modules sm ON sm.object_id=tr.object_id
  WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name,tr.name FOR JSON PATH,INCLUDE_NULL_VALUES)) AS [Triggers]
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER,INCLUDE_NULL_VALUES);
ROLLBACK TRANSACTION;
SELECT @report;
'@
$expectedLast = if ($ExpectedHistoryCount -eq 139) { '20261002184500_RestoreMissingOrderModelForeignKeys' } else { '20261002193500_RestoreQuotationAuditColumnCapacity' }
$query = $query.Replace('__HISTORY_COUNT__',[string]$ExpectedHistoryCount).Replace('__EXPECTED_LAST__',$expectedLast)
[IO.File]::WriteAllText($queryPath,$query,[Text.UTF8Encoding]::new($false))
$nativeOutput = & sqlcmd -S 'localhost\KOUSQLSERVER' -E -C -d 'CP6Compat_WP2_20261002_36ef9cae' -l 15 -t 180 -b -y 0 -w 65535 -f 65001 -i $queryPath 2>&1
$nativeExitCode = $LASTEXITCODE
if ($nativeExitCode -ne 0) {
    $failure = [ordered]@{Scope='BUG143 native capture';Stage=$Stage;Status='Failed';FailureKind='CaptureSetupOrQueryFailure';IsMigrationRejection=$false;CapturedUtc=[DateTime]::UtcNow.ToString('O');NativeExitCode=$nativeExitCode;QuerySha256=(Get-FileHash -LiteralPath $queryPath -Algorithm SHA256).Hash;InputSha256=$inputSha256}
    [IO.File]::WriteAllText($reportPath,($failure|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
    throw 'Native BUG143 capture failed; raw output/values/connection details are suppressed.'
}
$jsonStarted = $false
$parts = foreach ($line in $nativeOutput) {
    $textLine = [string]$line
    if (-not $jsonStarted -and $textLine.TrimStart().StartsWith('{')) { $jsonStarted=$true }
    if ($jsonStarted) { $textLine }
}
try { $state = (($parts -join '').Trim()) | ConvertFrom-Json -Depth 60 }
catch { throw 'Native BUG143 capture did not return complete JSON; raw output is suppressed.' }
if ($state.Database -cne 'CP6Compat_WP2_20261002_36ef9cae' -or
    @($state.Tables).Count -ne 352 -or @($state.Columns).Count -ne 6832 -or
    @($state.Indexes).Count -ne 1378 -or @($state.ForeignKeys).Count -ne 202 -or
    @($state.TableMetadata).Count -ne 352 -or @($state.CoreHistory).Count -ne $ExpectedHistoryCount -or
    $state.CoreHistory[-1].MigrationId -cne $expectedLast -or
    @($state.Tables | Where-Object { $null -eq $_.Rows -or $_.Rows -lt 0 -or $_.ContentSha256 -cnotmatch '^[0-9A-F]{64}$' }).Count -ne 0) {
    throw 'Incomplete BUG143 capture; exact 352/6832/1378/202 catalogs and non-NULL normalized hashes required.'
}
$report = [ordered]@{
    Scope='BUG143 complete SQL16 352-table PK-ordered UTF16 JSON SHA256/counts (empty normalized to []); all sys.columns fields plus identity/computed/default/masking metadata; full tables/indexes/FKs/checks/triggers/Core history. No row values or credentials returned.'
    Stage=$Stage;Status='Captured';CapturedUtc=[DateTime]::UtcNow.ToString('O')
    Database=$state.Database;Version=$state.Version;DatabaseCollation=$state.DatabaseCollation
    OwnerVerified=$true;Task='DB-COMPAT-01-WP2';ReceiptSha256=$receiptSha256
    InputManifestPath=$inputAbsolute;InputSha256=$inputSha256
    ExpectedHistoryCount=$ExpectedHistoryCount
    Tables=@($state.Tables);CoreHistory=@($state.CoreHistory);TableMetadata=@($state.TableMetadata)
    Columns=@($state.Columns);Indexes=@($state.Indexes);ForeignKeys=@($state.ForeignKeys)
    Checks=@($state.Checks);Triggers=@($state.Triggers)
    QuerySha256=(Get-FileHash -LiteralPath $queryPath -Algorithm SHA256).Hash
}
[IO.File]::WriteAllText($reportPath,($report|ConvertTo-Json -Depth 65),[Text.UTF8Encoding]::new($false))
Write-Output "Captured 352 tables/6832 columns/1378 indexes/202 FKs/Core$ExpectedHistoryCount; data represented only by hashes."
