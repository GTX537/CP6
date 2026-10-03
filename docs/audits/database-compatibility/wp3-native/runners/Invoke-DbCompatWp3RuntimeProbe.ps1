param(
 [Parameter(Mandatory)][ValidateSet('SqlServer','PostgreSql')][string]$Provider,
 [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Label,
 [ValidateSet('orders','orders-extra','locks','failures','shared','cursor','claim')][string]$Suite='orders',
 [ValidateSet('immediate','all')][string]$Case='all',
 [switch]$Initialize
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$taskRoot='D:\CP6\tmp\worktrees\db-compat-wp3-20261003'
$binary=Join-Path $taskRoot 'tools\CP6.DatabaseCompatibility.RuntimeProbe\bin\Debug\net8.0\CP6.DatabaseCompatibility.RuntimeProbe.dll'
$output="D:\CP6\tmp\wp3-$Label.json"
$log="D:\CP6\tmp\wp3-$Label.log"
$inputPath="D:\CP6\tmp\wp3-$Label-input.json"
foreach($p in @($output,$log,$inputPath)){if(Test-Path -LiteralPath $p){throw 'Preserve existing probe evidence.'}}
$receipt=Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.wp3-owned.json' -Raw|ConvertFrom-Json
if($receipt.Task -cne 'DB-COMPAT-01-WP3' -or $receipt.Owner -notmatch '^[a-f0-9]{32}$' -or $receipt.SqlServerState -cne 'CreatedAndOwnerMarked' -or $receipt.PostgreSqlState -cne 'CreatedAndOwnerMarked'){throw 'Exact completed WP3 ownership receipt required.'}
$sourcePaths=@('CP6.Core/Services/Common/DocNumber.cs','CP6.Core/Persistence/DatabaseResourceLocks.cs','CP6.Core/Persistence/DatabaseUtcClock.cs','CP6.Core/Persistence/DatabaseFailureClassifier.cs','CP6.Core/Persistence/DatabaseContextOptions.cs','CP6.Core/Persistence/DatabaseMigrationProfile.cs','CP6.Core/EFDbContext/CP6Context.cs')
$sourcePaths+=@(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'tools\CP6.DatabaseCompatibility.RuntimeProbe') -File|Where-Object{$_.Extension -in @('.cs','.csproj') -or $_.Name -eq 'packages.lock.json'}|ForEach-Object{'tools/CP6.DatabaseCompatibility.RuntimeProbe/'+$_.Name})
$sourcePaths+=@(& git -C $taskRoot diff --name-only; & git -C $taskRoot ls-files --others --exclude-standard)|Where-Object {$_ -match '\.(cs|csproj|props|targets)$|(^|/)packages\.lock\.json$'}
$sourcePaths=@($sourcePaths|Sort-Object -Unique)
$sources=@(foreach($relative in $sourcePaths){[ordered]@{Path=$relative;Sha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot $relative) -Algorithm SHA256).Hash}})
$sourceBase=(& git -C $taskRoot rev-parse HEAD).Trim()
[ordered]@{Scope='Actual source/output observation after the recorded build and before this native probe. Named inputs are observed, not claimed to be a complete compiler input manifest. Original main tree identifies unchanged repository files; uncommitted observed helpers retain exact hashes.';CapturedHostUtc=[DateTime]::UtcNow.ToString('o');SourceBase=$sourceBase;SourceState='Uncommitted WP3 task source';Sources=$sources;ProbeBinarySha256=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash;Suite=$Suite;Case=$Case;Initialize=[bool]$Initialize}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $inputPath -Encoding utf8NoBOM
$saved=@{}
foreach($name in @('CP6_TEST_DATABASE_OWNER','CP6_TEST_POSTGRES','CP6_TEST_SQLSERVER')){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
 $env:CP6_TEST_DATABASE_OWNER=$receipt.Owner
 $env:CP6_TEST_POSTGRES=$receipt.PostgreSqlConnection
 $env:CP6_TEST_SQLSERVER=$receipt.SqlServerConnection
 $arguments=@($binary,'--provider',$Provider,'--output',$output,'--suite',$Suite,'--case',$Case,'--source-sha',$sourceBase)
 if($Initialize){$arguments+='--initialize'}
 Push-Location -LiteralPath $taskRoot
 try { & dotnet @arguments *> $log; $code=$LASTEXITCODE } finally { Pop-Location }
 Get-Content -LiteralPath $log -Tail 15
 if(Test-Path -LiteralPath $output){$report=Get-Content -LiteralPath $output -Raw|ConvertFrom-Json; [ordered]@{Provider=$Provider;ExitCode=$code;Passed=@($report.Checks|Where-Object Status -eq 'Passed').Count;Failed=@($report.Checks|Where-Object Status -eq 'Failed').Count;JsonPath=$output;InputPath=$inputPath}|ConvertTo-Json}
 exit $code
}finally{
 foreach($name in $saved.Keys){if($null -eq $saved[$name]){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}else{[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
}
