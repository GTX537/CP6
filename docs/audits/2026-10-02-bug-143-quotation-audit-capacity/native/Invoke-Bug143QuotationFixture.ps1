param([Parameter(Mandatory)][ValidateSet('prepare','poison','restore','cleanup')][string]$Action)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$scriptPath='D:\CP6\tmp\bug-143-quotation-fixture.sql'
$reportPath="D:\CP6\tmp\bug-143-quotation-fixture-$Action.json"
$logPath="D:\CP6\tmp\bug-143-quotation-fixture-$Action.log"
if((Test-Path -LiteralPath $reportPath) -or (Test-Path -LiteralPath $logPath)){throw 'Existing fixture action evidence must be retained.'}
$start=[DateTime]::UtcNow
$output=& sqlcmd -S 'localhost\KOUSQLSERVER' -E -C -d 'CP6Compat_WP2_20261002_36ef9cae' -l 15 -t 60 -b -y 0 -w 65535 -f 65001 -i $scriptPath -v "FixtureAction=$Action" 2>&1
$code=$LASTEXITCODE
[IO.File]::WriteAllText($logPath,($output -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
$summary=$null
if($code -eq 0){$summary=($output -join '').Trim()|ConvertFrom-Json}
$valid=$code -eq 0 -and $summary.OwnerVerified -eq $true -and $summary.FixtureAction -ceq $Action -and $summary.BusinessValuesReturned -eq $false
$report=[ordered]@{Scope='Exact SQL36ef three owned quotation rows, native checked required facets/defaults/generated tokens; committed fixture action, no production acceptance.';Action=$Action;StartedUtc=$start.ToString('O');FinishedUtc=[DateTime]::UtcNow.ToString('O');NativeExitCode=$code;Status=if($valid){'Passed'}else{'Failed'};ScriptSha256=(Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash;LogSha256=(Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash;NativeSummary=$summary}
[IO.File]::WriteAllText($reportPath,($report|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output "BUG143 quotation fixture $Action status=$($report.Status); native exit=$code."
if(-not $valid){exit 1}
