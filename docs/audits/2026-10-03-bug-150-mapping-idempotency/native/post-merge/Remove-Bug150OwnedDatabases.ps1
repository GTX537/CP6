$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$receiptPath='D:\CP6\tmp\bug150-owned.json'
$outputPath='D:\CP6\tmp\bug150-owned-databases-cleanup.json'
if(Test-Path -LiteralPath $outputPath){throw 'Preserve earlier cleanup proof.'}
$receipt=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json
if($receipt.Task -ne 'BUG-150' -or $receipt.Owner -notmatch '^[a-f0-9]{32}$'){throw 'Exact BUG150 ownership receipt required.'}
$databaseName='CP6Bug150_20261003_'+$receipt.Owner.Substring(0,8)
if($receipt.SqlServerDatabase -ne $databaseName -or $receipt.PostgreSqlDatabase -ne $databaseName -or $receipt.SqlServerState -ne 'CreatedAndOwnerMarked' -or $receipt.PostgreSqlState -ne 'CreatedAndOwnerMarked'){throw 'Receipt target or ownership state differs.'}
$sqlServer='localhost\KOUSQLSERVER'
$expectedSql="Server=$sqlServer;Database=$databaseName;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connect Timeout=5"
if($receipt.SqlServerConnection -cne $expectedSql){throw 'SQL receipt endpoint differs from the created local target.'}
Add-Type -Path 'C:\Users\tt\.nuget\packages\npgsql\8.0.8\lib\net8.0\Npgsql.dll'
$pg=[Npgsql.NpgsqlConnectionStringBuilder]::new($receipt.PostgreSqlConnection)
if($pg.Host -notin @('localhost','127.0.0.1','::1') -or $pg.Port -ne 5432 -or $pg.Username -notmatch '^cp6compat_wp1_[a-f0-9]{8}$' -or $pg.Database -ne $databaseName){throw 'PG receipt endpoint differs from the created local target.'}
$sqlcmd=(Get-Command sqlcmd -ErrorAction Stop).Source
$sqlMarker=(& $sqlcmd -S $sqlServer -E -C -d $databaseName -b -h -1 -W -Q "SET NOCOUNT ON;SELECT CONVERT(nvarchar(200),o.value)+N'|'+CONVERT(nvarchar(200),t.value) FROM sys.extended_properties o CROSS JOIN sys.extended_properties t WHERE o.class=0 AND t.class=0 AND o.name=N'CP6CompatOwner' AND t.name=N'CP6CompatTask';") -join "`n"
if($LASTEXITCODE -ne 0 -or $sqlMarker.Trim() -cne ($receipt.Owner+'|BUG-150')){throw 'Actual SQL owner marker differs; no database deleted.'}
$saved=@{}
foreach($name in @('PGPASSWORD','PGCONNECT_TIMEOUT','PGHOSTADDR','PGSERVICE','PGSERVICEFILE')){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$proof=[ordered]@{Task='BUG-150';DatabaseName=$databaseName;StartedUtc=[DateTime]::UtcNow.ToString('o');ReceiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash;ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;SqlServer='NotDropped';PostgreSql='NotDropped';ForcedDisconnects=0;RoleRemoved=$false}
try{
 foreach($name in @('PGHOSTADDR','PGSERVICE','PGSERVICEFILE')){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}
 $env:PGPASSWORD=$pg.Password
 $env:PGCONNECT_TIMEOUT='5'
 $psql='C:\Program Files\PostgreSQL\18\bin\psql.exe'
 $arguments=@('-w','-h',$pg.Host,'-p',"$($pg.Port)",'-U',$pg.Username,'-d','postgres','-v','ON_ERROR_STOP=1','-At')
 $pgMarker=(& $psql @arguments -c "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname='$databaseName';") -join "`n"
 if($LASTEXITCODE -ne 0 -or $pgMarker.Trim() -cne ('BUG-150:'+$receipt.Owner)){throw 'Actual PG marker differs; no database deleted.'}
 $proof['BothActualOwnersVerified']=$true
 $proof|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $outputPath -Encoding utf8NoBOM
 & $sqlcmd -S $sqlServer -E -C -d master -b -Q "DROP DATABASE [$databaseName];"
 if($LASTEXITCODE -ne 0){throw 'Normal SQL DROP failed; no forced action.'}
 $sqlRemaining=& $sqlcmd -S $sqlServer -E -C -d master -b -h -1 -W -Q "SET NOCOUNT ON;SELECT COUNT(*) FROM sys.databases WHERE name=N'$databaseName';"
 if($LASTEXITCODE -ne 0 -or [int]$sqlRemaining -ne 0){throw 'SQL absence not confirmed.'}
 $proof.SqlServer='DroppedAndAbsenceVerified'
 $proof|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $outputPath -Encoding utf8NoBOM
 & $psql @arguments -c ('DROP DATABASE "'+$databaseName+'";')
 if($LASTEXITCODE -ne 0){throw 'Normal PG DROP failed; no forced action.'}
 $pgRemaining=& $psql @arguments -c "SELECT COUNT(*) FROM pg_database WHERE datname='$databaseName';"
 if($LASTEXITCODE -ne 0 -or [int]$pgRemaining -ne 0){throw 'PG absence not confirmed.'}
 $proof.PostgreSql='DroppedAndAbsenceVerified'
 $proof['FinishedUtc']=[DateTime]::UtcNow.ToString('o')
 $proof|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $outputPath -Encoding utf8NoBOM
 $proof|ConvertTo-Json -Depth 5
}finally{
 foreach($name in $saved.Keys){if($null -eq $saved[$name]){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}else{[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
}
