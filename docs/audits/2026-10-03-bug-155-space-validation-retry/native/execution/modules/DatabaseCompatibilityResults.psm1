# Pure offline verification. This module never starts a test process or connects to a database.
Set-StrictMode -Version Latest

function Get-Cp6ResultField($Object, [string]$Name) {
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return ,$Object[$Name] }
    }
    elseif ($null -ne $Object -and $null -ne $Object.PSObject.Properties[$Name]) { return ,$Object.$Name }
    return $null
}

function Assert-Cp6RequiredEntry($Entry) {
    $id=Get-Cp6ResultField $Entry 'Id'
    if ($id -isnot [string] -or $id -cnotmatch '^[a-z0-9][a-z0-9-]+$') { throw 'Invalid required entry Id.' }
    foreach ($field in @('Project','Role','EntryKind')) {
        if ([string]::IsNullOrWhiteSpace([string](Get-Cp6ResultField $Entry $field))) { throw "Missing required entry $field." }
    }
    if ($Entry.EntryKind -cnotin @('Trx','NativeRuntime','NativeMigration','IdentityChecks','NamedJsonChecks','ApplicationChecks')) { throw 'Unsupported required entry kind.' }
    if ($Entry.Role -cnotin @('schema','sqlupgrade','application','restore','runtime','oidcidentity','erp','corewms','space','reports','spacehistory')) { throw 'Unsupported database role.' }
    $names=Get-Cp6ResultField $Entry 'RequiredCaseNames'
    $expected=Get-Cp6ResultField $Entry 'ExpectedCases'
    if ($names -isnot [array] -or $names.Count -eq 0 -or
        ($expected -isnot [int] -and $expected -isnot [long]) -or $expected -ne $names.Count) { throw 'Required names must have a positive exact ExpectedCases count.' }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $names) {
        if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name) -or $name -cne $name.Trim() -or !$seen.Add($name)) {
            throw 'Required case names must be distinct nonempty exact strings.'
        }
    }
    $providers=Get-Cp6ResultField $Entry 'ProviderEligibility'
    if ($providers -isnot [array] -or $providers.Count -eq 0 -or $providers.Count -gt 2 -or
        @($providers | Where-Object { $_ -cnotin @('SqlServer','PostgreSql') }).Count -ne 0 -or
        @($providers | Select-Object -Unique).Count -ne $providers.Count) { throw 'Invalid provider eligibility.' }
    if ($Entry.EntryKind -ceq 'Trx') {
        $filter=Get-Cp6ResultField $Entry 'Filter'
        if ([string]::IsNullOrWhiteSpace([string]$filter)) { throw 'TRX entries require an explicit filter.' }
        $requiredMethods=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($name in $names) { $null=$requiredMethods.Add('FullyQualifiedName='+$name.Split('(')[0]) }
        $filterParts=$filter.Split('|')
        if ($filterParts.Count -ne $requiredMethods.Count -or !$requiredMethods.SetEquals([string[]]$filterParts)) { throw 'TRX filter must select exactly the declared methods.' }
    }
    if ((Get-Cp6ResultField $Entry 'CaseCommand') -isnot [array]) { throw 'CaseCommand must be an explicit argument array.' }
}

function Read-Cp6RequiredCaseManifest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $manifest=Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    $schema=Get-Cp6ResultField $manifest 'SchemaVersion'
    if (($schema -isnot [int] -and $schema -isnot [long]) -or $schema -ne 1 -or
        (Get-Cp6ResultField $manifest 'Entries') -isnot [array] -or $manifest.Entries.Count -eq 0) { throw 'Unsupported or empty required-case manifest.' }
    $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $manifest.Entries) {
        Assert-Cp6RequiredEntry $entry
        if (!$ids.Add($entry.Id)) { throw 'Duplicate required entry Id.' }
    }
    return $manifest
}

function Test-Cp6RequiredResults {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Entry,
        [Parameter(Mandatory)][string]$Provider,
        [Parameter(Mandatory)][AllowEmptyString()][string]$ResultPath,
        [Parameter(Mandatory)][AllowNull()]$ProcessExitCode
    )
    $failures=[Collections.Generic.List[string]]::new()
    $cases=[Collections.Generic.List[object]]::new()
    $missing=[Collections.Generic.List[string]]::new()
    $unexpected=[Collections.Generic.List[string]]::new()
    $duplicates=[Collections.Generic.List[string]]::new()
    $hash=$null
    $reportDatabaseName=$null
    $entryValid=$true
    try { Assert-Cp6RequiredEntry $Entry } catch { $entryValid=$false; $failures.Add('EntryInvalid') }
    if ($null -eq $ProcessExitCode) { $failures.Add('ProcessExitCodeMissing') }
    elseif ($ProcessExitCode -isnot [int] -and $ProcessExitCode -isnot [long]) { $failures.Add('ProcessExitCodeInvalid') }
    elseif ($ProcessExitCode -ne 0) { $failures.Add('ProcessFailed') }
    if ($entryValid -and $Provider -cnotin $Entry.ProviderEligibility) { $failures.Add('ProviderNotEligible') }

    if ($entryValid) {
        if ([string]::IsNullOrWhiteSpace($ResultPath) -or !(Test-Path -LiteralPath $ResultPath -PathType Leaf)) { $failures.Add('ReportMissing') }
        else {
            try {
                $bytes=[IO.File]::ReadAllBytes([IO.Path]::GetFullPath($ResultPath))
                $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
                if ($bytes.Length -eq 0 -or $bytes.Length -gt 67108864) { throw 'Invalid report size.' }
                if ($Entry.EntryKind -ceq 'Trx') {
                    $settings=[Xml.XmlReaderSettings]::new()
                    $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
                    $settings.XmlResolver=$null
                    $settings.MaxCharactersInDocument=67108864
                    $stream=[IO.MemoryStream]::new($bytes,$false)
                    $reader=$null
                    try {
                        $reader=[Xml.XmlReader]::Create($stream,$settings)
                        $xml=[Xml.XmlDocument]::new(); $xml.XmlResolver=$null; $xml.Load($reader)
                    }
                    finally { if ($null -ne $reader) { $reader.Dispose() }; $stream.Dispose() }
                    if ($xml.DocumentElement.LocalName -cne 'TestRun' -or
                        $xml.DocumentElement.NamespaceURI -cne 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010') { throw 'Invalid TRX root.' }
                    $ns=[Xml.XmlNamespaceManager]::new($xml.NameTable)
                    $ns.AddNamespace('t',$xml.DocumentElement.NamespaceURI)
                    $resultRoots=$xml.SelectNodes('/t:TestRun/t:Results',$ns)
                    $summaries=$xml.SelectNodes('/t:TestRun/t:ResultSummary',$ns)
                    $counters=$xml.SelectNodes('/t:TestRun/t:ResultSummary/t:Counters',$ns)
                    if ($resultRoots.Count -ne 1 -or $summaries.Count -ne 1 -or $counters.Count -ne 1) { throw 'Incomplete TRX structure.' }
                    if ($summaries[0].GetAttribute('outcome') -cnotin @('Completed','Passed')) { $failures.Add('RunNotSuccessful') }
                    $executionIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                    $testIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                    foreach ($node in $resultRoots[0].ChildNodes) {
                        if ($node.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
                        if ($node.LocalName -cne 'UnitTestResult' -or $node.NamespaceURI -cne $xml.DocumentElement.NamespaceURI) { throw 'Unsupported TRX result element.' }
                        $name=$node.GetAttribute('testName'); $outcome=$node.GetAttribute('outcome')
                        $executionId=$node.GetAttribute('executionId'); $testId=$node.GetAttribute('testId')
                        if ([string]::IsNullOrWhiteSpace($name) -or [string]::IsNullOrWhiteSpace($outcome) -or
                            [string]::IsNullOrWhiteSpace($executionId) -or [string]::IsNullOrWhiteSpace($testId)) { throw 'Incomplete TRX result.' }
                        if (!$executionIds.Add($executionId) -or !$testIds.Add($testId)) { $failures.Add('DuplicateResultIdentity') }
                        $cases.Add([pscustomobject]@{Name=$name;Passed=($outcome -ceq 'Passed');Outcome=$outcome})
                    }
                    $countValues=@{}
                    foreach ($field in @('total','executed','passed','failed','notExecuted')) {
                        $text=$counters[0].GetAttribute($field)
                        if ($text -cnotmatch '^\d+$') { throw 'Invalid required TRX counter.' }
                        $countValues[$field]=[long]$text
                    }
                    if ($countValues.total -ne $cases.Count -or $countValues.executed -ne $cases.Count -or
                        $countValues.passed -ne @($cases | Where-Object Passed).Count -or $countValues.failed -ne 0 -or
                        $countValues.notExecuted -ne 0) { $failures.Add('CounterMismatch') }
                    foreach ($field in @('error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','disconnected','warning','inProgress','pending')) {
                        if ($counters[0].HasAttribute($field) -and $counters[0].GetAttribute($field) -cne '0') { $failures.Add('NonPassingRunCounter') }
                    }
                }
                else {
                    $json=[Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF) | ConvertFrom-Json -ErrorAction Stop
                    if ($null -eq $json -or $json -is [array]) { throw 'Invalid native JSON root.' }
                    if ((Get-Cp6ResultField $json 'Provider') -cne $Provider) { $failures.Add('NativeProviderMismatch') }
                    $reportDatabaseName=Get-Cp6ResultField $json 'DatabaseName'
                    $contract=Get-Cp6ResultField $Entry 'ResultContract'
                    if ($null -ne $contract) {
                        $contractNames=if ($contract -is [System.Collections.IDictionary]) { @($contract.Keys) } else { @($contract.PSObject.Properties.Name) }
                        foreach ($name in $contractNames) {
                            $expectedValue=Get-Cp6ResultField $contract $name
                            $actual=Get-Cp6ResultField $json $name
                            if ($null -eq $expectedValue) { if ($null -ne $actual) { $failures.Add('NativeContractMismatch') } }
                            elseif ($actual -isnot [string] -or $actual -cne $expectedValue) { $failures.Add('NativeContractMismatch') }
                        }
                    }
                    if ($Entry.EntryKind -ceq 'ApplicationChecks') {
                        $schema=Get-Cp6ResultField $json 'SchemaVersion'
                        if (($schema -isnot [int] -and $schema -isnot [long]) -or $schema -ne 1 -or
                            (Get-Cp6ResultField $json 'Task') -cne 'DB-COMPAT-01-WP6' -or
                            (Get-Cp6ResultField $json 'Status') -cne 'Passed' -or
                            $reportDatabaseName -isnot [string] -or [string]::IsNullOrWhiteSpace($reportDatabaseName)) { $failures.Add('NativeContractMismatch') }
                        $rows=Get-Cp6ResultField $json 'Assertions'
                        if ($rows -isnot [array]) { throw 'Application Assertions array is required.' }
                        foreach ($row in $rows) {
                            $name=Get-Cp6ResultField $row 'Name'; $passed=Get-Cp6ResultField $row 'Passed'
                            if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name) -or $passed -isnot [bool]) { throw 'Invalid application assertion.' }
                            $cases.Add([pscustomobject]@{Name=$name;Passed=$passed;Outcome=$(if($passed){'Passed'}else{'Failed'})})
                            foreach ($pair in @(@('ExpectedSha256','ActualSha256'),@('ExpectedCount','ActualCount'))) {
                                $expectedValue=Get-Cp6ResultField $row $pair[0]; $actualValue=Get-Cp6ResultField $row $pair[1]
                                if ($null -eq $expectedValue -and $null -eq $actualValue) { continue }
                                if ($null -eq $expectedValue -or $null -eq $actualValue -or $expectedValue -cne $actualValue) { $failures.Add('AssertionValueMismatch') }
                                if ($pair[0] -ceq 'ExpectedCount') {
                                    if (($expectedValue -isnot [int] -and $expectedValue -isnot [long]) -or
                                        ($actualValue -isnot [int] -and $actualValue -isnot [long]) -or $expectedValue -lt 0 -or $actualValue -lt 0) { throw 'Invalid assertion count.' }
                                }
                                elseif ($expectedValue -isnot [string] -or $actualValue -isnot [string] -or
                                    $expectedValue -notmatch '\A[0-9a-fA-F]{64}\z' -or $actualValue -notmatch '\A[0-9a-fA-F]{64}\z') { throw 'Invalid assertion hash.' }
                            }
                        }
                        $requiredCounts=Get-Cp6ResultField $Entry 'RequiredCountFields'
                        foreach ($name in $requiredCounts) {
                            $value=Get-Cp6ResultField (Get-Cp6ResultField $json 'Counts') $name
                            if (($value -isnot [int] -and $value -isnot [long]) -or $value -lt 0) { throw 'Invalid application count.' }
                        }
                        $expectedCounts=Get-Cp6ResultField $Entry 'ExpectedCounts'
                        if ($null -ne $expectedCounts) {
                            $countNames=if ($expectedCounts -is [System.Collections.IDictionary]) { @($expectedCounts.Keys) } else { @($expectedCounts.PSObject.Properties.Name) }
                            foreach ($name in $countNames) {
                                $value=Get-Cp6ResultField (Get-Cp6ResultField $json 'Counts') $name
                                if ($value -cne (Get-Cp6ResultField $expectedCounts $name)) { $failures.Add('CounterMismatch') }
                            }
                        }
                        $requiredHashes=Get-Cp6ResultField $Entry 'RequiredHashFields'
                        foreach ($name in $requiredHashes) {
                            $value=Get-Cp6ResultField (Get-Cp6ResultField $json 'Hashes') $name
                            if ($value -isnot [string] -or $value -notmatch '\A[0-9a-fA-F]{64}\z') { throw 'Invalid application hash.' }
                        }
                        $equivalentHashes=Get-Cp6ResultField $Entry 'EquivalentHashFields'
                        foreach ($pair in $equivalentHashes) {
                            if ($pair -isnot [array] -or $pair.Count -ne 2) { throw 'Invalid hash equality contract.' }
                            $left=Get-Cp6ResultField (Get-Cp6ResultField $json 'Hashes') $pair[0]
                            $right=Get-Cp6ResultField (Get-Cp6ResultField $json 'Hashes') $pair[1]
                            if ($left -cne $right) { $failures.Add('AssertionValueMismatch') }
                        }
                    }
                    elseif ($Entry.EntryKind -ceq 'IdentityChecks') {
                        if ((Get-Cp6ResultField $json 'schemaId') -cne 'cp6.c02.producer-provider-verification.v1' -or
                            (Get-Cp6ResultField $json 'conclusion') -cne 'success') { $failures.Add('NativeContractMismatch') }
                        $rows=Get-Cp6ResultField $json 'cases'
                        if ($rows -isnot [array]) { throw 'Native cases array is required.' }
                        foreach ($row in $rows) {
                            $name=Get-Cp6ResultField $row 'name'; $passed=Get-Cp6ResultField $row 'passed'
                            if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name) -or $passed -isnot [bool]) { throw 'Invalid native case.' }
                            $cases.Add([pscustomobject]@{Name=$name;Passed=$passed;Outcome=$(if($passed){'Passed'}else{'Failed'})})
                        }
                        foreach ($field in @('expectedCases','total','passed','failed','skipped')) {
                            $value=Get-Cp6ResultField $json $field
                            if (($value -isnot [int] -and $value -isnot [long]) -or $value -lt 0) { throw 'Invalid native counter.' }
                        }
                        if ($json.expectedCases -ne $Entry.ExpectedCases -or $json.total -ne $cases.Count -or
                            $json.passed -ne @($cases | Where-Object Passed).Count -or $json.failed -ne 0 -or $json.skipped -ne 0) { $failures.Add('CounterMismatch') }
                    }
                    else {
                        $rows=Get-Cp6ResultField $json 'Checks'
                        if ($rows -isnot [array]) { throw 'Native Checks array is required.' }
                        foreach ($row in $rows) {
                            $name=Get-Cp6ResultField $row 'Name'
                            if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name)) { throw 'Invalid native check name.' }
                            if ($Entry.EntryKind -ceq 'NamedJsonChecks') {
                                $passed=Get-Cp6ResultField $row 'Passed'
                                if ($passed -isnot [bool]) { throw 'Native Passed must be a boolean.' }
                                $outcome=if($passed){'Passed'}else{'Failed'}
                            }
                            else {
                                $outcome=Get-Cp6ResultField $row 'Status'
                                if ($outcome -isnot [string] -or [string]::IsNullOrWhiteSpace($outcome)) { throw 'Invalid native check status.' }
                                $passed=$outcome -ceq 'Passed'
                            }
                            $cases.Add([pscustomobject]@{Name=$name;Passed=$passed;Outcome=$outcome})
                        }
                    }
                }
            }
            catch { $failures.Add('ReportMalformed') }
        }
        if ($cases.Count -eq 0) { $failures.Add('ZeroCases') }
        if ($cases.Count -ne $Entry.ExpectedCases) { $failures.Add('CaseCountMismatch') }
        $required=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($name in $Entry.RequiredCaseNames) { $null=$required.Add($name) }
        $actualNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($case in $cases) {
            if (!$actualNames.Add($case.Name)) { $duplicates.Add($case.Name) }
            if (!$required.Contains($case.Name)) { $unexpected.Add($case.Name) }
        }
        foreach ($name in $Entry.RequiredCaseNames) { if (!$actualNames.Contains($name)) { $missing.Add($name) } }
        if ($duplicates.Count -gt 0) { $failures.Add('DuplicateCase') }
        if ($missing.Count -gt 0) { $failures.Add('MissingCase') }
        if ($unexpected.Count -gt 0) { $failures.Add('UnexpectedCase') }
        if (@($cases | Where-Object { !$_.Passed }).Count -gt 0) { $failures.Add('NonPassingCase') }
    }
    [pscustomobject][ordered]@{
        EntryId=Get-Cp6ResultField $Entry 'Id'; Provider=$Provider; Success=($failures.Count -eq 0)
        Status=$(if($failures.Count -eq 0){'Passed'}else{'Failed'})
        FailureCodes=@($failures | Select-Object -Unique)
        Counts=[ordered]@{Expected=Get-Cp6ResultField $Entry 'ExpectedCases';Total=$cases.Count;Passed=@($cases | Where-Object Passed).Count;NonPassing=@($cases | Where-Object { !$_.Passed }).Count}
        MissingCaseNames=@($missing.ToArray()); UnexpectedCaseNames=@($unexpected | Select-Object -Unique); DuplicateCaseNames=@($duplicates | Select-Object -Unique)
        ActualCaseNames=@($cases | ForEach-Object Name); ReportSha256=$hash
        ReportDatabaseName=$reportDatabaseName
        VerificationKind='Offline report validation only; process exit code is supplied by the caller.'
    }
}

Export-ModuleMember -Function Read-Cp6RequiredCaseManifest,Test-Cp6RequiredResults
