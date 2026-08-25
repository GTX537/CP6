Set-StrictMode -Version Latest

function Resolve-Cp6SqlcmdPath {
    [CmdletBinding()]
    param(
        [string]$SqlcmdPath = '',

        [string[]]$StandardPaths = @(
            'C:\Program Files\sqlcmd\sqlcmd.exe',
            'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE',
            'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
        )
    )

    if (-not [string]::IsNullOrWhiteSpace($SqlcmdPath)) {
        if (-not [IO.Path]::IsPathRooted($SqlcmdPath)) {
            throw 'SqlcmdPath must be an absolute executable path when provided.'
        }
        $candidatePaths = @($SqlcmdPath)
    }
    else {
        $sqlcmdCommand = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue |
            Select-Object -First 1
        $candidatePaths = @(
            if ($null -ne $sqlcmdCommand) { $sqlcmdCommand.Source }
            $StandardPaths
        )
    }

    $resolvedPath = $candidatePaths |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and
            (Test-Path -LiteralPath $_ -PathType Leaf)
        } |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($resolvedPath)) {
        throw 'sqlcmd was not found on PATH or in a supported standard installation directory.'
    }

    return [IO.Path]::GetFullPath($resolvedPath)
}

Export-ModuleMember -Function Resolve-Cp6SqlcmdPath
