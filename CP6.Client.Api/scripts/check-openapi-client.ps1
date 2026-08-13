param(
    [Parameter(Mandatory = $true)]
    [string]$SwaggerUrl,
    [switch]$Update
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$hashFile = Join-Path $projectRoot "openapi\client-surface.sha256"
$tests = Join-Path $PSScriptRoot "check-openapi-client.test.mjs"
$checker = Join-Path $PSScriptRoot "check-openapi-client.mjs"

& node --test $tests
if ($LASTEXITCODE -ne 0) {
    throw "OpenAPI client surface unit tests failed with exit code $LASTEXITCODE."
}

$arguments = @(
    $checker,
    "--swagger-url", $SwaggerUrl,
    "--hash-file", $hashFile
)
if ($Update) {
    $arguments += "--update"
}

& node @arguments
if ($LASTEXITCODE -ne 0) {
    throw "OpenAPI client surface check failed with exit code $LASTEXITCODE."
}
