# Offline launcher; does not install Python, copy repositories or create an environment.
$ErrorActionPreference = 'Stop'
$taskPython = $null
foreach ($candidate in @('python', 'python3')) {
    $command = Get-Command $candidate -ErrorAction SilentlyContinue
    if ($command) {
        & $command.Source -c 'import sys;sys.exit(0 if sys.version_info >= (3,10) else 1)' 2>$null
        if ($LASTEXITCODE -eq 0) { $taskPython = $command.Source; break }
    }
}
if (-not $taskPython) {
    $uv = Get-Command uv -ErrorAction SilentlyContinue
    if ($uv) {
        $candidate = & $uv.Source python find --offline '>=3.10' 2>$null
        if ($LASTEXITCODE -eq 0) { $taskPython = "$candidate".Trim() }
    }
}
if (-not $taskPython) { throw 'Python >=3.10 is required. Select an existing offline interpreter; no automatic install.' }
& $taskPython -B (Join-Path $PSScriptRoot 'manage.py') @args
exit $LASTEXITCODE
