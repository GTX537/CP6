$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$receiptPath='D:\CP6\tmp\bug159-wp5-owned.private.json'
if(Test-Path -LiteralPath $receiptPath){throw 'Preserve existing WP5 ownership receipt.'}
$owner=[Guid]::NewGuid().ToString('N')
$databaseName='CP6Compat_WP5_20261003_'+$owner.Substring(0,8)
$taskName='DB-COMPAT-01-WP5'
$config=Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.local.json' -Raw|ConvertFrom-Json
Add-Type -Path 'C:\Users\tt\.nuget\packages\npgsql\8.0.8\lib\net8.0\Npgsql.dll'
$pg=[Npgsql.NpgsqlConnectionStringBuilder]::new($config.PostgreSql)
if($pg.Host -notin @('localhost','127.0.0.1','::1') -or $pg.Port -ne 5432 -or $pg.Username -notmatch '^cp6compat_wp1_[a-f0-9]{8}$'){throw 'Dedicated loopback PostgreSQL test role required.'}
$sqlServer='localhost\KOUSQLSERVER'
$sqlcmd=(Get-Command sqlcmd -ErrorAction Stop).Source
$sqlExists=& $sqlcmd -S $sqlServer -E -C -d master -b -h -1 -W -Q "SET NOCOUNT ON;SELECT COUNT(*) FROM sys.databases WHERE name=N'$databaseName';"
if($LASTEXITCODE -ne 0 -or [int]$sqlExists -ne 0){throw 'New SQL target must not exist.'}
$saved=@{}
foreach($name in @('PGPASSWORD','PGCONNECT_TIMEOUT','PGHOSTADDR','PGSERVICE','PGSERVICEFILE')){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
 foreach($name in @('PGHOSTADDR','PGSERVICE','PGSERVICEFILE')){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}
 $env:PGPASSWORD=$pg.Password
 $env:PGCONNECT_TIMEOUT='5'
 $psql='C:\Program Files\PostgreSQL\18\bin\psql.exe'
 $arguments=@('-w','-h',$pg.Host,'-p',"$($pg.Port)",'-U',$pg.Username,'-d','postgres','-v','ON_ERROR_STOP=1','-At')
 $pgExists=& $psql @arguments -c "SELECT COUNT(*) FROM pg_database WHERE datname='$databaseName';"
 if($LASTEXITCODE -ne 0 -or [int]$pgExists -ne 0){throw 'New PostgreSQL target must not exist.'}
 $pg.Database=$databaseName
 $pg.Timeout=5
$pg.SslMode=[Npgsql.SslMode]::Disable
 $pg.SearchPath='public'
 $receipt=[ordered]@{Task=$taskName;Owner=$owner;CreatedHostUtc=[DateTime]::UtcNow.ToString('o');SourceBase='5587a2a67ae73715596ca1a135b5863005abac8d';SqlServerDatabase=$databaseName;SqlServerConnection="Server=$sqlServer;Database=$databaseName;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connect Timeout=5";PostgreSqlDatabase=$databaseName;PostgreSqlConnection=$pg.ConnectionString;SqlServerState='Planned';PostgreSqlState='Planned'}
 $receipt|ConvertTo-Json|Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
 & $sqlcmd -S $sqlServer -E -C -d master -b -Q "CREATE DATABASE [$databaseName];"
 if($LASTEXITCODE -ne 0){throw 'SQL creation failed.'}
 $receipt.SqlServerState='Created'
 $receipt|ConvertTo-Json|Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
 & $sqlcmd -S $sqlServer -E -C -d $databaseName -b -Q "EXEC sys.sp_addextendedproperty @name=N'CP6CompatOwner',@value=N'$owner';EXEC sys.sp_addextendedproperty @name=N'CP6CompatTask',@value=N'$taskName';"
 if($LASTEXITCODE -ne 0){throw 'SQL owner marker failed.'}
 $receipt.SqlServerState='CreatedAndOwnerMarked'
 $receipt|ConvertTo-Json|Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
 & $psql @arguments -c ('CREATE DATABASE "'+$databaseName+'";')
 if($LASTEXITCODE -ne 0){throw 'PostgreSQL creation failed.'}
 $receipt.PostgreSqlState='Created'
 $receipt|ConvertTo-Json|Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
 & $psql @arguments -c ('COMMENT ON DATABASE "'+$databaseName+'" IS '''+$taskName+':'+$owner+''';')
 if($LASTEXITCODE -ne 0){throw 'PostgreSQL owner marker failed.'}
 $receipt.PostgreSqlState='CreatedAndOwnerMarked'
 $receipt|ConvertTo-Json|Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM
 [ordered]@{Task=$taskName;DatabaseName=$databaseName;BothCreatedAndOwnerMarked=$true;SourceBase=$receipt.SourceBase;CredentialReceiptIgnored=$true}|ConvertTo-Json|Set-Content -LiteralPath 'D:\CP6\tmp\bug159-databases-created.json' -Encoding utf8NoBOM
 Write-Output "Created both new owner-marked WP5 temporary databases: $databaseName."
}finally{
 foreach($name in $saved.Keys){if($null -eq $saved[$name]){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}else{[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
}

