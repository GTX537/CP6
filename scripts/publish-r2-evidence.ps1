[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string[]]$Paths,
    [Parameter(Mandatory = $true)][string]$EvidenceRootUri,
    [Parameter(Mandatory = $true)][uri]$EndpointUrl,
    [ValidateRange(1, 3650)][int]$RetentionDays = 365
)

$ErrorActionPreference = "Stop"
if ($EndpointUrl.Scheme -ne [Uri]::UriSchemeHttps) {
    throw "EndpointUrl must use HTTPS."
}
$root = $EvidenceRootUri -as [Uri]
if ($null -eq $root -or $root.Scheme -ne "s3" -or
    [string]::IsNullOrWhiteSpace($root.Host)) {
    throw "EvidenceRootUri must be an s3:// URI."
}

$bucket = $root.Host
$prefix = $root.AbsolutePath.Trim("/")
$retainUntil = [DateTimeOffset]::UtcNow.AddDays($RetentionDays).ToString("O")
$versioningJson = & aws --endpoint-url $EndpointUrl.AbsoluteUri `
    s3api get-bucket-versioning --bucket $bucket --output json 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Evidence bucket versioning check failed."
}
$versioning = ($versioningJson | Out-String) | ConvertFrom-Json
if ([string]$versioning.Status -ne "Enabled") {
    throw "Evidence bucket versioning must be Enabled."
}
$lockJson = & aws --endpoint-url $EndpointUrl.AbsoluteUri `
    s3api get-object-lock-configuration --bucket $bucket --output json 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Evidence bucket Object Lock check failed."
}
$lock = ($lockJson | Out-String) | ConvertFrom-Json
if ([string]$lock.ObjectLockConfiguration.ObjectLockEnabled -ne "Enabled") {
    throw "Evidence bucket Object Lock must be Enabled."
}

$published = @()
foreach ($path in $Paths) {
    $file = Get-Item -LiteralPath (
        Resolve-Path -LiteralPath $path -ErrorAction Stop
    ).Path
    if ($file.Length -le 0) {
        throw "Evidence file '$($file.Name)' is empty."
    }
    $key = if ([string]::IsNullOrWhiteSpace($prefix)) {
        $file.Name
    }
    else {
        "$prefix/$($file.Name)"
    }
    & aws --endpoint-url $EndpointUrl.AbsoluteUri s3api put-object `
        --bucket $bucket `
        --key $key `
        --body $file.FullName `
        --server-side-encryption AES256 `
        --object-lock-mode COMPLIANCE `
        --object-lock-retain-until-date $retainUntil `
        --checksum-algorithm SHA256 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Evidence upload failed for '$($file.Name)'."
    }
    $published += [pscustomobject]@{
        Uri = "s3://$bucket/$key"
        Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        Bytes = $file.Length
    }
}

$published | ConvertTo-Json -Depth 3
