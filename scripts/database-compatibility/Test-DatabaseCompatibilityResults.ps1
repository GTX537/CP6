# Offline parser self-tests. Synthetic reports are only parser inputs, never native acceptance evidence.
[CmdletBinding()]
param([string]$RepositoryRoot=(Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'DatabaseCompatibilityResults.psm1') -Force
$manifestPath=Join-Path $RepositoryRoot 'eng/database-compatibility/required-cases.json'
$manifest=Read-Cp6RequiredCaseManifest $manifestPath
$temporaryRoot=Join-Path ([IO.Path]::GetTempPath()) ('cp6-results-offline-'+[guid]::NewGuid().ToString('N'))
$null=[IO.Directory]::CreateDirectory($temporaryRoot)
$script:checked=0
$script:archivedChecked=0
$script:files=[Collections.Generic.List[string]]::new()

function Assert-Offline([bool]$Condition,[string]$Name) {
    if (!$Condition) { throw "Offline parser assertion failed: $Name" }
    $script:checked++
}
function Write-OfflineReport([string]$Text,[string]$Suffix='.json') {
    $path=Join-Path $temporaryRoot ([guid]::NewGuid().ToString('N')+$Suffix)
    [IO.File]::WriteAllText($path,$Text,[Text.UTF8Encoding]::new($false))
    $script:files.Add($path)
    return $path
}
function Copy-OfflineObject($Value) { return ($Value | ConvertTo-Json -Depth 30 | ConvertFrom-Json) }
function New-OfflineEntry([string]$Kind='Trx') {
    return [pscustomobject]@{
        Id='offline-parser';Project='offline.csproj';Role='runtime';EntryKind=$Kind
        ProviderEligibility=@('SqlServer','PostgreSql');RequiredCaseNames=@('A.Case','B.Theory(value: "<&")')
        ExpectedCases=2;Filter='FullyQualifiedName=A.Case|FullyQualifiedName=B.Theory';CaseCommand=@()
    }
}
function New-OfflineTrx([array]$Names=@('A.Case','B.Theory(value: "<&")'),[array]$Outcomes=@(),[string]$RunOutcome='Completed') {
    $rows=[Collections.Generic.List[string]]::new();$passed=0
    for($i=0;$i -lt $Names.Count;$i++) {
        $outcome=if($i -lt $Outcomes.Count){$Outcomes[$i]}else{'Passed'}
        if($outcome -ceq 'Passed'){$passed++}
        $name=[Security.SecurityElement]::Escape($Names[$i])
        $rows.Add('<UnitTestResult testName="'+$name+'" outcome="'+$outcome+'" testId="test-'+$i+'" executionId="execution-'+$i+'" />')
    }
    return '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'+($rows -join '')+'</Results><ResultSummary outcome="'+$RunOutcome+'"><Counters total="'+$Names.Count+'" executed="'+$Names.Count+'" passed="'+$passed+'" failed="0" notExecuted="0" /></ResultSummary></TestRun>'
}
function Assert-Report($Entry,[string]$Text,[bool]$Success,[string]$Code='',[object]$ExitCode=0,[string]$Provider='SqlServer') {
    $path=Write-OfflineReport $Text
    $result=Test-Cp6RequiredResults -Entry $Entry -Provider $Provider -ResultPath $path -ProcessExitCode $ExitCode
    Assert-Offline ($result.Success -eq $Success) "expected success=$Success, actual=$($result.Success), codes=$($result.FailureCodes -join ',')"
    if($Code){Assert-Offline ($Code -cin $result.FailureCodes) "required failure code $Code"}
    Assert-Offline ($result.ReportSha256 -ceq (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) 'report bytes hash'
    return $result
}
try {
    Assert-Offline ($manifest.Entries.Count -gt 0) 'real manifest nonempty'
    foreach($entry in $manifest.Entries | Where-Object EntryKind -CEQ 'Trx') {
        $expectedFilter=(@($entry.RequiredCaseNames | ForEach-Object { $_.Split('(')[0] } | Sort-Object -Unique) | ForEach-Object {'FullyQualifiedName='+$_}) -join '|'
        Assert-Offline ($entry.Filter -ceq $expectedFilter) "explicit exact-method filter $($entry.Id)"
    }
    $entry=New-OfflineEntry
    $good=New-OfflineTrx
    $null=Assert-Report $entry $good $true
    $null=Assert-Report $entry (New-OfflineTrx -Names @()) $false 'ZeroCases'
    $null=Assert-Report $entry (New-OfflineTrx -Names @('A.Case')) $false 'MissingCase'
    $null=Assert-Report $entry (New-OfflineTrx -Names @('A.Case','B.Theory(value: "<&")','C.Extra')) $false 'UnexpectedCase'
    $null=Assert-Report $entry (New-OfflineTrx -Names @('A.Case','C.WrongButSameCount')) $false 'MissingCase'
    $null=Assert-Report $entry (New-OfflineTrx -Names @('a.Case','B.Theory(value: "<&")')) $false 'UnexpectedCase'
    $null=Assert-Report $entry (New-OfflineTrx -Names @('A.Case','A.Case')) $false 'DuplicateCase'
    $null=Assert-Report $entry ($good.Replace('execution-1','execution-0')) $false 'DuplicateResultIdentity'
    $null=Assert-Report $entry ($good.Replace('test-1','test-0')) $false 'DuplicateResultIdentity'
    foreach($outcome in @('Failed','NotExecuted','Skipped','Inconclusive','Aborted','Timeout')) {
        $null=Assert-Report $entry (New-OfflineTrx -Outcomes @('Passed',$outcome)) $false 'NonPassingCase'
    }
    $null=Assert-Report $entry (New-OfflineTrx -RunOutcome 'Failed') $false 'RunNotSuccessful'
    $null=Assert-Report $entry ($good.Replace('total="2"','total="3"')) $false 'CounterMismatch'
    $null=Assert-Report $entry ($good.Replace('notExecuted="0"','notExecuted="1"')) $false 'CounterMismatch'
    $null=Assert-Report $entry ($good.Replace('notExecuted="0"','notExecuted="0" warning="1"')) $false 'NonPassingRunCounter'
    $null=Assert-Report $entry ($good.Replace('passed="2"','passed="two"')) $false 'ReportMalformed'
    $null=Assert-Report $entry ($good.Replace('testId="test-0"','')) $false 'ReportMalformed'
    $null=Assert-Report $entry ($good.Replace('</Results>','<Unknown /></Results>')) $false 'ReportMalformed'
    $null=Assert-Report $entry ($good.Replace('TeamTest/2010','TeamTest/unknown')) $false 'ReportMalformed'
    $null=Assert-Report $entry $good.Substring(0,$good.Length-3) $false 'ReportMalformed'
    $null=Assert-Report $entry '' $false 'ReportMalformed'
    $null=Assert-Report $entry ('<!DOCTYPE TestRun [<!ENTITY local SYSTEM "file:///not-a-report">]>'+$good) $false 'ReportMalformed'
    $null=Assert-Report $entry $good $false 'ProcessFailed' -ExitCode 1
    $null=Assert-Report $entry $good $false 'ProcessExitCodeMissing' -ExitCode $null
    $null=Assert-Report $entry $good $false 'ProcessExitCodeInvalid' -ExitCode '0'
    $result=Test-Cp6RequiredResults -Entry $entry -Provider SqlServer -ResultPath (Join-Path $temporaryRoot 'missing.trx') -ProcessExitCode 0
    Assert-Offline (!$result.Success -and 'ReportMissing' -cin $result.FailureCodes) 'missing report'
    $bad=Copy-OfflineObject $entry;$bad.ProviderEligibility=@('PostgreSql')
    $null=Assert-Report $bad $good $false 'ProviderNotEligible'

    foreach($kind in @('NativeRuntime','NativeMigration','NamedJsonChecks')) {
        $nativeEntry=New-OfflineEntry $kind
        $nativeEntry | Add-Member ResultContract @{Suite='sample';Case='all'}
        $rows=@($nativeEntry.RequiredCaseNames | ForEach-Object { if($kind -ceq 'NamedJsonChecks'){@{Name=$_;Passed=$true}}else{@{Name=$_;Status='Passed'}} })
        $native=[pscustomobject]@{Provider='SqlServer';Suite='sample';Case='all';Checks=$rows}
        $null=Assert-Report $nativeEntry ($native | ConvertTo-Json -Depth 8) $true
        $bad=Copy-OfflineObject $native;$bad.Checks[1].Name='wrong'
        $null=Assert-Report $nativeEntry ($bad | ConvertTo-Json -Depth 8) $false 'UnexpectedCase'
        $bad=Copy-OfflineObject $native;$bad.Checks[1].Name=$bad.Checks[0].Name
        $null=Assert-Report $nativeEntry ($bad | ConvertTo-Json -Depth 8) $false 'DuplicateCase'
        $bad=Copy-OfflineObject $native;$bad.Provider='PostgreSql'
        $null=Assert-Report $nativeEntry ($bad | ConvertTo-Json -Depth 8) $false 'NativeProviderMismatch'
        $bad=Copy-OfflineObject $native;$bad.Case=$null
        $null=Assert-Report $nativeEntry ($bad | ConvertTo-Json -Depth 8) $false 'NativeContractMismatch'
        $bad=Copy-OfflineObject $native;$bad.Checks=@()
        $null=Assert-Report $nativeEntry ($bad | ConvertTo-Json -Depth 8) $false 'ZeroCases'
        $bad=Copy-OfflineObject $native
        if($kind -ceq 'NamedJsonChecks'){$bad.Checks[1].Passed=$false}else{$bad.Checks[1].Status='Skipped'}
        $null=Assert-Report $nativeEntry ($bad | ConvertTo-Json -Depth 8) $false 'NonPassingCase'
        if($kind -ceq 'NamedJsonChecks') {
            $bad.Checks[1].Passed='true'
            $null=Assert-Report $nativeEntry ($bad | ConvertTo-Json -Depth 8) $false 'ReportMalformed'
        }
    }
    $identityEntry=New-OfflineEntry 'IdentityChecks'
    $identity=[pscustomobject]@{schemaId='cp6.c02.producer-provider-verification.v1';conclusion='success';provider='SqlServer';expectedCases=2;total=2;passed=2;failed=0;skipped=0;cases=@($identityEntry.RequiredCaseNames | ForEach-Object {@{name=$_;passed=$true}})}
    $null=Assert-Report $identityEntry ($identity | ConvertTo-Json -Depth 8) $true
    foreach($field in @('expectedCases','total','passed','failed','skipped')) {
        $bad=Copy-OfflineObject $identity;$bad.$field=99
        $null=Assert-Report $identityEntry ($bad | ConvertTo-Json -Depth 8) $false 'CounterMismatch'
    }
    $bad=Copy-OfflineObject $identity;$bad.cases[1].passed=$false
    $null=Assert-Report $identityEntry ($bad | ConvertTo-Json -Depth 8) $false 'NonPassingCase'
    $bad=Copy-OfflineObject $identity;$bad.conclusion='incomplete'
    $null=Assert-Report $identityEntry ($bad | ConvertTo-Json -Depth 8) $false 'NativeContractMismatch'
    $null=Assert-Report $identityEntry '{broken' $false 'ReportMalformed'

    $applicationEntry=$manifest.Entries | Where-Object Id -CEQ 'application-state-capture'
    $digest='a'*64
    $application=[pscustomobject]@{
        SchemaVersion=1;Task='DB-COMPAT-01-WP6';Provider='SqlServer';Status='Passed';Command='capture';Role='Application';DatabaseName='offline-fixture-only'
        Assertions=@($applicationEntry.RequiredCaseNames | ForEach-Object {@{Name=$_;Passed=$true;ExpectedSha256=$digest;ActualSha256=$digest;ExpectedCount=1;ActualCount=1}})
        Counts=@{EffectiveProfiles=4;Tables=400;Rows=800;Sequences=12}
        Hashes=@{StateSha256=$digest;CatalogSha256=$digest;ContentSha256=$digest;SequenceSha256=$digest;HistorySha256=$digest}
    }
    $result=Assert-Report $applicationEntry ($application | ConvertTo-Json -Depth 8) $true
    Assert-Offline ($result.ReportDatabaseName -ceq 'offline-fixture-only') 'physical name returned for receipt comparison'
    $boundEntry=Copy-OfflineObject $applicationEntry
    $boundEntry.ResultContract | Add-Member DatabaseName 'expected-receipt-database'
    $null=Assert-Report $boundEntry ($application | ConvertTo-Json -Depth 8) $false 'NativeContractMismatch'
    $bad=Copy-OfflineObject $application;$bad.Assertions[1].Name='same-count-wrong-name'
    $null=Assert-Report $applicationEntry ($bad | ConvertTo-Json -Depth 8) $false 'UnexpectedCase'
    $bad=Copy-OfflineObject $application;$bad.Assertions[1].Name=$bad.Assertions[0].Name
    $null=Assert-Report $applicationEntry ($bad | ConvertTo-Json -Depth 8) $false 'DuplicateCase'
    $bad=Copy-OfflineObject $application;$bad.Assertions[1].Passed=$false
    $null=Assert-Report $applicationEntry ($bad | ConvertTo-Json -Depth 8) $false 'NonPassingCase'
    $bad=Copy-OfflineObject $application;$bad.Assertions[1].ActualSha256='b'*64
    $null=Assert-Report $applicationEntry ($bad | ConvertTo-Json -Depth 8) $false 'AssertionValueMismatch'
    $bad=Copy-OfflineObject $application;$bad.Assertions[1].ActualCount=2
    $null=Assert-Report $applicationEntry ($bad | ConvertTo-Json -Depth 8) $false 'AssertionValueMismatch'
    $bad=Copy-OfflineObject $application;$bad.Assertions[1].ExpectedCount='1';$bad.Assertions[1].ActualCount='1'
    $null=Assert-Report $applicationEntry ($bad | ConvertTo-Json -Depth 8) $false 'ReportMalformed'
    $bad=Copy-OfflineObject $application;$bad.Counts.EffectiveProfiles=3
    $null=Assert-Report $applicationEntry ($bad | ConvertTo-Json -Depth 8) $false 'CounterMismatch'
    $bad=Copy-OfflineObject $application;$bad.Counts.Tables=-1
    $null=Assert-Report $applicationEntry ($bad | ConvertTo-Json -Depth 8) $false 'ReportMalformed'
    $bad=Copy-OfflineObject $application;$bad.Hashes.StateSha256=$null
    $null=Assert-Report $applicationEntry ($bad | ConvertTo-Json -Depth 8) $false 'ReportMalformed'
    foreach($field in @('Status','Task','Command','Role')) {
        $bad=Copy-OfflineObject $application;$bad.$field='wrong'
        $null=Assert-Report $applicationEntry ($bad | ConvertTo-Json -Depth 8) $false 'NativeContractMismatch'
    }

    foreach($entryId in @('application-signalr-user-delivery','restore-notification-replay')) {
        $entry=$manifest.Entries | Where-Object Id -CEQ $entryId
        $counts=@{};foreach($field in $entry.ExpectedCounts.PSObject.Properties){$counts[$field.Name]=$field.Value}
        $hashes=@{};foreach($field in $entry.RequiredHashFields){$hashes[$field]=$digest}
        $report=[pscustomobject]@{
            SchemaVersion=1;Task='DB-COMPAT-01-WP6';Provider='SqlServer';Status='Passed';DatabaseName='offline-fixture-only'
            Command=$entry.ResultContract.Command;Role=$entry.ResultContract.Role
            Assertions=@($entry.RequiredCaseNames | ForEach-Object {@{Name=$_;Passed=$true}})
            Counts=$counts;Hashes=$hashes
        }
        $null=Assert-Report $entry ($report | ConvertTo-Json -Depth 8) $true
        $bad=Copy-OfflineObject $report;$bad.Hashes.ReplayedNotificationStateSha256='b'*64
        $null=Assert-Report $entry ($bad | ConvertTo-Json -Depth 8) $false 'AssertionValueMismatch'
        $bad=Copy-OfflineObject $report;$bad.Counts.DispatchAttempts=2
        $null=Assert-Report $entry ($bad | ConvertTo-Json -Depth 8) $false 'CounterMismatch'
        $bad=Copy-OfflineObject $report;$bad.Assertions[1].Name='same-count-wrong-native-name'
        $null=Assert-Report $entry ($bad | ConvertTo-Json -Depth 8) $false 'UnexpectedCase'
    }

    # Manifest failures must be rejected before executing any entry.
    foreach($mutation in @('empty','duplicate-id','duplicate-name','wrong-count','zero','provider','missing-filter','broad-filter','missing-command','schema-type')) {
        $one=New-OfflineEntry
        $candidate=[pscustomobject]@{SchemaVersion=1;Entries=@($one)}
        switch($mutation) {
            'empty' {$candidate.Entries=@()}
            'duplicate-id' {$candidate.Entries=@($one,$one)}
            'duplicate-name' {$one.RequiredCaseNames=@('A.Case','A.Case')}
            'wrong-count' {$one.ExpectedCases=3}
            'zero' {$one.RequiredCaseNames=@();$one.ExpectedCases=0}
            'provider' {$one.ProviderEligibility=@('sqlserver')}
            'missing-filter' {$one.Filter=''}
            'broad-filter' {$one.Filter='FullyQualifiedName~A'}
            'missing-command' {$one.CaseCommand=$null}
            'schema-type' {$candidate.SchemaVersion='1'}
        }
        $path=Write-OfflineReport ($candidate | ConvertTo-Json -Depth 8)
        $rejected=$false
        try{$null=Read-Cp6RequiredCaseManifest $path}catch{$rejected=$true}
        Assert-Offline $rejected "invalid manifest: $mutation"
    }

    # Re-read immutable historical reports. Only reports with the same exact name set
    # are candidates; grouped/subset provenance is not relabelled as a matching run.
    foreach($entry in $manifest.Entries) {
        foreach($source in $entry.SourceEvidence) {
            $path=Join-Path $RepositoryRoot $source.Path
            Assert-Offline ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ieq $source.Sha256) "source evidence hash $($entry.Id)"
            if($entry.EntryKind -ceq 'Trx') {
                [xml]$xml=Get-Content -LiteralPath $path -Raw
                $names=@($xml.TestRun.Results.UnitTestResult | ForEach-Object testName)
                $provider=$source.Provider
            }
            else {
                $json=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
                $provider=$json.Provider
                $names=if($entry.EntryKind -ceq 'IdentityChecks'){@($json.cases | ForEach-Object name)}else{@($json.Checks | ForEach-Object Name)}
            }
            if(@(Compare-Object -ReferenceObject $entry.RequiredCaseNames -DifferenceObject $names -CaseSensitive).Count -gt 0){continue}
            if($provider -cnotin $entry.ProviderEligibility){continue}
            $expected=$source.RecordedNonPassing -eq 0 -and $entry.Id -cne 'runtime-initialize'
            $result=Test-Cp6RequiredResults -Entry $entry -Provider $provider -ResultPath $path -ProcessExitCode 0
            Assert-Offline ($result.Success -eq $expected) "historical report parser result $($entry.Id): $($result.FailureCodes -join ',')"
            $script:archivedChecked++
        }
    }
    Assert-Offline ($script:archivedChecked -gt 30) 'actual archived TRX/native report coverage'
    [pscustomobject]@{Status='Passed';Scope='Offline parser self-tests only; no .NET process or database execution';Assertions=$script:checked;ArchivedReports=$script:archivedChecked;ManifestEntries=$manifest.Entries.Count}
}
finally {
    # Delete only individual files created by this script, then the empty exact directory.
    $fullRoot=[IO.Path]::GetFullPath($temporaryRoot)+[IO.Path]::DirectorySeparatorChar
    foreach($path in $script:files) {
        if(![IO.Path]::GetFullPath($path).StartsWith($fullRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Offline cleanup path escaped its owned directory.'}
        Remove-Item -LiteralPath $path -Force -ErrorAction Stop
    }
    Remove-Item -LiteralPath $temporaryRoot -ErrorAction Stop
}
