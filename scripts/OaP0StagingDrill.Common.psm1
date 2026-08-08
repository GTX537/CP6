Set-StrictMode -Version Latest

$script:OaP0DatabasePattern = '^CP6OaP0Stage_[0-9]{14}_[0-9a-f]{8}$'
$script:OaP0ContainerBackupPattern =
    '^/var/opt/mssql/backup/oa-p0-stage-[0-9]{14}-[0-9a-f]{8}\.bak$'

function Test-OaP0StageDatabaseName {
    [CmdletBinding()]
    param([AllowNull()][string]$DatabaseName)

    return -not [string]::IsNullOrWhiteSpace($DatabaseName) -and
        $DatabaseName -cmatch $script:OaP0DatabasePattern
}

function New-OaP0StageIdentity {
    [CmdletBinding()]
    param(
        [datetime]$UtcNow = [datetime]::UtcNow,
        [string]$HexSuffix = ([guid]::NewGuid().ToString('N').Substring(0, 8))
    )

    if ($HexSuffix -cnotmatch '^[0-9a-f]{8}$') {
        throw 'OA P0 stage suffix must be exactly eight lowercase hexadecimal characters.'
    }

    $stamp = $UtcNow.ToUniversalTime().ToString(
        'yyyyMMddHHmmss',
        [System.Globalization.CultureInfo]::InvariantCulture)
    $databaseName = "CP6OaP0Stage_${stamp}_${HexSuffix}"
    if (-not (Test-OaP0StageDatabaseName $databaseName)) {
        throw "Generated OA P0 database name failed validation: $databaseName"
    }

    [pscustomobject]@{
        RunId = "${stamp}_${HexSuffix}"
        DatabaseName = $databaseName
        ContainerBackupPath = "/var/opt/mssql/backup/oa-p0-stage-${stamp}-${HexSuffix}.bak"
    }
}

function Test-OaP0ContainerBackupPath {
    [CmdletBinding()]
    param([AllowNull()][string]$ContainerBackupPath)

    return -not [string]::IsNullOrWhiteSpace($ContainerBackupPath) -and
        $ContainerBackupPath -cmatch $script:OaP0ContainerBackupPattern
}

function ConvertFrom-OaP0SqlCmdTable {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $normalized = @(
        $Lines |
            ForEach-Object { ([string]$_).TrimEnd() } |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                $_ -notmatch '^\([0-9]+ rows? affected\)$' -and
                $_ -notmatch '^Changed database context to '
            }
    )
    $separatorIndex = -1
    for ($index = 1; $index -lt $normalized.Count; $index++) {
        if ($normalized[$index] -match '^\s*-+(\s*\^\s*-+)+\s*$') {
            $separatorIndex = $index
            break
        }
    }
    if ($separatorIndex -lt 1) {
        throw 'sqlcmd output did not contain a delimited header and separator row.'
    }

    $headers = @($normalized[$separatorIndex - 1] -split '\^' | ForEach-Object { $_.Trim() })
    if ($headers.Count -eq 0 -or $headers | Where-Object { [string]::IsNullOrWhiteSpace($_) }) {
        throw 'sqlcmd output contained an empty column name.'
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    for ($index = $separatorIndex + 1; $index -lt $normalized.Count; $index++) {
        if ($normalized[$index] -notmatch '\^') {
            continue
        }

        $values = @($normalized[$index] -split '\^' | ForEach-Object { $_.Trim() })
        if ($values.Count -ne $headers.Count) {
            throw "sqlcmd row had $($values.Count) values for $($headers.Count) columns."
        }

        $record = [ordered]@{}
        for ($column = 0; $column -lt $headers.Count; $column++) {
            $record[$headers[$column]] = $values[$column]
        }
        $rows.Add([pscustomobject]$record)
    }

    return @($rows)
}

function Assert-OaP0BackfillHasNoErrors {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Report)

    $categories = @(
        'FlowVersions',
        'FormVersions',
        'FlowPins',
        'FormDataPins',
        'Bindings',
        'Dependencies',
        'Drafts'
    )
    foreach ($category in $categories) {
        $property = $Report.PSObject.Properties[$category]
        if ($null -eq $property -or $null -eq $property.Value) {
            throw "Backfill report is missing category '$category'."
        }
        $errorsProperty = $property.Value.PSObject.Properties['Errors']
        if ($null -eq $errorsProperty) {
            throw "Backfill category '$category' is missing Errors."
        }
        if ([int]$errorsProperty.Value -ne 0) {
            throw "Backfill category '$category' reported errors."
        }
    }
}

function Assert-OaP0SecondBackfillIsIdempotent {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Report)

    Assert-OaP0BackfillHasNoErrors -Report $Report
    $categories = @(
        'FlowVersions',
        'FormVersions',
        'FlowPins',
        'FormDataPins',
        'Bindings',
        'Dependencies',
        'Drafts'
    )
    foreach ($category in $categories) {
        $insertedProperty = $Report.$category.PSObject.Properties['Inserted']
        if ($null -eq $insertedProperty) {
            throw "Backfill category '$category' is missing Inserted."
        }
        if ([int]$insertedProperty.Value -ne 0) {
            throw "Second backfill inserted rows in category '$category'."
        }
    }
}

function Assert-OaP0CleanupState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$State)

    foreach ($required in @('runId', 'databaseName', 'containerBackupPath')) {
        if ($null -eq $State.PSObject.Properties[$required] -or
            [string]::IsNullOrWhiteSpace([string]$State.$required)) {
            throw "Cleanup state is missing '$required'."
        }
    }

    if (-not (Test-OaP0StageDatabaseName ([string]$State.databaseName))) {
        throw 'Cleanup state database name is unsafe.'
    }
    if (-not (Test-OaP0ContainerBackupPath ([string]$State.containerBackupPath))) {
        throw 'Cleanup state container backup path is unsafe.'
    }
    $expectedRunId = ([string]$State.databaseName).Substring('CP6OaP0Stage_'.Length)
    if ([string]$State.runId -cne $expectedRunId) {
        throw 'Cleanup state run identifier does not match the database name.'
    }
    $expectedContainerSuffix = ([string]$State.runId).Replace('_', '-')
    if ([string]$State.containerBackupPath -cne
        "/var/opt/mssql/backup/oa-p0-stage-$expectedContainerSuffix.bak") {
        throw 'Cleanup state run identifier does not match the copied backup path.'
    }
}

Export-ModuleMember -Function @(
    'Test-OaP0StageDatabaseName',
    'New-OaP0StageIdentity',
    'Test-OaP0ContainerBackupPath',
    'ConvertFrom-OaP0SqlCmdTable',
    'Assert-OaP0BackfillHasNoErrors',
    'Assert-OaP0SecondBackfillIsIdempotent',
    'Assert-OaP0CleanupState'
)
