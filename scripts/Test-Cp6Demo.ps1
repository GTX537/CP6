#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-f0-9]{40}$')]
    [string]$ExpectedGitSha,
    [string]$WebUrl = 'https://cp6.uk',
    [string]$ApiUrl = 'https://api.cp6.uk',
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$WebUrl = $WebUrl.TrimEnd('/')
$ApiUrl = $ApiUrl.TrimEnd('/')
$checks = [Collections.Generic.List[object]]::new()
$verified = $false

function Get-CheckedResponse {
    param([string]$Name, [string]$Url, [int]$ExpectedStatus = 200)
    $response = Invoke-WebRequest -Uri $Url -TimeoutSec 30 -SkipHttpErrorCheck
    $checks.Add([pscustomobject]@{
        name = $Name
        url = $Url
        status = [int]$response.StatusCode
        passed = [int]$response.StatusCode -eq $ExpectedStatus
    })
    if ([int]$response.StatusCode -ne $ExpectedStatus) {
        throw "$Name returned HTTP $($response.StatusCode), expected $ExpectedStatus."
    }
    return $response
}

try {
    $login = Get-CheckedResponse 'login document' "$WebUrl/login"
    if ($login.Content -notmatch 'id="app"') { throw 'The login response is not the Vue application.' }
    $asset = [regex]::Match($login.Content, 'src="(/assets/[^" ]+\.js)"')
    if (-not $asset.Success) { throw 'The application entry script was not found.' }
    $null = Get-CheckedResponse 'application entry script' ($WebUrl + $asset.Groups[1].Value)

    foreach ($probe in @('live', 'ready')) {
        $health = (Get-CheckedResponse "API $probe" "$ApiUrl/health/$probe").Content | ConvertFrom-Json
        if ($health.status -ne 'Healthy') { throw "API $probe is not Healthy." }
    }
    $apiRelease = (Get-CheckedResponse 'API release' "$ApiUrl/health/release").Content | ConvertFrom-Json
    $webRelease = (Get-CheckedResponse 'Web release' "$WebUrl/release.json").Content | ConvertFrom-Json
    $identityMatches = $apiRelease.gitSha -ceq $ExpectedGitSha -and
        $webRelease.gitSha -ceq $ExpectedGitSha -and
        $apiRelease.version -ceq $webRelease.version
    $checks.Add([pscustomobject]@{ name = 'matching source and version'; passed = $identityMatches })
    if (-not $identityMatches) { throw 'API/Web identity does not match the expected source and version.' }

    $null = Get-CheckedResponse 'anonymous same-origin profile rejected' "$WebUrl/api/auth/profile" 401
    $null = Get-CheckedResponse 'anonymous ERP access rejected' "$ApiUrl/api/products" 401
    $language = (Get-CheckedResponse 'login translations' "$WebUrl/api/lang/ja/ns/_core").Content
    if ($language -notmatch 'login\.button') { throw 'Login translations are missing.' }

    $verified = $true
    [pscustomobject]@{ Passed = $true; Checks = $checks.Count; GitSha = $ExpectedGitSha; Version = $apiRelease.version }
}
finally {
    if ($OutputPath) {
        [pscustomobject]@{
            observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            passed = $verified
            expectedGitSha = $ExpectedGitSha
            checks = @($checks.ToArray())
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    }
}
