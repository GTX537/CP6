param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference = 'Stop'
$rows = @()
foreach ($file in Get-ChildItem -LiteralPath $Directory -Filter '*.dll' -File) {
    if ($file.Name -notmatch '^(CP6\.Platform\.|OpenTelemetry|System\.Diagnostics\.DiagnosticSource\.)') { continue }
    $identity = [System.Reflection.AssemblyName]::GetAssemblyName($file.FullName)
    $stream = [System.IO.File]::OpenRead($file.FullName)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            $mvid = $reader.GetGuid($reader.GetModuleDefinition().Mvid).ToString()
        } finally { $pe.Dispose() }
    } finally { $stream.Dispose() }
    $rows += [ordered]@{
        name = $identity.Name
        version = $identity.Version.ToString()
        path = $file.FullName
        mvid = $mvid
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
ConvertTo-Json -InputObject @($rows | Sort-Object name) -Depth 5
