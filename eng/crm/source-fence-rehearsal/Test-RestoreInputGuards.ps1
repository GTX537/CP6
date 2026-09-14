[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'New-SourceRestore.ps1'
. (Join-Path $PSScriptRoot 'LocalStorage.ps1')
$saved = $env:C04A_SOURCE_CONNECTION
$results = [Collections.Generic.List[object]]::new()
function Rejected([string]$Name, [string]$Connection, [string]$Server, [string]$Database, [string]$Destination, [string]$ExpectedError) {
    $env:C04A_SOURCE_CONNECTION = $Connection
    $caught = $false
    try { & $script -ExpectedServer $Server -ExpectedDatabase $Database -OutputDirectory $Destination } catch {
        if ($_.Exception.Message -notlike $ExpectedError) { throw "Unexpected refusal for $Name" }
        $caught = $true
    }
    if (!$caught) { throw "Unsafe input was accepted: $Name" }
    $results.Add(@{ name = $Name; passed = $true; databaseConnected = $false })
}
try {
    $unused = Join-Path ([IO.Path]::GetTempPath()) ('CP6_C04A_InputGuard_' + [Guid]::NewGuid().ToString('N'))
    Rejected missing-connection '' localhost CP6DB $unused '*identity does not match*'
    Rejected wrong-database 'Server=localhost;Database=Different;Integrated Security=True' localhost CP6DB $unused '*identity does not match*'
    Rejected wrong-server 'Server=127.0.0.1;Database=CP6DB;Integrated Security=True' localhost CP6DB $unused '*identity does not match*'
    Rejected system-database 'Server=localhost;Database=master;Integrated Security=True' localhost master $unused '*identity does not match*'
    Rejected rehearsal-as-source 'Server=localhost;Database=CP6_C04A_Rehearsal_invalid;Integrated Security=True' localhost CP6_C04A_Rehearsal_invalid $unused '*identity does not match*'
    Rejected remote-server 'Server=remote.invalid;Database=CP6DB;Integrated Security=True' remote.invalid CP6DB $unused '*only supports a local SQL Server*'
    Rejected existing-output 'Server=localhost;Database=CP6DB;Integrated Security=True' localhost CP6DB $PSScriptRoot '*must not already exist*'
    foreach ($path in @('\\remote.invalid\share\backup', '\\?\C:\backup', '\\.\C:\backup', 'relative\backup', 'C:relative')) {
        $caught = $false
        try { Assert-LocalStorageDirectory $path } catch {
            if ($_.Exception.Message -notlike '*absolute local fixed-drive*') { throw 'Unexpected storage refusal.' }
            $caught = $true
        }
        if (!$caught) { throw 'Non-local storage input was accepted.' }
        $results.Add(@{ name = 'nonlocal-storage-' + $results.Count; passed = $true; databaseConnected = $false })
    }
    Assert-LocalStorageDirectory ([IO.Path]::GetTempPath())
    $results.Add(@{ name = 'existing-local-fixed-drive-accepted'; passed = $true; databaseConnected = $false })
    if (Test-Path -LiteralPath $unused) { throw 'Rejected input created output resources.' }
    @{ status = 'Passed'; checks = @($results.ToArray()); createdOutputResources = $false } | ConvertTo-Json -Depth 5
} finally { $env:C04A_SOURCE_CONNECTION = $saved }
