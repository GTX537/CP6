$ErrorActionPreference='Stop'
$work='D:\CP6\tmp\worktrees\db-compat-wp6-20261003'
foreach($module in @('Lifecycle','Process')){Import-Module (Join-Path $work ('scripts/database-compatibility/DatabaseCompatibility'+$module+'.psm1'))}
$config=Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.local.json' -Raw|ConvertFrom-Json
$builder=[System.Data.Common.DbConnectionStringBuilder]::new();$builder.set_ConnectionString($config.PostgreSql);$builder['SSL Mode']='Disable'
$context=New-Cp6LifecycleContext -Provider PostgreSql -RunDirectory 'D:\CP6\tmp\db-compat-wp6-postgresql-matrix' -ConnectionString $builder.get_ConnectionString() -PostgreSqlBinDirectory 'C:\Program Files\PostgreSQL\18\bin'
$receipt=Get-Content -LiteralPath (Join-Path $context.ReceiptDirectory 'CP6Compat_WP6_20261003_d7fc9e9f_erp.json') -Raw|ConvertFrom-Json
$null=Test-Cp6OwnedDatabase -Context $context -Receipt $receipt -RequireNoSessions
$run='D:\CP6\tmp\wp6-erp-concurrency-diagnosis'
if(Test-Path -LiteralPath $run){throw 'DIAGNOSIS_OUTPUT_EXISTS'}
$null=New-Item -ItemType Directory -Path $run
$project=Join-Path $work 'eng/crm/erp-integration-tests/CP6.ErpIntegration.SqlTests.csproj'
$build=Invoke-Cp6CompatibilityProcess -Executable (Get-Command dotnet).Source -ArgumentList @('build',$project,'--no-restore','-v:q') -WorkingDirectory $work -LogDirectory (Join-Path $run 'private/build') -ChildEnvironment @{DOTNET_NOLOGO='1'} -TimeoutSeconds 600
$build|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $run 'build.json')
if($build.ExitCode -ne 0){throw 'DIAGNOSIS_BUILD_FAILED'}
$environment=@{CP6_COMPAT_SCOPE='WP6';CP6_COMPAT_DATABASE_NAME=$receipt.DatabaseName;CP6_TEST_DATABASE_OWNER=$receipt.Owner;CP6_ERP_TEST_PROVIDER='PostgreSql';CP6_ERP_TEST_CONNECTION=(Get-Cp6OwnedDatabaseConnection -Context $context -Receipt $receipt);DOTNET_NOLOGO='1'}
$filter='FullyQualifiedName=CP6.ErpIntegration.SqlTests.OrderSqlTests.Concurrent_duplicate_commands_create_exactly_one_order|FullyQualifiedName=CP6.ErpIntegration.SqlTests.OrderSqlTests.Concurrent_different_versions_for_one_opportunity_create_one_order'
$result=Invoke-Cp6CompatibilityProcess -Executable (Get-Command dotnet).Source -ArgumentList @('test',$project,'--no-build','--no-restore','--filter',$filter,'--logger','trx;LogFileName=results.trx','--results-directory',(Join-Path $run 'private/trx')) -WorkingDirectory $work -LogDirectory (Join-Path $run 'private/test') -ChildEnvironment $environment -TimeoutSeconds 600
$result|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $run 'execution.json')
Get-Content -LiteralPath (Join-Path $run 'private/test/stdout.log')
exit $result.ExitCode
