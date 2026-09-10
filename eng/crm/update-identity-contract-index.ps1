param([switch]$Check)
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '../../contracts/events/platform'
$entries = foreach ($name in @('tenant.changed', 'user.changed', 'department.changed', 'permission.changed', 'token.revoked')) {
    $slug = $name.Replace('.', '-')
    $schema = "$slug/v1/schema.json"
    $examples = foreach ($case in @('valid', 'missing-required', 'unknown-optional', 'wrong-type', 'pii-negative')) {
        $path = "$slug/v1/examples/$case.json"
        [ordered]@{ name = $case; path = $path; valid = $case -in @('valid', 'unknown-optional'); sha256 = (Get-FileHash -LiteralPath (Join-Path $root $path) -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    [ordered]@{ eventType = "com.gtx537.platform.$name.v1"; schemaVersion = '1.0.0'; schemaId = "https://contracts.cp6.uk/events/platform/$slug/v1/schema.json"; schemaPath = $schema; schemaSha256 = (Get-FileHash -LiteralPath (Join-Path $root $schema) -Algorithm SHA256).Hash.ToLowerInvariant(); examples = @($examples) }
}
$manifest = [ordered]@{ bundleVersion = '1.0.0'; cloudEventsSpecVersion = '1.0'; jsonSchemaDialect = 'https://json-schema.org/draft/2020-12/schema'; entries = @($entries) }
$bytes = ($manifest | ConvertTo-Json -Depth 10).Replace("`r`n", "`n") + "`n"
$target = Join-Path $root 'contract-bundle.v1.json'
if ($Check) {
    if (!(Test-Path -LiteralPath $target) -or [IO.File]::ReadAllText($target) -cne $bytes) { throw 'C02 contract bundle hashes are stale.' }
} else { [IO.File]::WriteAllText($target, $bytes, [Text.UTF8Encoding]::new($false)) }
