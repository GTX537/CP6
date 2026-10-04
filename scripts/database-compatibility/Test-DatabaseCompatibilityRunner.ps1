# Offline command planning only. Never launches .NET, HTTP, native database tools, or cleanup.
[CmdletBinding()]
param([string]$RepositoryRoot=(Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$runner=Join-Path $RepositoryRoot 'scripts/Test-Cp6DatabaseCompatibility.ps1'
$manifest=Get-Content -LiteralPath (Join-Path $RepositoryRoot 'eng/database-compatibility/required-cases.json') -Raw | ConvertFrom-Json
$script:assertions=0
function Assert-Plan([bool]$Condition,[string]$Name){if(!$Condition){throw ('Offline runner assertion failed: '+$Name)};$script:assertions++}
$plans=@()
foreach($selectedProvider in @('SqlServer','PostgreSql')){
    foreach($selectedPhase in @('Full','Matrix','Application')){
        $unused=Join-Path ([IO.Path]::GetTempPath()) ('cp6-plan-only-'+[guid]::NewGuid().ToString('N'))
        $plan=& $runner -Provider $selectedProvider -Phase $selectedPhase -RunDirectory $unused -PlanOnly
        $expected=@($manifest.Entries | Where-Object {
            $_.ProviderEligibility -ccontains $selectedProvider -and
            ($selectedPhase -ceq 'Full' -or ($selectedPhase -ceq 'Matrix' -and $_.Role -cnotin @('application','restore')) -or
            ($selectedPhase -ceq 'Application' -and $_.Role -cin @('application','restore')))
        })
        Assert-Plan (!$plan.NativeExecution -and !$plan.FullAcceptance -and $plan.Status -ceq 'Planned') 'planning never claims native acceptance'
        Assert-Plan (!(Test-Path -LiteralPath $unused)) 'planning creates no run directory'
        Assert-Plan (@(Compare-Object @($expected.Id) @($plan.RequiredEntryIds) -CaseSensitive).Count -eq 0) 'provider and phase exact entry selection'
        Assert-Plan (@($plan.RequiredEntryIds | Sort-Object -Unique).Count -eq $plan.RequiredEntryIds.Count) 'each required entry selected once'
        Assert-Plan ($plan.Sql136BaselineRequired -eq ($selectedProvider -ceq 'SqlServer' -and $selectedPhase -cne 'Application')) 'historical SQL baseline selected only for SQL matrix'
        $fresh=@($expected | Where-Object DatabaseLifecycle -CEQ 'FreshPerEntry' | ForEach-Object Id)
        Assert-Plan (@(Compare-Object $fresh @($plan.FreshPerEntryIds) -CaseSensitive).Count -eq 0) 'fresh receipts required for every isolated case'
        if($selectedPhase -cne 'Application'){
            foreach($pair in @(@('reports-empty','reports-parity'),@('runtime-initialize','runtime-orders'),@('oidc-provider','identity-provider'))){
                Assert-Plan ([array]::IndexOf($plan.RequiredEntryIds,$pair[0]) -lt [array]::IndexOf($plan.RequiredEntryIds,$pair[1])) ('matrix order '+($pair -join ' before '))
            }
        }
        if($selectedPhase -cne 'Matrix'){
            foreach($pair in @(
                @('database-init-first','application-seed-prepare'),@('application-seed-prepare','database-init-repeat'),
                @('database-init-repeat','application-seed-verify'),@('api-first-stop','application-notification-enqueue'),
                @('application-notification-queued','api-worker-restart-and-health'),@('application-notification-dispatched','api-worker-stop'),
                @('api-third-start-and-health','application-signalr-user-delivery'),@('application-signalr-user-delivery','asset-cursor-near-backup'),
                @('asset-cursor-near-backup','api-before-backup-stop'),@('api-before-backup-stop','application-restore-pending-enqueue'),
                @('application-restore-pending-queued','application-state-capture'),@('application-state-capture','native-backup'),
                @('application-seed-capture','native-backup'),@('native-backup','new-independent-restore-database'),
                @('native-restore','restore-state-verify'),@('restore-state-verify','restored-api-start-and-health'),
                @('restore-histories','restored-api-start-and-health'),@('restored-api-start-and-health','restore-notification-dispatched'),
                @('restore-notification-dispatched','restore-notification-replay'),@('restore-notification-replay','original-cursor-permissions-and-notifications'),
                @('original-cursor-permissions-and-notifications','restored-api-stop')
            )){
                $first=[array]::IndexOf($plan.ApplicationFlow,$pair[0]);$second=[array]::IndexOf($plan.ApplicationFlow,$pair[1])
                Assert-Plan ($first -ge 0 -and $second -gt $first) ('application order '+($pair -join ' before '))
            }
            Assert-Plan ($plan.RequiredEntryIds -cnotcontains 'restore-seed-verify') 'same-database seed verifier not used against restored database'
            Assert-Plan ($plan.Projects -ccontains 'CP6.WebApi/CP6.WebApi.csproj') 'application build includes actual API'
        }else{Assert-Plan (@($plan.ApplicationFlow).Count -eq 0) 'matrix contains no application execution'}
        $plans+=@([pscustomobject]@{Provider=$selectedProvider;Phase=$selectedPhase;Entries=$plan.RequiredEntryIds.Count;Fresh=$plan.FreshPerEntryIds.Count})
    }
}
$oidc=@($manifest.Entries | Where-Object Id -CLike 'oidc-history-*')
Assert-Plan ($oidc.Count -eq 2) 'two explicit historical OIDC methods'
foreach($entry in $oidc){Assert-Plan ($entry.ExpectedCases -eq 1 -and $entry.DatabaseLifecycle -ceq 'FreshPerEntry' -and $entry.Role -ceq 'sqlupgrade' -and $entry.ProviderEligibility.Count -eq 1 -and $entry.ProviderEligibility[0] -ceq 'SqlServer') 'OIDC historical method has its own empty SQL database'}
$firstSeed=$manifest.Entries | Where-Object Id -CEQ 'application-seed-prepare'
$capturedSeed=$manifest.Entries | Where-Object Id -CEQ 'application-seed-capture'
Assert-Plan ($firstSeed.CaseCommand -ccontains '{SeedStatePath}' -and $capturedSeed.CaseCommand -ccontains '{CapturedSeedStatePath}') 'initial and final seed captures use distinct state files'
$tokens=$null;$errors=$null
$null=[System.Management.Automation.Language.Parser]::ParseFile($runner,[ref]$tokens,[ref]$errors)
Assert-Plan ($errors.Count -eq 0) 'formal runner PowerShell parsing'
[pscustomobject]@{Status='Passed';Scope='Offline command planning only; no native process, HTTP, or database execution';Assertions=$script:assertions;Plans=$plans;RunnerSha256=(Get-FileHash -LiteralPath $runner -Algorithm SHA256).Hash;ManifestSha256=(Get-FileHash -LiteralPath (Join-Path $RepositoryRoot 'eng/database-compatibility/required-cases.json') -Algorithm SHA256).Hash}
