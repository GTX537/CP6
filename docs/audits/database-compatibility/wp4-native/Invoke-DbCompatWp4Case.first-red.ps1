param(
 [Parameter(Mandatory)][ValidateSet('Oidc','Identity')][string]$Suite,
 [Parameter(Mandatory)][ValidateSet('SqlServer','PostgreSql')][string]$Provider,
 [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Label,
 [string]$Filter='FullyQualifiedName~CP6.Oidc.IntegrationTests.GrantStoreRelationalTests.Twenty_four_independent_connections_redeem_exactly_once',
 [string]$Case='business-save-produces-valid-versioned-snapshots'
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$worktree='D:\CP6\tmp\worktrees\db-compat-wp4-20261003'
$receiptPath='D:\CP6\tmp\db-compat.wp4-owned.json'
$receipt=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json
if($receipt.Task -ne 'DB-COMPAT-01-WP4' -or $receipt.Owner -notmatch '^[a-f0-9]{32}$'){throw 'WP4 ownership receipt required.'}
$expectedName='CP6Compat_WP4_20261003_'+$receipt.Owner.Substring(0,8)
if($receipt.SqlServerDatabase -ne $expectedName -or $receipt.PostgreSqlDatabase -ne $expectedName -or $receipt.SqlServerState -ne 'CreatedAndOwnerMarked' -or $receipt.PostgreSqlState -ne 'CreatedAndOwnerMarked'){throw 'WP4 owned database targets required.'}
$outputRoot=Join-Path 'D:\CP6\tmp\db-compat-wp4-tests' $Label
$reportPath='D:\CP6\tmp\'+$Label+'.json'
if((Test-Path -LiteralPath $outputRoot) -or (Test-Path -LiteralPath $reportPath)){throw 'Preserve previous evidence; use a fresh label.'}
$null=New-Item -ItemType Directory -Path $outputRoot
$logPath=Join-Path $outputRoot 'process.log'
$saved=@{}
$names=@('CP6_OIDC_TEST_PROVIDER','CP6_OIDC_TEST_CONNECTION','CP6_TEST_DATABASE_OWNER','CP6_C02_TEST_SQL','CP6_C02_TEST_POSTGRES')
foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$sourcePaths=@('CP6.Oidc.IntegrationTests/OidcRelationalFixture.cs','CP6.Oidc.IntegrationTests/GrantStoreRelationalTests.cs','CP6.WebApi/Services/CrmOidcGrantStore.cs','CP6.Core/Services/Sys/RefreshTokenService.cs','CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs','CP6.Core/EFDbContext/CP6Context.cs','eng/crm/identity-events-fixture/Program.cs','eng/crm/identity-events-fixture/ProviderCases.cs')
$sourceHashes=@($sourcePaths|ForEach-Object { $path=Join-Path $worktree $_; if(Test-Path -LiteralPath $path){[ordered]@{Path=$_;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}} })
$binaryBase=if($Suite -eq 'Oidc'){'CP6.Oidc.IntegrationTests/bin/Debug/net8.0'}else{'eng/crm/identity-events-fixture/bin/Debug/net8.0'}
$binaryHashes=@('CP6.Core.dll','CP6.WebApi.dll','CP6.Persistence.PostgreSql.dll','CP6.Oidc.IntegrationTests.dll','CP6.IdentityEvents.Fixture.dll')|ForEach-Object { $path=Join-Path (Join-Path $worktree $binaryBase) $_;if(Test-Path -LiteralPath $path){[ordered]@{Name=$_;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}} }
$started=[DateTime]::UtcNow
$exitCode=$null
Push-Location -LiteralPath $worktree
try{
 foreach($name in $names){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}
 $env:CP6_TEST_DATABASE_OWNER=$receipt.Owner
 $selectedConnection=if($Provider -eq 'PostgreSql'){$receipt.PostgreSqlConnection}else{$receipt.SqlServerConnection}
 if($Suite -eq 'Oidc'){
  $env:CP6_OIDC_TEST_PROVIDER=$Provider
  $env:CP6_OIDC_TEST_CONNECTION=$selectedConnection
  & dotnet test CP6.Oidc.IntegrationTests/CP6.Oidc.IntegrationTests.csproj --no-build --no-restore --filter $Filter --logger 'trx;LogFileName=results.trx' --results-directory $outputRoot *> $logPath
 }else{
  if($Provider -eq 'PostgreSql'){$env:CP6_C02_TEST_POSTGRES=$selectedConnection}else{$env:CP6_C02_TEST_SQL=$selectedConnection}
  $fixtureDll=Join-Path (Join-Path $worktree $binaryBase) 'CP6.IdentityEvents.Fixture.dll'
  & dotnet $fixtureDll (Join-Path $outputRoot 'evidence') (Join-Path $outputRoot 'private-diagnostics') provider-case $Provider $Case *> $logPath
 }
 $exitCode=$LASTEXITCODE
}finally{
 Pop-Location
 foreach($name in $saved.Keys){if($null -eq $saved[$name]){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}else{[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
}
$counts=$null
if($Suite -eq 'Oidc'){
 $trx=Join-Path $outputRoot 'results.trx'
 if(Test-Path -LiteralPath $trx){[xml]$xml=Get-Content -LiteralPath $trx -Raw;$counts=$xml.TestRun.ResultSummary.Counters|Select-Object total,executed,passed,failed,notExecuted}
}else{
 $summary=Join-Path $outputRoot 'evidence/summary.json'
 if(Test-Path -LiteralPath $summary){$result=Get-Content -LiteralPath $summary -Raw|ConvertFrom-Json;$counts=$result|Select-Object total,passed,failed,skipped,expectedCases}
}
$report=[ordered]@{Task='DB-COMPAT-01-WP4';Suite=$Suite;Provider=$Provider;Label=$Label;StartedUtc=$started.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o');ExitCode=$exitCode;Selection=$(if($Suite -eq 'Oidc'){$Filter}else{$Case});Counts=$counts;DatabaseName=$expectedName;ReceiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash;RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;SourcesBeforeRun=$sourceHashes;BinariesBeforeRun=@($binaryHashes);ProcessLog=$logPath;ProcessLogSha256=(Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash;Scope='Actual selected provider test; fixture verifies ownership and migrations. No full WP4 acceptance claimed.'}
$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
$report|Select-Object Suite,Provider,Label,ExitCode,Counts|ConvertTo-Json -Depth 5
exit $exitCode
