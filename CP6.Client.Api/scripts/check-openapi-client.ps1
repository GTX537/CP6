param(
    [Parameter(Mandatory = $true)]
    [string]$SwaggerUrl,
    [switch]$Update
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$hashFile = Join-Path $projectRoot "openapi\client-surface.sha256"
$document = Invoke-RestMethod -Uri $SwaggerUrl
$selected = [ordered]@{}
foreach ($property in $document.paths.PSObject.Properties | Sort-Object Name) {
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
$canonical = $surface | ConvertTo-Json -Depth 100 -Compress
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
