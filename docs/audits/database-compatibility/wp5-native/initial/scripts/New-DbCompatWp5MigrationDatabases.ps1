$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$receiptPath='D:\CP6\tmp\db-compat.wp5-migration-owned.json'
$publicPath='D:\CP6\tmp\wp5-migration-databases-created.json'
if ((Test-Path -LiteralPath $receiptPath) -or (Test-Path -LiteralPath $publicPath)) { throw 'Preserve existing migration ownership evidence.' }
$taskName='DB-COMPAT-01-WP5'
$sourceBase='c6c662f5b744b44a51427e26fb2faff96472fcc2'
$sqlServer='localhost\KOUSQLSERVER'
$sqlcmd=(Get-Command sqlcmd -ErrorAction Stop).Source
$config=Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.local.json' -Raw | ConvertFrom-Json
Add-Type -Path 'C:\Users\tt\.nuget\packages\npgsql\8.0.8\lib\net8.0\Npgsql.dll'
$pg=[Npgsql.NpgsqlConnectionStringBuilder]::new($config.PostgreSql)
if ($pg.Host -notin @('localhost','127.0.0.1','::1') -or $pg.Port -ne 5432 -or $pg.Username -notmatch '^cp6compat_wp1_[a-f0-9]{8}$') { throw 'Dedicated loopback PostgreSQL test role required.' }
$purposes=@('space-only','asset-legacy-failure','ai-retention-script','publish-recovery-failure','publish-recovery-script')
$entries=@()
foreach ($purpose in $purposes) {
    $owner=[Guid]::NewGuid().ToString('N')
    $databaseName='CP6Compat_WP5_20261003_'+$owner.Substring(0,8)
    $entries+=,[ordered]@{ Provider='SqlServer'; Purpose=$purpose; Owner=$owner; DatabaseName=$databaseName; ConnectionString="Server=$sqlServer;Database=$databaseName;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connect Timeout=5"; State='Planned' }
}
$pgOwner=[Guid]::NewGuid().ToString('N')
$pg.Database='CP6Compat_WP5_20261003_'+$pgOwner.Substring(0,8)
$pg.Timeout=5
$pg.SearchPath='public'
$entries+=,[ordered]@{ Provider='PostgreSql'; Purpose='space-only'; Owner=$pgOwner; DatabaseName=$pg.Database; ConnectionString=$pg.ConnectionString; State='Planned' }
$receipt=[ordered]@{ Task=$taskName; SourceBase=$sourceBase; CreatedHostUtc=[DateTime]::UtcNow.ToString('o'); Databases=$entries }
function Save-Receipt { $receipt | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $receiptPath -Encoding utf8NoBOM }
$saved=@{}
foreach ($name in @('PGPASSWORD','PGCONNECT_TIMEOUT','PGHOSTADDR','PGSERVICE','PGSERVICEFILE')) { $saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
try {
    foreach ($name in @('PGHOSTADDR','PGSERVICE','PGSERVICEFILE')) { [Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process') }
    $env:PGPASSWORD=$pg.Password
    $env:PGCONNECT_TIMEOUT='5'
    $psql='C:\Program Files\PostgreSQL\18\bin\psql.exe'
    $arguments=@('-w','-h',$pg.Host,'-p',"$($pg.Port)",'-U',$pg.Username,'-d','postgres','-v','ON_ERROR_STOP=1','-At')
    foreach ($entry in $entries) {
        $databaseName=$entry.DatabaseName
        if ($entry.Provider -eq 'SqlServer') {
            $exists=& $sqlcmd -S $sqlServer -E -C -d master -b -h -1 -W -Q "SET NOCOUNT ON;SELECT COUNT(*) FROM sys.databases WHERE name=N'$databaseName';"
        } else {
            $exists=& $psql @arguments -c "SELECT COUNT(*) FROM pg_database WHERE datname='$databaseName';"
        }
        if ($LASTEXITCODE -ne 0 -or [int]$exists -ne 0) { throw 'All migration targets must be newly named databases.' }
    }
    Save-Receipt
    foreach ($entry in $entries) {
        $databaseName=$entry.DatabaseName
        $owner=$entry.Owner
        if ($entry.Provider -eq 'SqlServer') {
            & $sqlcmd -S $sqlServer -E -C -d master -b -Q "CREATE DATABASE [$databaseName];"
        } else {
            & $psql @arguments -c ('CREATE DATABASE "'+$databaseName+'";')
        }
        if ($LASTEXITCODE -ne 0) { throw 'Migration target creation failed; retain receipt for inspection.' }
        $entry.State='Created'
        Save-Receipt
        if ($entry.Provider -eq 'SqlServer') {
            & $sqlcmd -S $sqlServer -E -C -d $databaseName -b -Q "EXEC sys.sp_addextendedproperty @name=N'CP6CompatOwner',@value=N'$owner';EXEC sys.sp_addextendedproperty @name=N'CP6CompatTask',@value=N'$taskName';"
        } else {
            & $psql @arguments -c ('COMMENT ON DATABASE "'+$databaseName+'" IS '''+$taskName+':'+$owner+''';')
        }
        if ($LASTEXITCODE -ne 0) { throw 'Migration target owner marker failed; retain receipt for inspection.' }
        $entry.State='CreatedAndOwnerMarked'
        Save-Receipt
    }
    $public=[ordered]@{ Task=$taskName; SourceBase=$sourceBase; CreatedHostUtc=$receipt.CreatedHostUtc; ReceiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash; CreatorSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash; Databases=@($entries | ForEach-Object { [ordered]@{ Provider=$_.Provider; Purpose=$_.Purpose; DatabaseName=$_.DatabaseName; State=$_.State } }); Scope='Empty owner-marked test databases only; no migrations or business validation claimed.' }
    $public | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $publicPath -Encoding utf8NoBOM
    $public | ConvertTo-Json -Depth 6
} finally {
    foreach ($name in $saved.Keys) { if ($null -eq $saved[$name]) { [Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process') } else { [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process') } }
}
