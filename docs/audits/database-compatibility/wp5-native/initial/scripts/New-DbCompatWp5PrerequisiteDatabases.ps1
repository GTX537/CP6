$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$receiptPath='D:\CP6\tmp\db-compat.wp5-prerequisite-owned.json'
$publicPath='D:\CP6\tmp\wp5-prerequisite-databases-created.json'
if((Test-Path -LiteralPath $receiptPath) -or (Test-Path -LiteralPath $publicPath)){throw 'Preserve existing ownership evidence.'}
$taskName='DB-COMPAT-01-WP5'
$sourceBase='c6c662f5b744b44a51427e26fb2faff96472fcc2'
$config=Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.local.json' -Raw|ConvertFrom-Json
Add-Type -Path 'C:\Users\tt\.nuget\packages\npgsql\8.0.8\lib\net8.0\Npgsql.dll'
$pg=[Npgsql.NpgsqlConnectionStringBuilder]::new($config.PostgreSql)
if($pg.Host -notin @('localhost','127.0.0.1','::1') -or $pg.Port -ne 5432 -or $pg.Username -notmatch '^cp6compat_wp1_[a-f0-9]{8}$'){throw 'Dedicated loopback PostgreSQL test role required.'}
$entries=@()
foreach($purpose in @('space-then-core','core-then-space','space-script')){
 $owner=[Guid]::NewGuid().ToString('N')
 $pg.Database='CP6Compat_WP5_20261003_'+$owner.Substring(0,8)
 $pg.Timeout=5
 $pg.SearchPath='public'
 $entries+=,[ordered]@{Provider='PostgreSql';Purpose=$purpose;Owner=$owner;DatabaseName=$pg.Database;ConnectionString=$pg.ConnectionString;State='Planned'}
}
$receipt=[ordered]@{Task=$taskName;SourceBase=$sourceBase;CreatedHostUtc=[DateTime]::UtcNow.ToString('o');Databases=$entries}
function Save-Receipt {$receipt|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM}
$saved=@{}
foreach($name in @('PGPASSWORD','PGCONNECT_TIMEOUT','PGHOSTADDR','PGSERVICE','PGSERVICEFILE')){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
 foreach($name in @('PGHOSTADDR','PGSERVICE','PGSERVICEFILE')){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}
 $env:PGPASSWORD=$pg.Password
 $env:PGCONNECT_TIMEOUT='5'
 $psql='C:\Program Files\PostgreSQL\18\bin\psql.exe'
 $arguments=@('-w','-h',$pg.Host,'-p',"$($pg.Port)",'-U',$pg.Username,'-d','postgres','-v','ON_ERROR_STOP=1','-At')
 foreach($entry in $entries){
  $exists=& $psql @arguments -c "SELECT COUNT(*) FROM pg_database WHERE datname='$($entry.DatabaseName)';"
  if($LASTEXITCODE -ne 0 -or [int]$exists -ne 0){throw 'All prerequisite targets must be newly named databases.'}
 }
 Save-Receipt
 foreach($entry in $entries){
  & $psql @arguments -c ('CREATE DATABASE "'+$entry.DatabaseName+'";')
  if($LASTEXITCODE -ne 0){throw 'Target creation failed; retain receipt.'}
  $entry.State='Created'
  Save-Receipt
  & $psql @arguments -c ('COMMENT ON DATABASE "'+$entry.DatabaseName+'" IS '''+$taskName+':'+$entry.Owner+''';')
  if($LASTEXITCODE -ne 0){throw 'Target marker failed; retain receipt.'}
  $entry.State='CreatedAndOwnerMarked'
  Save-Receipt
 }
 $public=[ordered]@{Task=$taskName;SourceBase=$sourceBase;CreatedHostUtc=$receipt.CreatedHostUtc;ReceiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash;CreatorSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;Databases=@($entries|ForEach-Object{[ordered]@{Provider=$_.Provider;Purpose=$_.Purpose;DatabaseName=$_.DatabaseName;State=$_.State}});Scope='New empty owner-marked PostgreSQL test databases only.'}
 $public|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $publicPath -Encoding utf8NoBOM
 $public|ConvertTo-Json -Depth 6
}finally{
 foreach($name in $saved.Keys){if($null -eq $saved[$name]){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}else{[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
}
