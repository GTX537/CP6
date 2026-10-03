param([Parameter(Mandatory)][ValidateSet('SqlServer','PostgreSql')][string]$Provider,[Parameter(Mandatory)][string]$Label,[switch]$Build,[switch]$Full)
$ErrorActionPreference='Stop'
$work='D:\CP6\tmp\worktrees\bug-153-erp-concurrency-retry'
Import-Module 'D:\CP6\tmp\worktrees\db-compat-wp6-20261003\scripts\database-compatibility\DatabaseCompatibilityProcess.psm1'
$run=Join-Path 'D:\CP6\tmp' $Label
if($Label -cnotmatch '^bug153-[a-z0-9-]+$' -or (Test-Path -LiteralPath $run)){throw 'New BUG153 output required'}
$null=New-Item -ItemType Directory -Path $run
$project=Join-Path $work 'eng/crm/erp-integration-tests/CP6.ErpIntegration.SqlTests.csproj'
if($Build){
 foreach($step in @('restore','build')){
  $args=if($step -ceq 'restore'){@('restore',$project,'--locked-mode','-v:q')}else{@('build',$project,'--no-restore','-v:q')}
  $result=Invoke-Cp6CompatibilityProcess -Executable (Get-Command dotnet).Source -ArgumentList $args -WorkingDirectory $work -LogDirectory (Join-Path $run ('private/'+$step)) -ChildEnvironment @{DOTNET_NOLOGO='1'} -TimeoutSeconds 900
  $result|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $run ($step+'.json'))
  if($result.ExitCode -ne 0){throw ('BUG153_'+$step.ToUpperInvariant()+'_FAILED')}
 }
}
$receipt=Get-Content -LiteralPath 'D:\CP6\tmp\bug153-wp4-owned.private.json' -Raw|ConvertFrom-Json
if($receipt.Task -cne 'DB-COMPAT-01-WP4' -or $receipt.Owner -cne '0e0c9ce066954e4c9581d35bb68c93ba' -or $receipt.PostgreSqlDatabase -cne 'CP6Compat_WP4_20261003_0e0c9ce0' -or $receipt.SqlServerDatabase -cne $receipt.PostgreSqlDatabase){throw 'BUG153_RECEIPT_MISMATCH'}
$connection=if($Provider -ceq 'PostgreSql'){$receipt.PostgreSqlConnection}else{$receipt.SqlServerConnection}
$environment=@{CP6_TEST_DATABASE_OWNER=$receipt.Owner;CP6_ERP_TEST_PROVIDER=$Provider;CP6_ERP_TEST_CONNECTION=$connection;DOTNET_NOLOGO='1'}
$filter='FullyQualifiedName=CP6.ErpIntegration.SqlTests.OrderSqlTests.Concurrent_duplicate_commands_create_exactly_one_order|FullyQualifiedName=CP6.ErpIntegration.SqlTests.OrderSqlTests.Concurrent_different_versions_for_one_opportunity_create_one_order'
$expected=2
if($Full){
 $manifest=Get-Content -LiteralPath 'D:\CP6\tmp\worktrees\db-compat-wp6-20261003\eng\database-compatibility\required-cases.json' -Raw|ConvertFrom-Json
 $entry=$manifest.Entries|Where-Object Id -CEQ 'erp-business'
 $filter=$entry.Filter;$expected=$entry.ExpectedCases
}
$source=Get-FileHash -LiteralPath (Join-Path $work 'eng/crm/erp-integration-tests/OrderSqlTests.cs')
$binary=Get-FileHash -LiteralPath (Join-Path $work 'eng/crm/erp-integration-tests/bin/Debug/net8.0/CP6.ErpIntegration.SqlTests.dll')
$result=Invoke-Cp6CompatibilityProcess -Executable (Get-Command dotnet).Source -ArgumentList @('test',$project,'--no-build','--no-restore','--filter',$filter,'--logger','trx;LogFileName=results.trx','--results-directory',(Join-Path $run 'private/trx')) -WorkingDirectory $work -LogDirectory (Join-Path $run 'private/test') -ChildEnvironment $environment -TimeoutSeconds 1200
$results=@();$counters=$null
$trxPath=Join-Path $run 'private/trx/results.trx'
if(Test-Path -LiteralPath $trxPath){[xml]$trx=Get-Content -LiteralPath $trxPath -Raw;$results=@($trx.TestRun.Results.UnitTestResult);$counters=[ordered]@{Total=$results.Count;Passed=@($results|Where-Object outcome -CEQ Passed).Count;Failed=@($results|Where-Object outcome -CEQ Failed).Count;Other=@($results|Where-Object outcome -CNotIn @('Passed','Failed')).Count}}
$success=$result.ExitCode -eq 0 -and !$result.TimedOut -and $results.Count -eq $expected -and @($results|Where-Object outcome -CNE Passed).Count -eq 0
$report=[ordered]@{Task='BUG-153';Provider=$Provider;SourceBase='cbbb7fc8e99290f6aba7589830a98a98726280e9';ExecutionHead=((& git -C $work rev-parse HEAD).Trim());BinaryBuildScope='Original build from SourceBase plus exact recorded OrderSqlTests source; unchanged binaries reused when Build is absent';SourceSha256=$source.Hash;BinarySha256=$binary.Hash;DatabaseName=$receipt.PostgreSqlDatabase;Expected=$expected;Selection=$filter;Process=$result;Counters=$counters;Success=$success;NativeCodes=@($results|ForEach-Object{$_.Output.StdOut}|Where-Object{$_ -match 'native='});TrxSha256=$(if(Test-Path $trxPath){(Get-FileHash $trxPath).Hash});FinishedUtc=[datetime]::UtcNow.ToString('o')}
$report|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $run 'result.json')
Get-Content -LiteralPath (Join-Path $run 'private/test/stdout.log')
if(!$success){exit 1}
