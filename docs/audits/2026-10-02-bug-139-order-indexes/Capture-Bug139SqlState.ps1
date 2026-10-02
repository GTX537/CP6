param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Stage)
$ErrorActionPreference='Stop'
$receipt=Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.wp2-owned.json' -Raw | ConvertFrom-Json
if($receipt.Task -ne 'DB-COMPAT-01-WP2' -or $receipt.Owner -notmatch '^[a-f0-9]{32}$' -or $receipt.SqlServerDatabase -notmatch '^CP6Compat_WP2_[0-9]{8}_[a-f0-9]{8}$') {throw 'Owned SQL receipt required.'}
$reportPath="D:\CP6\tmp\bug-139-sql-$Stage.json"
if(Test-Path -LiteralPath $reportPath) {throw 'Existing evidence must not be overwritten.'}
$query=@'
SET NOCOUNT ON;
IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'__OWNER__')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51000,'Owned isolated SQL database required.',1;
DECLARE @tables TABLE([Schema] sysname,[Name] sysname,[Rows] bigint,[ContentSha256] varchar(64));
DECLARE @schema sysname,@name sysname,@object int,@keys nvarchar(max),@command nvarchar(max),@rows bigint,@digest varchar(64);
DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR
 SELECT s.name,t.name,t.object_id FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0 ORDER BY s.name,t.name;
OPEN table_cursor;
FETCH NEXT FROM table_cursor INTO @schema,@name,@object;
WHILE @@FETCH_STATUS=0
BEGIN
 SELECT @keys=STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(c.name)),N',') WITHIN GROUP(ORDER BY ic.key_ordinal)
 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
 JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
 WHERE i.object_id=@object AND i.is_primary_key=1 AND ic.key_ordinal>0;
 IF @keys IS NULL THROW 51001,'Every captured table must have a deterministic primary-key order.',1;
 SET @command=N'SELECT @rows=COUNT_BIG(*) FROM '+QUOTENAME(@schema)+N'.'+QUOTENAME(@name)
  +N'; SELECT @digest=CONVERT(varchar(64),HASHBYTES(''SHA2_256'',(SELECT * FROM '+QUOTENAME(@schema)+N'.'+QUOTENAME(@name)
  +N' ORDER BY '+@keys+N' FOR JSON PATH, INCLUDE_NULL_VALUES)),2);';
 EXEC sys.sp_executesql @command,N'@rows bigint OUTPUT,@digest varchar(64) OUTPUT',@rows OUTPUT,@digest OUTPUT;
 INSERT @tables VALUES(@schema,@name,@rows,@digest);
 FETCH NEXT FROM table_cursor INTO @schema,@name,@object;
END;
CLOSE table_cursor; DEALLOCATE table_cursor;
DECLARE @report nvarchar(max)=(SELECT DB_NAME() AS [Database],CONVERT(varchar(40),SERVERPROPERTY('ProductVersion')) AS [Version],
 JSON_QUERY((SELECT [Schema],[Name],[Rows],[ContentSha256] FROM @tables ORDER BY [Schema],[Name] FOR JSON PATH)) AS [Tables],
 JSON_QUERY((SELECT MigrationId,ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH)) AS [CoreHistory],
 JSON_QUERY((SELECT s.name AS [Schema],t.name AS [Table],i.name AS [Name],i.index_id AS [Id],i.type AS [Type],i.is_unique AS [Unique],
  i.is_primary_key AS [PrimaryKey],i.is_unique_constraint AS [UniqueConstraint],i.is_disabled AS [Disabled],i.ignore_dup_key AS [IgnoreDuplicateKey],
  i.has_filter AS [HasFilter],i.filter_definition AS [Filter],
  JSON_QUERY((SELECT c.name AS [Name],ic.key_ordinal AS [KeyOrdinal],ic.is_included_column AS [Included],ic.is_descending_key AS [Descending]
   FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
   WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id ORDER BY ic.index_column_id FOR JSON PATH)) AS [Columns]
  FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
  WHERE t.is_ms_shipped=0 AND i.index_id>0 ORDER BY s.name,t.name,i.name FOR JSON PATH)) AS [Indexes]
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
SELECT @report;
'@
$query=$query.Replace('__OWNER__',$receipt.Owner)
$queryPath="D:\CP6\tmp\bug-139-sql-$Stage.capture.sql"
[IO.File]::WriteAllText($queryPath,$query,[Text.UTF8Encoding]::new($false))
$output=& sqlcmd -S 'localhost\KOUSQLSERVER' -E -C -d $receipt.SqlServerDatabase -b -y 0 -w 65535 -f 65001 -i $queryPath
if($LASTEXITCODE -ne 0) {throw 'Native SQL state capture failed.'}
$started=$false
$parts=foreach($line in $output) {if(-not $started -and $line.TrimStart().StartsWith('{')) {$started=$true}; if($started) {$line.Trim()}}
$payload=($parts -join '').Trim()
$state=$payload | ConvertFrom-Json -Depth 30
if($state.Database -ne $receipt.SqlServerDatabase -or @($state.Tables).Count -lt 300) {throw 'Incomplete state capture.'}
$report=[ordered]@{Scope='Native all-table deterministic PK-ordered content hashes and counts; existing index definitions and canonical Core history, no row values or credentials returned';Stage=$Stage;CapturedUtc=[DateTime]::UtcNow.ToString('O');Database=$state.Database;Version=$state.Version;Tables=$state.Tables;CoreHistory=$state.CoreHistory;Indexes=$state.Indexes;QuerySha256=(Get-FileHash -LiteralPath $queryPath -Algorithm SHA256).Hash}
$report | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $reportPath -Encoding utf8
Write-Output "Captured $($state.Tables.Count) tables, $($state.Indexes.Count) indexes and $($state.CoreHistory.Count) canonical history rows; all data represented by SHA256 only."
