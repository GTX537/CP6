param(
    [Parameter(Mandatory = $true)]
    [string]$SwaggerUrl,
    [switch]$Update
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$hashFile = Join-Path $projectRoot "openapi\client-surface.sha256"

function ConvertTo-CanonicalValue {
    param(
        [AllowNull()]
        [object]$Value
    )

    if ($null -eq $Value `
        -or $Value -is [string] `
        -or $Value -is [ValueType]) {
        return $Value
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        $keys = [string[]]@($Value.Keys | ForEach-Object { [string]$_ })
        [Array]::Sort($keys, [StringComparer]::Ordinal)
        foreach ($key in $keys) {
            $result[$key] = ConvertTo-CanonicalValue $Value[$key]
        }
        return $result
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        $items = @()
        foreach ($item in $Value) {
            $items += ,(ConvertTo-CanonicalValue $item)
        }
        return ,$items
    }

    $propertyNames = [string[]]@(
        $Value.PSObject.Properties |
            Where-Object { $_.MemberType -in @("NoteProperty", "Property") } |
            ForEach-Object { $_.Name }
    )
    if ($propertyNames.Count -gt 0) {
        [Array]::Sort($propertyNames, [StringComparer]::Ordinal)
        $result = [ordered]@{}
        foreach ($name in $propertyNames) {
            $result[$name] = ConvertTo-CanonicalValue `
                $Value.PSObject.Properties[$name].Value
        }
        return $result
    }

    return $Value
}

$document = Invoke-RestMethod -Uri $SwaggerUrl
$selected = [ordered]@{}
foreach ($property in $document.paths.PSObject.Properties) {
    if ($property.Name -like "/api/client-auth/*" `
        -or $property.Name -eq "/api/client/bootstrap" `
        -or $property.Name -like "/api/client/devices/*" `
        -or $property.Name -like "/api/v2/wms/tasks*" `
        -or $property.Name -like "/api/v2/wms/label-jobs*") {
        $selected[$property.Name] = $property.Value
    }
}
$surface = [ordered]@{
    paths = $selected
    schemas = $document.components.schemas
}
$canonical = ConvertTo-CanonicalValue $surface |
    ConvertTo-Json -Depth 100 -Compress
$bytes = [Text.Encoding]::UTF8.GetBytes($canonical)
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "")
}
finally {
    $sha.Dispose()
}

if ($Update) {
    Set-Content -LiteralPath $hashFile -Value $hash -NoNewline
    Write-Host "Updated client surface hash: $hash"
    exit 0
}

$expected = (Get-Content -LiteralPath $hashFile -Raw).Trim()
if ($expected -ne $hash) {
    throw "OpenAPI client drift detected. Expected $expected, actual $hash. Regenerate the typed client and rerun with -Update."
}
Write-Host "OpenAPI client surface is in sync: $hash"
