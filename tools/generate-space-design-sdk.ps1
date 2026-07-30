param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$openApi = Join-Path $repo 'docs\space\contracts\design-v1.openapi.json'
$csharp = Join-Path $repo 'CP6.Space.Client\SpaceDesignV1Client.g.cs'
$typescript = Join-Path $repo 'sdk\typescript\space-design-v1\spaceDesignV1Client.ts'
$tempBase = [System.IO.Path]::GetFullPath(
    [System.IO.Path]::GetTempPath()
).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
)
$tempRoot = [System.IO.Path]::GetFullPath((Join-Path $tempBase (
    'cp6-space-sdk-' + [Guid]::NewGuid().ToString('N'))
))
$expectedPrefix = $tempBase + [System.IO.Path]::DirectorySeparatorChar +
    'cp6-space-sdk-'
if (!$tempRoot.StartsWith(
        $expectedPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use an unexpected temporary path: $tempRoot"
}
New-Item -ItemType Directory -Path $tempRoot | Out-Null

function Get-NormalizedGeneratedText {
    param([Parameter(Mandatory)][string]$Path)

    $content = [System.IO.File]::ReadAllText($Path)
    $content = $content.Replace("`r`n", "`n").Replace("`r", "`n")
    $content = [System.Text.RegularExpressions.Regex]::Replace(
        $content,
        '(?m)[\t ]+$',
        ''
    ).TrimEnd("`n") + "`n"
    return $content
}

function Normalize-GeneratedText {
    param([Parameter(Mandatory)][string]$Path)

    [System.IO.File]::WriteAllText(
        $Path,
        (Get-NormalizedGeneratedText -Path $Path),
        [System.Text.UTF8Encoding]::new($false)
    )
}

try {
    $generatedOpenApi = Join-Path $tempRoot 'design-v1.openapi.json'
    $generatedCSharp = Join-Path $tempRoot 'SpaceDesignV1Client.g.cs'
    $generatedTypeScript = Join-Path $tempRoot 'spaceDesignV1Client.ts'

    dotnet run --project (
        Join-Path $repo 'tools\CP6.Space.OpenApiGenerator\CP6.Space.OpenApiGenerator.csproj'
    ) -- $generatedOpenApi
    if ($LASTEXITCODE -ne 0) { throw 'OpenAPI generation failed.' }

    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

    dotnet nswag openapi2csclient `
        "/input:$generatedOpenApi" `
        "/output:$generatedCSharp" `
        /namespace:CP6.Space.Client `
        /classname:SpaceDesignV1Client `
        /UseBaseUrl:false `
        /GenerateClientInterfaces:true `
        /JsonLibrary:SystemTextJson
    if ($LASTEXITCODE -ne 0) { throw 'C# client generation failed.' }

    dotnet nswag openapi2tsclient `
        "/input:$generatedOpenApi" `
        "/output:$generatedTypeScript" `
        /classname:SpaceDesignV1Client `
        /template:Fetch `
        /GenerateClientInterfaces:true
    if ($LASTEXITCODE -ne 0) { throw 'TypeScript client generation failed.' }

    Normalize-GeneratedText -Path $generatedOpenApi
    Normalize-GeneratedText -Path $generatedCSharp
    Normalize-GeneratedText -Path $generatedTypeScript

    if ($Check) {
        $pairs = @(
            @($generatedOpenApi, $openApi),
            @($generatedCSharp, $csharp),
            @($generatedTypeScript, $typescript)
        )
        foreach ($pair in $pairs) {
            if (!(Test-Path -LiteralPath $pair[1]) -or
                (Get-NormalizedGeneratedText -Path $pair[0]) -cne
                (Get-NormalizedGeneratedText -Path $pair[1])) {
                throw "Generated artifact is stale: $($pair[1])"
            }
        }
        return
    }

    New-Item -ItemType Directory -Force -Path (
        Split-Path -Parent $openApi) | Out-Null
    New-Item -ItemType Directory -Force -Path (
        Split-Path -Parent $csharp) | Out-Null
    New-Item -ItemType Directory -Force -Path (
        Split-Path -Parent $typescript) | Out-Null
    Copy-Item -LiteralPath $generatedOpenApi -Destination $openApi -Force
    Copy-Item -LiteralPath $generatedCSharp -Destination $csharp -Force
    Copy-Item -LiteralPath $generatedTypeScript -Destination $typescript -Force
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
