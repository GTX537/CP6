$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$run='D:\CP6\tmp\wp6-sql-formal-matrix-cad-cleanup-fix'
$reportPath='D:\CP6\tmp\wp6-sql-wms-cleanup-recovery.json'
if(Test-Path -LiteralPath $reportPath){throw 'Preserve cleanup recovery evidence.'}
$summaryPath=Join-Path $run 'summary.json'
$summary=Get-Content -LiteralPath $summaryPath -Raw|ConvertFrom-Json
function Require([bool]$Condition,[string]$Code){if(!$Condition){throw ('CP6_WP6_SQL_CLEANUP_'+$Code)}}
Require ($summary.Status -ceq 'Failed' -and $summary.FailureStage -ceq 'wms-business' -and (Get-FileHash -LiteralPath $summaryPath).Hash -ceq '8259FAEA4EEEF0786705CB4501F1072F79D0CA7DC116606B9EEB5364A7205F50') 'ORIGINAL_FAILURE_REQUIRED'
Require (@($summary.ReceiptPaths).Count -eq 1) 'ONE_RECEIPT_REQUIRED'
$receiptPath=Join-Path $run 'receipts/CP6Compat_WP6_20261003_b26cb565_corewms.json'
Require ($summary.ReceiptPaths[0] -ceq $receiptPath) 'EXACT_RECEIPT_REQUIRED'
$receipt=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json
Require ($receipt.Task -ceq 'DB-COMPAT-01-WP6' -and $receipt.Provider -ceq 'SqlServer' -and $receipt.Role -ceq 'corewms' -and $receipt.Status -ceq 'Owned' -and $receipt.DatabaseName -ceq 'CP6Compat_WP6_20261003_b26cb565_corewms' -and $receipt.PhysicalIdentity.DatabaseId -eq 68) 'EXACT_OWNED_TARGET_REQUIRED'
$delivery=Get-Content -LiteralPath 'D:\CP6\tmp\bug163-remote-main-verification.json' -Raw|ConvertFrom-Json
$smoke=Get-Content -LiteralPath 'D:\CP6\tmp\bug163-postmerge-verification.json' -Raw|ConvertFrom-Json
Require ($delivery.PR -eq 164 -and $delivery.CandidateAncestor -and $delivery.FullTreesEqual -and $smoke.PR -eq 164 -and $smoke.Status -ceq 'RemoteMergedPostSmokePassedCleanupPending' -and $smoke.RemoteMain -ceq $delivery.RemoteMain) 'DELIVERY_AND_SMOKE_REQUIRED'
$originals=@(foreach($path in @(Get-ChildItem -LiteralPath $run -File)+@(foreach($entry in @('core-business','wms-business')){Get-ChildItem -LiteralPath (Join-Path $run ('entries/'+$entry)) -File -Recurse})){
    [pscustomobject]@{Path=$path.FullName;Sha256=(Get-FileHash -LiteralPath $path.FullName).Hash}
})
$modulePath='D:\CP6\tmp\worktrees\db-compat-wp6-20261003\scripts\database-compatibility\DatabaseCompatibilityLifecycle.psm1'
Require ((Get-FileHash -LiteralPath $modulePath).Hash -ceq 'D96CC5903DABA60CB242D715E80173C955C0191E854044F5C1F412ED1977796C') 'REVIEWED_LIFECYCLE_MODULE_REQUIRED'
$module=Import-Module $modulePath -Force -PassThru -DisableNameChecking
$context=New-Cp6LifecycleContext -Provider SqlServer -RunDirectory $run -ConnectionString 'Server=localhost\KOUSQLSERVER;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connect Timeout=5'
$null=Test-Cp6OwnedDatabase $context $receipt -RequireNoSessions
$private=& $module {param($ctx) Get-Cp6Private $ctx} $context
function Start-ControlledClient {
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=$private.Tools.sqlcmd; $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    foreach($argument in @('-S','localhost\KOUSQLSERVER','-E','-C','-d',$receipt.DatabaseName,'-b','-Q',"WAITFOR DELAY '00:00:04'; SELECT 1;")){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new(); $process.StartInfo=$start; $null=$process.Start()
    $output=$process.StandardOutput.ReadToEndAsync(); $errorOutput=$process.StandardError.ReadToEndAsync()
    $watch=[Diagnostics.Stopwatch]::StartNew(); $observed=$false
    while($watch.Elapsed.TotalSeconds -lt 3 -and !$process.HasExited){
        $state=Test-Cp6OwnedDatabase $context $receipt
        if($state.Sessions -eq 1){$observed=$true;break}
        Start-Sleep -Milliseconds 50
    }
    Require $observed 'CONTROLLED_SESSION_NOT_OBSERVED'
    return [pscustomobject]@{Process=$process;Output=$output;Error=$errorOutput}
}
function Finish-ControlledClient($Client){
    Require ($Client.Process.WaitForExit(10000)) 'CONTROLLED_CLIENT_NOT_TERMINAL'
    $output=$Client.Output.GetAwaiter().GetResult(); $errorOutput=$Client.Error.GetAwaiter().GetResult()
    Require ($Client.Process.ExitCode -eq 0) 'CONTROLLED_CLIENT_NATIVE_FAILURE'
    $result=[pscustomobject]@{ProcessId=$Client.Process.Id;ExitCode=0;NaturallyExited=$true;OutputSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($output)));ErrorSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($errorOutput)))}
    $Client.Process.Dispose();return $result
}
$client=$null; $persistent=$null; $transient=$null; $status='Running'; $failure=$null; $cleaned=@()
try{
    $receiptHash=(Get-FileHash -LiteralPath $receiptPath).Hash
    $client=Start-ControlledClient
    $watch=[Diagnostics.Stopwatch]::StartNew();$rejection=$null
    try{$null=Remove-Cp6OwnedDatabases $context @($receipt) -SessionWaitSeconds 1}catch{$rejection=$_.Exception.Message}
    Require ($rejection -ceq 'CP6_COMPAT_DATABASE_HAS_SESSIONS' -and $watch.Elapsed.TotalSeconds -ge 0.9 -and $watch.Elapsed.TotalSeconds -lt 5) 'PERSISTENT_SESSION_NOT_REJECTED'
    Require ((Get-FileHash -LiteralPath $receiptPath).Hash -ceq $receiptHash) 'REJECTED_ATTEMPT_CHANGED_RECEIPT'
    $persistent=[pscustomobject]@{ObservedSessions=1;ExpectedRejection=$rejection;ElapsedSeconds=$watch.Elapsed.TotalSeconds;ReceiptUnchanged=$true;Client=(Finish-ControlledClient $client)};$client=$null
    $client=Start-ControlledClient
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $cleaned=@(Remove-Cp6OwnedDatabases $context @($receipt))
    Require ($cleaned.Count -eq 1 -and $cleaned[0].Status -ceq 'Absent') 'ONE_TARGET_CLEANUP_REQUIRED'
    $transient=[pscustomobject]@{ObservedSessions=1;DefaultSessionWaitSeconds=15;CleanupElapsedSeconds=$watch.Elapsed.TotalSeconds;Client=(Finish-ControlledClient $client)};$client=$null
    Require (!(Test-Cp6OwnedDatabase $context $receipt -RequireNoSessions).Exists) 'FINAL_ABSENCE_REQUIRED'
    $status='Passed'
}catch{$status='Failed';$failure=if($_.Exception.Message -cmatch '^CP6_[A-Z_]+$'){$_.Exception.Message}else{'CP6_WP6_SQL_CLEANUP_SAFE_FAILURE'}}finally{
    if($null -ne $client){$null=Finish-ControlledClient $client}
    $unchanged=$true
    foreach($original in $originals){$unchanged=$unchanged -and (Get-FileHash -LiteralPath $original.Path).Hash -ceq $original.Sha256}
    if(!$unchanged){$status='Failed';$failure='CP6_WP6_SQL_CLEANUP_ORIGINAL_EVIDENCE_CHANGED'}
    [pscustomobject]@{Task='DB-COMPAT-01-WP6';RelatedBug=163;Status=$status;CheckedUtc=[datetime]::UtcNow.ToString('o');ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash;LifecycleModuleSha256=(Get-FileHash -LiteralPath $modulePath).Hash;DatabaseName=$receipt.DatabaseName;PhysicalDatabaseId=68;ControlledPersistentSession=$persistent;ControlledTransientSession=$transient;CleanedDatabaseCount=$cleaned.Count;AllOneAbsent=$status -ceq 'Passed';OriginalFailedEvidenceUnchanged=$unchanged;FailureCode=$failure;Scope='Actual native SQL Server bounded-wait controls, followed by ordinary DROP of only the exact receipt-owned database from the terminal failed WMS matrix. Both controlled clients exit normally; no termination/force/production. Original failed summary, source manifest and both core/WMS entry raw reports remain unchanged. Not a successful Matrix.'}|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
}
[pscustomobject]@{Status=$status;CleanedDatabases=$cleaned.Count;OriginalEvidenceUnchanged=$unchanged;FailureCode=$failure}|ConvertTo-Json
if($status -cne 'Passed'){exit 1}
