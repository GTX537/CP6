$ErrorActionPreference='Stop'
$taskTmp='D:\CP6\tmp'
$testRoot=Join-Path $taskTmp 'db-compat-wp4-tests'
$secrets=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($name in @('db-compat.wp4-owned.json','db-compat.wp4-erp-owned.json','db-compat.wp4-business-owned.json')){
 $receipt=Get-Content -LiteralPath (Join-Path $taskTmp $name) -Raw|ConvertFrom-Json
 foreach($connection in @($receipt.PostgreSqlConnection,$receipt.SqlServerConnection)){
  $builder=[System.Data.Common.DbConnectionStringBuilder]::new()
  $builder.set_ConnectionString($connection)
  foreach($key in @('Password','Pwd')){
   if($builder.ContainsKey($key) -and ![string]::IsNullOrEmpty([string]$builder[$key])){$null=$secrets.Add([string]$builder[$key])}
  }
 }
}
if($secrets.Count -eq 0){throw 'Expected a configured task PostgreSQL credential for exact-value inspection.'}
$files=@(Get-ChildItem -LiteralPath $taskTmp -File -Filter 'wp4-*'|Where-Object Extension -in @('.json','.log'))
$files+=@(Get-ChildItem -LiteralPath $testRoot -File -Recurse|Where-Object {$_.FullName -notmatch '[\\/]private[^\\/]*[\\/]' -and $_.Name -notlike 'private-*'})
$files+=@(@('Invoke-DbCompatWp4Case.ps1','Invoke-DbCompatWp4Case.first-red.ps1','Invoke-DbCompatWp4Case.before-identity-http.ps1','New-DbCompatWp4Databases.ps1','New-DbCompatWp4ErpDatabases.ps1','New-DbCompatWp4BusinessDatabases.ps1','Test-Wp4WmsSelection.ps1','Test-Wp4WmsSelection.first.ps1','Test-Wp4RelatedUnit.ps1','Test-Wp4PublicEvidenceInputs.ps1','Archive-DbCompatWp4Evidence.ps1','wp4-task-code-review.md')|ForEach-Object {Get-Item -LiteralPath (Join-Path $taskTmp $_)})
$worktree=Join-Path $taskTmp 'worktrees\db-compat-wp4-20261003'
$paths=@(& git -C $worktree diff origin/main --name-only)+@(& git -C $worktree ls-files --others --exclude-standard)
if($LASTEXITCODE -ne 0){throw 'Could not enumerate task files.'}
$files+=@($paths|Sort-Object -Unique|ForEach-Object {Get-Item -LiteralPath (Join-Path $worktree $_)})
$matches=[Collections.Generic.List[string]]::new()
foreach($file in $files){
 $content=[IO.File]::ReadAllText($file.FullName)
 foreach($secret in $secrets){if($content.Contains($secret,[StringComparison]::Ordinal)){$matches.Add($file.FullName);break}}
}
$out=[ordered]@{Task='DB-COMPAT-01-WP4';ObservedUtc=[DateTime]::UtcNow.ToString('o');Scope='Exact task credential values compared privately with public evidence and task diff; no credential values or receipts exported';FilesChecked=$files.Count;MatchingFileCount=$matches.Count;Passed=$matches.Count -eq 0}
$out|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $taskTmp 'wp4-public-evidence-input-check.json') -Encoding utf8
$out|ConvertTo-Json
if($matches.Count -gt 0){throw 'A selected public file contains a configured task credential; stop publication and inspect locally.'}
