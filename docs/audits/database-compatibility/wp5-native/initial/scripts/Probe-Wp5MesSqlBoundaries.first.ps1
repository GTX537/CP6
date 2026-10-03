$ErrorActionPreference='Stop'
$reportPath='D:\CP6\tmp\wp5-mes-sql-boundary-reference.json'
$logPath='D:\CP6\tmp\wp5-mes-sql-boundary-reference.log'
if ((Test-Path -LiteralPath $reportPath) -or (Test-Path -LiteralPath $logPath)) { throw 'Preserve original boundary evidence.' }
$receiptPath='D:\CP6\tmp\db-compat.wp5-reports-owned.json'
$receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ($receipt.Task -ne 'DB-COMPAT-01-WP5' -or $receipt.Owner -notmatch '^[a-f0-9]{32}$' -or $receipt.SqlServerDatabase -ne ('CP6Compat_WP5_20261003_'+$receipt.Owner.Substring(0,8))) { throw 'WP5 report ownership required.' }
$builder=[System.Data.Common.DbConnectionStringBuilder]::new()
$builder.set_ConnectionString($receipt.SqlServerConnection)
if ($builder['Server'] -ne 'localhost\KOUSQLSERVER' -or $builder['Database'] -ne $receipt.SqlServerDatabase -or $builder['Integrated Security'] -ne 'True') { throw 'Exact local integrated target required.' }
$query=@'
SET NOCOUNT ON;
IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'__OWNER__')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP5')
 THROW 51070,'WP5 owner mismatch',1;
DECLARE @results TABLE (Kind varchar(30), Days int NULL, RowsReturned int NULL, FirstDate nvarchar(10) NULL, LastDate nvarchar(10) NULL, ErrorNumber int NULL, Good varchar(50) NULL, Defect varchar(50) NULL, DivisionPrecision int NULL, DivisionScale int NULL, ProductPrecision int NULL, ProductScale int NULL, ProductValue varchar(80) NULL, FinalRate varchar(50) NULL);
DECLARE @inputs TABLE (Id int identity, Days int);
INSERT @inputs(Days) VALUES(-1),(0),(1),(366),(367),(368);
DECLARE @rows TABLE ([Date] nvarchar(10), GoodQty decimal(38,8), DefectQty decimal(38,8));
DECLARE @i int=1,@days int;
WHILE @i<=6
BEGIN
 SELECT @days=Days FROM @inputs WHERE Id=@i;
 DELETE FROM @rows;
 BEGIN TRY
  INSERT @rows EXEC dbo.usp_GetMesDailyTrend @Days=@days;
  INSERT @results(Kind,Days,RowsReturned,FirstDate,LastDate,ErrorNumber)
   SELECT 'OriginalStoredProcedure',@days,COUNT(*),MIN([Date]),MAX([Date]),NULL FROM @rows;
 END TRY
 BEGIN CATCH
  INSERT @results(Kind,Days,RowsReturned,ErrorNumber) VALUES('OriginalStoredProcedure',@days,(SELECT COUNT(*) FROM @rows),ERROR_NUMBER());
 END CATCH;
 SET @i+=1;
END;
DECLARE @amounts TABLE (Id int identity, Good decimal(21,8), Defect decimal(21,8));
INSERT @amounts(Good,Defect) VALUES(9899.50004000,100.49996000),(98.99500000,1.00500000);
SET @i=1;
DECLARE @good decimal(21,8),@defect decimal(21,8);
WHILE @i<=2
BEGIN
 SELECT @good=Good,@defect=Defect FROM @amounts WHERE Id=@i;
 INSERT @results(Kind,Good,Defect,DivisionPrecision,DivisionScale,ProductPrecision,ProductScale,ProductValue,FinalRate)
 SELECT 'OriginalDecimalExpression',CONVERT(varchar(50),@good),CONVERT(varchar(50),@defect),
  CONVERT(int,SQL_VARIANT_PROPERTY(@defect/(@good+@defect),'Precision')),
  CONVERT(int,SQL_VARIANT_PROPERTY(@defect/(@good+@defect),'Scale')),
  CONVERT(int,SQL_VARIANT_PROPERTY(@defect/(@good+@defect)*100,'Precision')),
  CONVERT(int,SQL_VARIANT_PROPERTY(@defect/(@good+@defect)*100,'Scale')),
  CONVERT(varchar(80),@defect/(@good+@defect)*100),
  CONVERT(varchar(50),CAST(@defect/(@good+@defect)*100 AS decimal(8,2)));
 SET @i+=1;
END;
SELECT * FROM @results FOR JSON PATH;
'@
$query=$query.Replace('__OWNER__',$receipt.Owner)
$raw=& sqlcmd -S 'localhost\KOUSQLSERVER' -E -C -d $receipt.SqlServerDatabase -b -h -1 -y 0 -w 65535 -f 65001 -Q $query 2>&1
$exitCode=$LASTEXITCODE
$raw | Set-Content -LiteralPath $logPath -Encoding utf8NoBOM
if ($exitCode -ne 0) { throw 'Original SQL boundary probe failed; inspect retained log.' }
$cases=($raw -join "`n").Trim() | ConvertFrom-Json
if (@($cases).Count -ne 8) { throw 'Expected six date ranges and two exact decimal expressions.' }
[ordered]@{Task='DB-COMPAT-01-WP5';Provider='SqlServer';ObservedUtc=[DateTime]::UtcNow.ToString('o');Database=$receipt.SqlServerDatabase;ReceiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash;ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;ExitCode=$exitCode;Cases=$cases;Scope='Read-only actual existing MES stored-procedure date boundaries and exact original decimal expression metadata; no production-data writes and no data-rich report acceptance.'} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
$cases | ConvertTo-Json -Depth 5
