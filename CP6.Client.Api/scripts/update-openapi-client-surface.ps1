[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [string]$Url = "http://127.0.0.1:5080"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$apiDll = Join-Path $projectRoot "CP6.WebApi\bin\$Configuration\net8.0\CP6.WebApi.dll"
if (-not (Test-Path -LiteralPath $apiDll)) {
    throw "Web API build output was not found: $apiDll"
}
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousSkipDatabase = $env:Startup__SkipDatabaseInitialization
$previousSkipHosted = $env:Startup__SkipHostedServices
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Startup__SkipDatabaseInitialization = "true"
$env:Startup__SkipHostedServices = "true"

$api = $null
try {
    $api = Start-Process dotnet -ArgumentList @(
        $apiDll,
        "--urls", $Url
    ) -WindowStyle Hidden -PassThru
    $swaggerUrl = "$($Url.TrimEnd('/'))/swagger/v1/swagger.json"
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            Invoke-WebRequest $swaggerUrl -UseBasicParsing | Out-Null
            break
        }
        catch {
            if ($attempt -eq 39) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
    & (Join-Path $PSScriptRoot "check-openapi-client.ps1") `
        -SwaggerUrl $swaggerUrl `
        -Update
}
finally {
    if ($null -ne $api) {
        Stop-Process -Id $api.Id -ErrorAction SilentlyContinue
    }
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:Startup__SkipDatabaseInitialization = $previousSkipDatabase
    $env:Startup__SkipHostedServices = $previousSkipHosted
}
