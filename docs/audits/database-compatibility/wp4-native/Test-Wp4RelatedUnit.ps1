$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$worktree='D:\CP6\tmp\worktrees\db-compat-wp4-20261003'
$label='wp4-related-unit-regression'
$directory=Join-Path 'D:\CP6\tmp\db-compat-wp4-tests' $label
$reportPath=Join-Path 'D:\CP6\tmp' ($label+'.json')
if((Test-Path -LiteralPath $directory) -or (Test-Path -LiteralPath $reportPath)){throw 'Preserve earlier evidence.'}
$null=New-Item -ItemType Directory -Path $directory
$filter='FullyQualifiedName~CP6.Tests.CrmIdentity|FullyQualifiedName~CP6.Tests.Sys.RefreshToken|FullyQualifiedName~CP6.Tests.Sys.CrmOidc|FullyQualifiedName~CP6.Tests.Pur.PurchaseRequestApprovalP0Tests|FullyQualifiedName~CP6.Tests.Pur.PurchaseRequestServiceTests'
$sourceFiles=@('CP6.Core/Services/Pur/PurchaseRequestService.cs','CP6.Core/Services/Sys/RefreshTokenService.cs','CP6.Core/Services/CrmIdentity/IdentitySnapshotWriter.cs','CP6.Core/Services/CrmIdentity/IdentityBootstrapService.cs','CP6.Core/Services/CrmIdentity/CrmServiceTokenRecordStore.cs','CP6.WebApi/Services/CrmOidcGrantStore.cs')
$sources=@($sourceFiles|ForEach-Object {[ordered]@{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $worktree $_) -Algorithm SHA256).Hash}})
$binaries=@('CP6.Core.dll','CP6.WebApi.dll','CP6.Tests.dll')|ForEach-Object {[ordered]@{Name=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $worktree ('CP6.Tests/bin/Debug/net8.0/'+$_)) -Algorithm SHA256).Hash}}
$start=[DateTime]::UtcNow
Push-Location -LiteralPath $worktree
try{
 & dotnet test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --filter $filter --logger 'trx;LogFileName=results.trx' --results-directory $directory *> (Join-Path $directory 'process.log')
 $exitCode=$LASTEXITCODE
}finally{Pop-Location}
[xml]$trx=Get-Content -LiteralPath (Join-Path $directory 'results.trx') -Raw
$counts=$trx.TestRun.ResultSummary.Counters|Select-Object total,executed,passed,failed,notExecuted
$outcomes=@($trx.TestRun.Results.UnitTestResult)
$okay=$exitCode -eq 0 -and $outcomes.Count -gt 0 -and @($outcomes|Where-Object outcome -ne 'Passed').Count -eq 0 -and [int]$counts.total -eq $outcomes.Count -and [int]$counts.passed -eq $outcomes.Count
$report=[ordered]@{Task='DB-COMPAT-01-WP4';Suite='RelatedUnit';Scope='Existing identity/OIDC/refresh and purchase unit/SQLite regression; NOT native provider acceptance';StartedUtc=$start.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o');Selection=$filter;ProcessExitCode=$exitCode;Passed=$okay;Counts=$counts;SourcesBeforeRun=$sources;BinariesBeforeRun=$binaries;RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash}
$report|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $reportPath -Encoding utf8
[ordered]@{Label=$label;Passed=$okay;Counts=$counts}|ConvertTo-Json -Depth 4
if(!$okay){exit 1}
