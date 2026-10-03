param(
 [Parameter(Mandatory)][ValidateSet('Oidc','Identity','Erp','Wms','CoreBusiness')][string]$Suite,
 [Parameter(Mandatory)][ValidateSet('SqlServer','PostgreSql')][string]$Provider,
 [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Label,
 [string]$Filter='FullyQualifiedName~CP6.Oidc.IntegrationTests.GrantStoreRelationalTests.Twenty_four_independent_connections_redeem_exactly_once',
 [string]$Case='business-save-produces-valid-versioned-snapshots',
 [switch]$CaptureCommandFailure,
 [switch]$LegacySqlHistory,
 [ValidateSet('Main','Erp','Business')][string]$DatabaseSet='Main'
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$worktree='D:\CP6\tmp\worktrees\db-compat-wp4-20261003'
if($DatabaseSet -eq 'Erp' -and $Suite -ne 'Erp'){throw 'The ERP database set is reserved for the ERP suite.'}
if(($DatabaseSet -eq 'Business') -ne ($Suite -eq 'CoreBusiness')){throw 'Core business tests require their dedicated database set.'}
$receiptPath=switch($DatabaseSet){'Erp'{'D:\CP6\tmp\db-compat.wp4-erp-owned.json'}'Business'{'D:\CP6\tmp\db-compat.wp4-business-owned.json'}default{'D:\CP6\tmp\db-compat.wp4-owned.json'}}
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
$names=@('CP6_OIDC_TEST_PROVIDER','CP6_OIDC_TEST_CONNECTION','CP6_OIDC_TEST_SQL','CP6_ERP_TEST_PROVIDER','CP6_ERP_TEST_CONNECTION','CP6_WMS_TEST_PROVIDER','CP6_WMS_TEST_CONNECTION','CP6_CORE_TEST_PROVIDER','CP6_CORE_TEST_CONNECTION','CP6_TEST_DATABASE_OWNER','CP6_C02_TEST_SQL','CP6_C02_TEST_POSTGRES','CP6_C02_COMMAND_DIAGNOSTIC')
foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$sourcePaths=@('CP6.Oidc.IntegrationTests/OidcRelationalFixture.cs','CP6.Oidc.IntegrationTests/GrantStoreRelationalTests.cs','CP6.WebApi/Services/CrmOidcGrantStore.cs','CP6.Core/Services/Sys/RefreshTokenService.cs','CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs','CP6.Core/EFDbContext/CP6Context.cs','eng/crm/identity-events-fixture/Program.cs','eng/crm/identity-events-fixture/ProviderCases.cs')
$sourcePaths+=@('CP6.Oidc.IntegrationTests/GrantStoreRelationalCases.cs','CP6.Oidc.IntegrationTests/BrowserSessionRelationalTests.cs','CP6.Core/Services/CrmIdentity/IdentityBootstrapService.cs','CP6.Core/Services/CrmIdentity/CrmServiceTokenRecordStore.cs','eng/crm/identity-events-fixture/ProviderFixtureSupport.cs','eng/crm/identity-events-fixture/BusinessCases.cs','CP6.WebApi/Program.cs')
if($Suite -eq 'Erp'){$sourcePaths+=@('eng/crm/erp-integration-tests/ErpRelationalFixture.cs','eng/crm/erp-integration-tests/ErpHandlerRelationalTests.cs','eng/crm/erp-integration-tests/ErpScenario.cs','eng/crm/erp-integration-tests/SqlDatabaseFixture.cs','CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs','CP6.Core/Services/ErpIntegration/ErpInboxReplayService.cs','CP6.Core/Services/ErpIntegration/ErpDeliveryReplayService.cs','CP6.Core/Services/ErpIntegration/ErpQuotationOrderFactory.cs','CP6.WebApi/BackgroundServices/ErpOrderBridgeWorker.cs')}
if($Suite -eq 'Wms'){$sourcePaths+=@('CP6.Tests/WmsProductionSqlServerTests.cs','CP6.Tests/Infra/WmsProductionFactAttribute.cs')}
if($Suite -eq 'Identity'){$sourcePaths+=@('eng/crm/identity-events-fixture/ProviderDispatcherCases.cs')}
if($Suite -eq 'Erp'){$sourcePaths+=@(Get-ChildItem -LiteralPath (Join-Path $worktree 'eng/crm/erp-integration-tests') -Filter '*.cs' -File|ForEach-Object {'eng/crm/erp-integration-tests/'+$_.Name});$sourcePaths=@($sourcePaths|Sort-Object -Unique)}
if($Suite -eq 'Erp'){$sourcePaths+=@('CP6.Core/Services/Erp/OrderService.cs')}
if($Suite -eq 'CoreBusiness'){$sourcePaths+=@(Get-ChildItem -LiteralPath (Join-Path $worktree 'CP6.Tests/DatabaseCompatibility') -Filter '*.cs' -File|ForEach-Object {'CP6.Tests/DatabaseCompatibility/'+$_.Name});$sourcePaths+=@('CP6.Core/Services/Pur/PurchaseRequestService.cs','CP6.Core/Services/Wf/WfServiceJobService.cs','CP6.WebApi/BackgroundServices/WfNotificationDispatchWorker.cs','CP6.Core/Services/Fin/BudgetLineService.cs','CP6.Core/Services/Fin/JournalEntryService.cs','CP6.Core/Services/Sys/DataScopeFilter.cs','CP6.Core/Services/Sys/PermissionAggregator.cs')}
$sourceHashes=@($sourcePaths|ForEach-Object { $path=Join-Path $worktree $_; if(Test-Path -LiteralPath $path){[ordered]@{Path=$_;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}} })
$binaryBase=switch($Suite){'Oidc'{'CP6.Oidc.IntegrationTests/bin/Debug/net8.0'}'Erp'{'eng/crm/erp-integration-tests/bin/Debug/net8.0'}'Wms'{'CP6.Tests/bin/Debug/net8.0'}'CoreBusiness'{'CP6.Tests/bin/Debug/net8.0'}default{'eng/crm/identity-events-fixture/bin/Debug/net8.0'}}
$binaryHashes=@('CP6.Core.dll','CP6.WebApi.dll','CP6.Persistence.PostgreSql.dll','CP6.Oidc.IntegrationTests.dll','CP6.IdentityEvents.Fixture.dll','CP6.ErpIntegration.SqlTests.dll','CP6.Tests.dll')|ForEach-Object { $path=Join-Path (Join-Path $worktree $binaryBase) $_;if(Test-Path -LiteralPath $path){[ordered]@{Name=$_;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}} }
$started=[DateTime]::UtcNow
$exitCode=$null
if($LegacySqlHistory -and ($Suite -ne 'Oidc' -or $Provider -ne 'SqlServer' -or $Filter -ne 'FullyQualifiedName~CP6.Oidc.IntegrationTests.GrantStoreSqlTests.Forward_')){throw 'Legacy history requires its exact SQL-only fixture filter.'}
Push-Location -LiteralPath $worktree
try{
 foreach($name in $names){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}
 $env:CP6_TEST_DATABASE_OWNER=$receipt.Owner
 if($CaptureCommandFailure){$env:CP6_C02_COMMAND_DIAGNOSTIC=Join-Path $outputRoot 'private-command-failure.log'}
 $selectedConnection=if($Provider -eq 'PostgreSql'){$receipt.PostgreSqlConnection}else{$receipt.SqlServerConnection}
 if($Suite -eq 'Oidc'){
  $env:CP6_OIDC_TEST_PROVIDER=$Provider
  $env:CP6_OIDC_TEST_CONNECTION=$selectedConnection
  if($LegacySqlHistory){$env:CP6_OIDC_TEST_SQL=$selectedConnection}
  & dotnet test CP6.Oidc.IntegrationTests/CP6.Oidc.IntegrationTests.csproj --no-build --no-restore --filter $Filter --logger 'trx;LogFileName=results.trx' --results-directory $outputRoot *> $logPath
 }elseif($Suite -eq 'Erp'){
  $env:CP6_ERP_TEST_PROVIDER=$Provider
  $env:CP6_ERP_TEST_CONNECTION=$selectedConnection
  & dotnet test eng/crm/erp-integration-tests/CP6.ErpIntegration.SqlTests.csproj --no-build --no-restore --filter $Filter --logger 'trx;LogFileName=results.trx' --results-directory $outputRoot *> $logPath
 }elseif($Suite -eq 'Wms'){
  $env:CP6_WMS_TEST_PROVIDER=$Provider
  $env:CP6_WMS_TEST_CONNECTION=$selectedConnection
  & dotnet test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --filter $Filter --logger 'trx;LogFileName=results.trx' --results-directory $outputRoot *> $logPath
 }elseif($Suite -eq 'CoreBusiness'){
  $env:CP6_CORE_TEST_PROVIDER=$Provider
  $env:CP6_CORE_TEST_CONNECTION=$selectedConnection
  & dotnet test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --filter $Filter --logger 'trx;LogFileName=results.trx' --results-directory $outputRoot *> $logPath
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
if($Suite -ne 'Identity'){
 $trx=Join-Path $outputRoot 'results.trx'
 if(Test-Path -LiteralPath $trx){[xml]$xml=Get-Content -LiteralPath $trx -Raw;$counts=$xml.TestRun.ResultSummary.Counters|Select-Object total,executed,passed,failed,notExecuted}
}else{
 $summary=Join-Path $outputRoot 'evidence/summary.json'
 if(Test-Path -LiteralPath $summary){$result=Get-Content -LiteralPath $summary -Raw|ConvertFrom-Json;$counts=$result|Select-Object total,passed,failed,skipped,expectedCases}
}
$report=[ordered]@{Task='DB-COMPAT-01-WP4';Suite=$Suite;Provider=$Provider;Label=$Label;StartedUtc=$started.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o');ExitCode=$exitCode;Selection=$(if($Suite -ne 'Identity'){$Filter}else{$Case});Counts=$counts;DatabaseName=$expectedName;ReceiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash;RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;SourcesBeforeRun=$sourceHashes;BinariesBeforeRun=@($binaryHashes);ProcessLog=$logPath;ProcessLogSha256=(Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash;Scope='Actual selected provider test; fixture verifies ownership and migrations. No full WP4 acceptance claimed.'}
$report.DatabaseSet=$DatabaseSet
$report.ProcessExitCode=$exitCode
if($exitCode -eq 0){
 $countsValid=if($Suite -eq 'Identity'){$null -ne $counts -and [int]$counts.total -eq [int]$counts.expectedCases -and [int]$counts.passed -eq [int]$counts.expectedCases -and [int]$counts.failed -eq 0 -and [int]$counts.skipped -eq 0}else{$null -ne $counts -and [int]$counts.total -gt 0 -and [int]$counts.executed -eq [int]$counts.total -and [int]$counts.passed -eq [int]$counts.total -and [int]$counts.failed -eq 0 -and [int]$counts.notExecuted -eq 0}
 $report.RequiredCaseCountValidated=$countsValid
 if(-not $countsValid){$exitCode=1;$report.ExitCode=1;$report.HarnessError='Required selection must execute at least one case and complete all selected cases with zero failures/skips.'}
}
if($LegacySqlHistory){$report.DatabaseName=$null;$report.DatabaseCreation='Legacy SQL-only fixture creates separate CP6OidcTest_<GUID> databases from the local instance connection and disposes them; it does not use the WP4 database.';$report.Scope='Two unchanged SQL historical migration cases; real generated production DDL and original forward migration. Not PostgreSQL history acceptance.'}
$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
[pscustomobject]$report|Select-Object Suite,Provider,Label,ExitCode,Counts|ConvertTo-Json -Depth 5
exit $exitCode
