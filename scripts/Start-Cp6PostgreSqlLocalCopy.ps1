[CmdletBinding()]
param([Parameter(Mandatory)][string]$RunDirectory,[ValidateRange(1024,65535)][int]$Port=5188)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$configPath=Join-Path $RunDirectory 'private\settings.json'
$cfg=Get-Content -LiteralPath $configPath -Raw|ConvertFrom-Json
$result=Get-Content -LiteralPath (Join-Path $RunDirectory 'transfer-result.json') -Raw|ConvertFrom-Json
if($cfg.Task -cne 'CP6DB-PG-LOCAL-COPY' -or $cfg.PgDatabase -cnotmatch ('^cp6db_pg_test_[0-9]{8}_'+[regex]::Escape($cfg.RunId)+'$') -or $result.Status -cne 'Passed' -or $result.PgDatabase -cne $cfg.PgDatabase){throw 'A completed owned local data copy is required.'}
$connection=[Data.Common.DbConnectionStringBuilder]::new();$connection.set_ConnectionString($cfg.PostgreSqlConnection)
if($connection['Host'] -notin @('127.0.0.1','localhost','::1') -or $connection['Database'] -cne $cfg.PgDatabase){throw 'Local database identity mismatch.'}
if(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue){throw 'Local API port is already in use.'}
$run=Join-Path $RunDirectory ('private\api-'+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $run
$root=Split-Path -Parent $PSScriptRoot
$environment=@{DOTNET_ENVIRONMENT='Development';ASPNETCORE_ENVIRONMENT='Development';Database__Provider='PostgreSql';ConnectionStrings__DefaultConnection=$cfg.PostgreSqlConnection;ConnectionStrings__Redis='';Kafka__BootstrapServers='';RabbitMQ__HostName='';Startup__Mode='Api';Startup__SkipDatabaseInitialization='true';Startup__SkipHostedServices='true';ASPNETCORE_URLS='http://127.0.0.1:'+$Port;JWT__Secret=$cfg.JwtSecret;JWT__Issuer='CP6.LocalCopy.'+$cfg.RunId;JWT__Audience='CP6.LocalTesting';Security__Cookie__Secure='false';Security__Csrf__Enabled='true';CrmOidc__Enabled='false';CrmIdentity__Enabled='false';ErpIntegration__Enabled='false';Space__Cad__RemoteWorker__Enabled='false';Space__Files__RootPath=(Join-Path $RunDirectory 'private\space-files');Storage__Provider='Local';Storage__LocalRoot=(Join-Path $RunDirectory 'private\uploads');Release__GitSha=$cfg.OriginalArtifactSource;Release__Version='Local-PG-data-copy';Logging__LogLevel__Default='Information';'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command'='Warning'}
# Run interactively in this terminal. No password in arguments; Ctrl+C stops only this API.
# Child environment is restored after the owned API exits.
$saved=@{}
try{
 foreach($key in $environment.Keys){$saved[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,[string]$environment[$key],'Process')}
 Write-Host ('PostgreSQL database: '+$cfg.PgDatabase)
 Write-Host ('API: http://127.0.0.1:'+$Port+' ; Swagger: /swagger')
 Write-Host 'Background services are disabled for the copied data. Ctrl+C stops this API.'
 Push-Location $cfg.ArtifactDirectory
 try{& dotnet (Join-Path $cfg.ArtifactDirectory 'CP6.WebApi.dll');if($LASTEXITCODE){throw 'Local API exited unsuccessfully.'}}finally{Pop-Location}
}finally{foreach($key in $saved.Keys){[Environment]::SetEnvironmentVariable($key,$saved[$key],'Process')}}
