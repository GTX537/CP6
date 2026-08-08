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
$previousConnection = $env:ConnectionStrings__DefaultConnection
$previousFileRoot = $env:Space__Files__RootPath
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:Startup__SkipDatabaseInitialization = "true"
$env:Startup__SkipHostedServices = "true"
$env:ConnectionStrings__DefaultConnection =
    "Server=(localdb)\MSSQLLocalDB;Database=unused;Trusted_Connection=True;TrustServerCertificate=True"
$env:Space__Files__RootPath = Join-Path $projectRoot "tmp\space-openapi-files"

$api = $null
try {
    $api = Start-Process dotnet -ArgumentList @(
        $apiDll,
        "--urls", $Url
    ) -WindowStyle Hidden -PassThru
    $swaggerUrl = "$($Url.TrimEnd('/'))/swagger/v1/swagger.json"
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            Invoke-WebRequest $swaggerUrl -UseBasicParsing -TimeoutSec 2 |
                Out-Null
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
    $env:ConnectionStrings__DefaultConnection = $previousConnection
    $env:Space__Files__RootPath = $previousFileRoot
}
