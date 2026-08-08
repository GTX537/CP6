param(
    [Parameter(Mandatory = $true)][string]$KeyStore,
    [Parameter(Mandatory = $true)][string]$KeyAlias,
    [string]$StorePasswordEnvironmentVariable = "CP6_ANDROID_STORE_PASSWORD",
    [string]$KeyPasswordEnvironmentVariable = "CP6_ANDROID_KEY_PASSWORD"
)

$ErrorActionPreference = "Stop"

if ($StorePasswordEnvironmentVariable -notmatch "^[A-Za-z_][A-Za-z0-9_]*$") {
    throw "StorePasswordEnvironmentVariable is not a valid environment variable name."
}
if ($KeyPasswordEnvironmentVariable -notmatch "^[A-Za-z_][A-Za-z0-9_]*$") {
    throw "KeyPasswordEnvironmentVariable is not a valid environment variable name."
}
if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($StorePasswordEnvironmentVariable))) {
    throw "Set the $StorePasswordEnvironmentVariable environment variable before publishing."
}
if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($KeyPasswordEnvironmentVariable))) {
    throw "Set the $KeyPasswordEnvironmentVariable environment variable before publishing."
}

$resolvedKeyStore = (Resolve-Path -LiteralPath $KeyStore -ErrorAction Stop).Path
$project = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $project "CP6.Mobile.csproj"
$publishArguments = @(
    "publish", $projectFile,
    "-f", "net10.0-android",
    "-c", "Release",
    "/p:AndroidPackageFormat=apk",
    "/p:AndroidKeyStore=true",
    "/p:AndroidSigningKeyStore=$resolvedKeyStore",
    "/p:AndroidSigningKeyAlias=$KeyAlias",
    "/p:AndroidSigningStorePass=env:$StorePasswordEnvironmentVariable",
    "/p:AndroidSigningKeyPass=env:$KeyPasswordEnvironmentVariable"
)

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "Android Release publish failed with exit code $LASTEXITCODE."
}

$apk = Get-ChildItem (Join-Path $project "bin\Release\net10.0-android\publish") `
    -Filter "*-Signed.apk" |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($null -eq $apk) { throw "Signed APK was not produced." }
Get-FileHash -Algorithm SHA256 $apk.FullName
