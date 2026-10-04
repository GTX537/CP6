# Local WP6 lifecycle only. Credentials never belong in public contexts or receipts.
Set-StrictMode -Version Latest
$script:contexts=@{}
$script:roles=@('schema','runtime','oidcidentity','erp','corewms','space','reports','spacehistory','sqlupgrade','application','restore')
$script:task='DB-COMPAT-01-WP6'
function Assert-Cp6([bool]$Condition,[string]$Code) { if(!$Condition){throw $Code} }
function Get-Cp6ConnectionKeys([string]$Text){
    # The native .NET parser validates values first. This second pass retains duplicate
    # keys without treating semicolons or equals inside quoted credentials as routing.
    $position=0
    while($position -lt $Text.Length){
        while($position -lt $Text.Length -and ([char]::IsWhiteSpace($Text[$position]) -or $Text[$position] -eq ';')){$position++}
        if($position -eq $Text.Length){break}
        $keyStart=$position
        while($position -lt $Text.Length -and $Text[$position] -ne '='){$position++}
        Assert-Cp6 ($position -lt $Text.Length) 'CP6_COMPAT_CONNECTION_INVALID'
        $key=$Text.Substring($keyStart,$position-$keyStart).Trim().ToLowerInvariant();$position++
        while($position -lt $Text.Length -and [char]::IsWhiteSpace($Text[$position])){$position++}
        if($position -lt $Text.Length -and $Text[$position] -cin @([char]'"',[char]"'")){
            $quote=$Text[$position];$position++;$closed=$false
            while($position -lt $Text.Length){
                if($Text[$position] -eq $quote){
                    if($position+1 -lt $Text.Length -and $Text[$position+1] -eq $quote){$position+=2;continue}
                    $position++;$closed=$true;break
                }
                $position++
            }
            Assert-Cp6 $closed 'CP6_COMPAT_CONNECTION_INVALID'
            while($position -lt $Text.Length -and [char]::IsWhiteSpace($Text[$position])){$position++}
            Assert-Cp6 ($position -eq $Text.Length -or $Text[$position] -eq ';') 'CP6_COMPAT_CONNECTION_INVALID'
        }else{while($position -lt $Text.Length -and $Text[$position] -ne ';'){$position++}}
        $key
    }
}
function ConvertTo-Cp6LifecycleConfiguration {
    param([string]$Provider,[string]$ConnectionString)
    Assert-Cp6 ($Provider -cin @('SqlServer','PostgreSql')) 'CP6_COMPAT_PROVIDER_INVALID'
    Assert-Cp6 (![string]::IsNullOrWhiteSpace($ConnectionString)) 'CP6_COMPAT_CONNECTION_REQUIRED'
    $builder=[System.Data.Common.DbConnectionStringBuilder]::new()
    try {$builder.set_ConnectionString($ConnectionString)} catch {throw 'CP6_COMPAT_CONNECTION_INVALID'}
    $aliases=if($Provider -ceq 'SqlServer'){
        @{'server'='host';'data source'='host';'address'='host';'initial catalog'='database';'database'='database';'user id'='username';'uid'='username';'password'='password';'pwd'='password';'integrated security'='integrated';'trusted_connection'='integrated';'encrypt'='encrypt';'trustservercertificate'='trust';'trust server certificate'='trust';'connect timeout'='timeout';'connection timeout'='timeout';'pooling'='pooling';'multipleactiveresultsets'='mars';'application name'='application'}
    }else{
        @{'host'='host';'server'='host';'port'='port';'database'='database';'username'='username';'user id'='username';'password'='password';'ssl mode'='sslmode';'root certificate'='rootcertificate';'timeout'='timeout';'command timeout'='commandtimeout';'pooling'='pooling';'multiplexing'='multiplexing';'search path'='searchpath';'include error detail'='details';'application name'='application'}
    }
    $values=@{};$seen=@{}
    foreach($key in @(Get-Cp6ConnectionKeys $ConnectionString)) {
        Assert-Cp6 ($aliases.ContainsKey($key)) 'CP6_COMPAT_CONNECTION_KEY_FORBIDDEN'
        $canonical=$aliases[$key]
        Assert-Cp6 (!$seen.ContainsKey($canonical)) 'CP6_COMPAT_CONNECTION_KEY_DUPLICATE'
        $seen[$canonical]=$true
    }
    foreach($key in $builder.Keys){
        $lower=$key.ToLowerInvariant()
        Assert-Cp6 ($aliases.ContainsKey($lower)) 'CP6_COMPAT_CONNECTION_KEY_FORBIDDEN'
        $values[$aliases[$lower]]=[string]$builder[$key]
    }
    Assert-Cp6 ($values.ContainsKey('host')) 'CP6_COMPAT_LOOPBACK_REQUIRED'
    if($Provider -ceq 'SqlServer'){
        $hostValue=$values.host -creplace '^tcp:',''
        Assert-Cp6 ($hostValue -cmatch '^(localhost|127\.0\.0\.1|\[::1\]|::1)(\\[A-Za-z0-9_]+)?(,[0-9]{1,5})?$') 'CP6_COMPAT_LOOPBACK_REQUIRED'
        if($values.ContainsKey('mars')){Assert-Cp6 ($values.mars -ieq 'false') 'CP6_COMPAT_SQL_MARS_FORBIDDEN'}
        $values.integrated=if($values.ContainsKey('integrated')){$values.integrated}else{'false'}
        Assert-Cp6 ($values.integrated -iin @('true','false','sspi')) 'CP6_COMPAT_AUTHENTICATION_INVALID'
        Assert-Cp6 (($values.integrated -ine 'false') -or ($values.ContainsKey('username') -and $values.ContainsKey('password'))) 'CP6_COMPAT_AUTHENTICATION_REQUIRED'
        Assert-Cp6 (!(($values.integrated -ine 'false') -and ($values.ContainsKey('username') -or $values.ContainsKey('password')))) 'CP6_COMPAT_AUTHENTICATION_AMBIGUOUS'
        $values.trust=if($values.ContainsKey('trust')){$values.trust}else{'false'}
        Assert-Cp6 ($values.trust -iin @('true','false')) 'CP6_COMPAT_TLS_INVALID'
        $endpoint='tcp:'+$hostValue;$values.host=$endpoint
    }else{
        Assert-Cp6 ($values.host -cin @('localhost','127.0.0.1','::1')) 'CP6_COMPAT_LOOPBACK_REQUIRED'
        $values.port=if($values.ContainsKey('port')){$values.port}else{'5432'}
        Assert-Cp6 ($values.port -cmatch '^[0-9]{1,5}$' -and [int]$values.port -gt 0 -and [int]$values.port -le 65535) 'CP6_COMPAT_PORT_INVALID'
        Assert-Cp6 ($values.ContainsKey('username') -and $values.ContainsKey('password')) 'CP6_COMPAT_AUTHENTICATION_REQUIRED'
        if($values.ContainsKey('multiplexing')){Assert-Cp6 ($values.multiplexing -ieq 'false') 'CP6_COMPAT_PG_MULTIPLEXING_FORBIDDEN'}
        if($values.ContainsKey('searchpath')){Assert-Cp6 ($values.searchpath -ceq 'public') 'CP6_COMPAT_PG_SEARCH_PATH_INVALID'}
        $values.sslmode=if($values.ContainsKey('sslmode')){$values.sslmode}else{'Require'}
        Assert-Cp6 ($values.sslmode -iin @('Disable','Require','VerifyCA','VerifyFull')) 'CP6_COMPAT_TLS_INVALID'
        $endpoint=$values.host+':'+$values.port
    }
    return [pscustomobject]@{Provider=$Provider;Endpoint=$endpoint;Values=$values}
}
function Write-Cp6Json([string]$Path,$Value){
    $temporary=$Path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    [IO.File]::WriteAllText($temporary,($Value|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary,$Path,$true)
}
function Get-Cp6Private($Context){
    Assert-Cp6 ($script:contexts.ContainsKey([string]$Context.RunId)) 'CP6_COMPAT_CONTEXT_UNKNOWN'
    $private=$script:contexts[$Context.RunId]
    Assert-Cp6 ($Context.Provider -ceq $private.Public.Provider -and $Context.RunDirectory -ceq $private.Public.RunDirectory -and $Context.Endpoint -ceq $private.Public.Endpoint -and $Context.PrivateDirectory -ceq $private.Public.PrivateDirectory -and $Context.ReceiptDirectory -ceq $private.Public.ReceiptDirectory) 'CP6_COMPAT_CONTEXT_MISMATCH'
    return $private
}
function New-Cp6LifecycleContext {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('SqlServer','PostgreSql')][string]$Provider,[Parameter(Mandatory)][string]$RunDirectory,[string]$ConnectionString,[string]$PostgreSqlBinDirectory)
    if(!$ConnectionString){$ConnectionString=[Environment]::GetEnvironmentVariable($(if($Provider -ceq 'SqlServer'){'CP6_TEST_SQLSERVER'}else{'CP6_TEST_POSTGRES'}))}
    $configuration=ConvertTo-Cp6LifecycleConfiguration $Provider $ConnectionString
    $directory=[IO.Path]::GetFullPath($RunDirectory)
    $privateDirectory=Join-Path $directory 'private';$receiptDirectory=Join-Path $directory 'receipts'
    $null=[IO.Directory]::CreateDirectory($privateDirectory);$null=[IO.Directory]::CreateDirectory($receiptDirectory)
    $tools=@{}
    foreach($name in $(if($Provider -ceq 'SqlServer'){@('sqlcmd')}else{@('psql','pg_dump','pg_restore')})){
        $candidate=if($PostgreSqlBinDirectory -and $Provider -ceq 'PostgreSql'){Join-Path $PostgreSqlBinDirectory $name}else{$name}
        $command=Get-Command $candidate -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        Assert-Cp6 ($null -ne $command) 'CP6_COMPAT_NATIVE_TOOL_MISSING'
        $tools[$name]=$command.Source
    }
    $manifestPath=Join-Path $directory ('lifecycle-'+$Provider+'.json')
    if(Test-Path -LiteralPath $manifestPath){
        try {$public=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json}catch{throw 'CP6_COMPAT_RUN_MANIFEST_INVALID'}
        Assert-Cp6 ($public.Provider -ceq $Provider -and $public.Endpoint -ceq $configuration.Endpoint -and $public.RunDirectory -ceq $directory -and $public.PrivateDirectory -ceq $privateDirectory -and $public.ReceiptDirectory -ceq $receiptDirectory -and $public.RunId -cmatch '^[a-f0-9]{32}$' -and $public.Task -ceq $script:task) 'CP6_COMPAT_RUN_MANIFEST_MISMATCH'
    }else{
        $public=[pscustomobject]@{RunId=[guid]::NewGuid().ToString('N');Task=$script:task;Provider=$Provider;Endpoint=$configuration.Endpoint;RunDirectory=$directory;PrivateDirectory=$privateDirectory;ReceiptDirectory=$receiptDirectory}
        Write-Cp6Json $manifestPath $public
    }
    $script:contexts[$public.RunId]=@{Public=($public|ConvertTo-Json|ConvertFrom-Json);Configuration=$configuration;Tools=$tools}
    return $public
}
function Assert-Cp6Receipt($Context,$Receipt){
    $null=Get-Cp6Private $Context
    Assert-Cp6 ($Receipt.RunId -ceq $Context.RunId -and $Receipt.Provider -ceq $Context.Provider -and $Receipt.Task -ceq $script:task -and $Receipt.Endpoint -ceq $Context.Endpoint) 'CP6_COMPAT_RECEIPT_CONTEXT_MISMATCH'
    Assert-Cp6 ($Receipt.Owner -cmatch '^[a-f0-9]{32}$' -and $Receipt.Role -cin $script:roles) 'CP6_COMPAT_RECEIPT_OWNER_INVALID'
    $date=[datetime]::MinValue
    Assert-Cp6 ($Receipt.DatabaseName -cmatch ('^CP6Compat_WP6_(?<date>[0-9]{8})_'+$Receipt.Owner.Substring(0,8)+'_'+$Receipt.Role+'$')) 'CP6_COMPAT_RECEIPT_NAME_INVALID'
    Assert-Cp6 ([datetime]::TryParseExact($Matches.date,'yyyyMMdd',[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$date)) 'CP6_COMPAT_RECEIPT_NAME_INVALID'
    $expectedPath=Join-Path $Context.ReceiptDirectory ($Receipt.DatabaseName+'.json')
    Assert-Cp6 ($Receipt.ReceiptPath -ceq $expectedPath -and (Test-Path -LiteralPath $expectedPath -PathType Leaf)) 'CP6_COMPAT_RECEIPT_PATH_INVALID'
    try {$recorded=Get-Content -LiteralPath $expectedPath -Raw | ConvertFrom-Json}catch{throw 'CP6_COMPAT_RECEIPT_INVALID'}
    foreach($key in @('RunId','Task','Provider','Endpoint','DatabaseName','Owner','Role','Status')){Assert-Cp6 ($Receipt.$key -ceq $recorded.$key) 'CP6_COMPAT_RECEIPT_CHANGED'}
    Assert-Cp6 (($Receipt.PhysicalIdentity|ConvertTo-Json -Depth 10 -Compress) -ceq ($recorded.PhysicalIdentity|ConvertTo-Json -Depth 10 -Compress)) 'CP6_COMPAT_RECEIPT_CHANGED'
}
function Get-Cp6OwnedDatabaseConnection {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context,[Parameter(Mandatory)]$Receipt)
    Assert-Cp6Receipt $Context $Receipt
    $private=Get-Cp6Private $Context;$values=$private.Configuration.Values
    $b=[System.Data.Common.DbConnectionStringBuilder]::new();$b['Database']=$Receipt.DatabaseName
    if($Context.Provider -ceq 'SqlServer'){
        $b['Server']=$values.host;$b['Encrypt']='True';$b['TrustServerCertificate']=$values.trust
        if($values.integrated -ine 'false'){$b['Integrated Security']='True'}else{$b['User Id']=$values.username;$b['Password']=$values.password}
    }else{
        $b['Host']=$values.host;$b['Port']=$values.port;$b['Username']=$values.username;$b['Password']=$values.password;$b['SSL Mode']=$values.sslmode
        if($values.ContainsKey('rootcertificate')){$b['Root Certificate']=$values.rootcertificate}
        $b['Search Path']='public';$b['Include Error Detail']='False'
    }
    $b['Pooling']='False';$b['Application Name']='CP6Compat.WP6.Runner'
    return $b.get_ConnectionString()
}
function Invoke-Cp6Native {
    param($Context,[string]$Tool,[string]$Database,[string[]]$Arguments=@(),[string]$InputText='', [int]$TimeoutSeconds=300)
    $private=Get-Cp6Private $Context;$values=$private.Configuration.Values
    Assert-Cp6 ($private.Tools.ContainsKey($Tool)) 'CP6_COMPAT_NATIVE_TOOL_INVALID'
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$private.Tools[$Tool];$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($key in @($start.Environment.Keys)){if($key -match '^(PG|SQLCMD)'){$null=$start.Environment.Remove($key)}}
    if($Context.Provider -ceq 'SqlServer'){
        $start.Environment['SQLCMDSERVER']=$values.host;$start.Environment['SQLCMDDBNAME']=$Database
        $nativeArguments=@('-b','-r','1','-h','-1','-w','65535','-y','8000','-f','65001','-l','30','-N')
        if($values.trust -ieq 'true'){$nativeArguments+='-C'}
        if($values.integrated -ine 'false'){$nativeArguments+='-E'}else{$start.Environment['SQLCMDUSER']=$values.username;$start.Environment['SQLCMDPASSWORD']=$values.password}
    }else{
        $start.Environment['PGHOST']=$values.host;$start.Environment['PGPORT']=$values.port;$start.Environment['PGDATABASE']=$Database;$start.Environment['PGUSER']=$values.username;$start.Environment['PGPASSWORD']=$values.password
        $start.Environment['PGCONNECT_TIMEOUT']='30';$start.Environment['PGAPPNAME']='CP6Compat.WP6.Lifecycle'
        $start.Environment['PGSSLMODE']=switch($values.sslmode.ToLowerInvariant()){'verifyca'{'verify-ca'}'verifyfull'{'verify-full'}default{$values.sslmode.ToLowerInvariant()}}
        if($values.ContainsKey('rootcertificate')){$start.Environment['PGSSLROOTCERT']=$values.rootcertificate}
        $nativeArguments=if($Tool -ceq 'psql'){@('-X','-q','-A','-t','-v','ON_ERROR_STOP=1')}else{@()}
    }
    foreach($argument in @($nativeArguments)+@($Arguments)){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$start
    $logBase=Join-Path $Context.PrivateDirectory ([guid]::NewGuid().ToString('N')+'-'+$Tool)
    try {
        $null=$process.Start();$stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($InputText);$process.StandardInput.Close()
        $timedOut=!$process.WaitForExit($TimeoutSeconds*1000)
        if($timedOut){$process.Kill($true);$process.WaitForExit()}
        $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult();$exitCode=$process.ExitCode
        [IO.File]::WriteAllText($logBase+'.stdout',$out);[IO.File]::WriteAllText($logBase+'.stderr',$err)
        Write-Cp6Json ($logBase+'.result.json') ([pscustomobject]@{Tool=$Tool;ExitCode=$exitCode;TimedOut=$timedOut;FinishedUtc=[datetime]::UtcNow.ToString('O')})
        Assert-Cp6 (!$timedOut) 'CP6_COMPAT_NATIVE_TIMEOUT'
        Assert-Cp6 ($exitCode -eq 0) 'CP6_COMPAT_NATIVE_FAILED'
        return $out.Trim()
    }catch{
        if($_.Exception.Message -cmatch '^CP6_COMPAT_[A-Z_]+$'){throw $_.Exception.Message}
        throw 'CP6_COMPAT_NATIVE_FAILED'
    }finally{$process.Dispose()}
}
function Invoke-Cp6Query($Context,[string]$Database,[string]$Query){
    $text=if($Context.Provider -ceq 'SqlServer'){Invoke-Cp6Native $Context sqlcmd $Database -InputText ("SET NOCOUNT ON;`n"+$Query+"`nGO`n")}else{Invoke-Cp6Native $Context psql $Database -InputText ($Query+"`n")}
    if($Context.Provider -ceq 'SqlServer'){
        # sqlcmd's supported variable column width tops out at 8000. Metadata at
        # that boundary is rejected rather than accepting possible truncation.
        $text=$text.Replace("`r",'').Replace("`n",'')
        Assert-Cp6 ($text.Length -lt 8000) 'CP6_COMPAT_NATIVE_RESULT_TOO_LARGE'
    }
    try{return ($text | ConvertFrom-Json)}catch{throw 'CP6_COMPAT_NATIVE_RESULT_INVALID'}
}
function Invoke-Cp6Statement($Context,[string]$Database,[string]$Query){
    if($Context.Provider -ceq 'SqlServer'){$null=Invoke-Cp6Native $Context sqlcmd $Database -InputText ("SET NOCOUNT ON;`n"+$Query+"`nGO`n")}else{$null=Invoke-Cp6Native $Context psql $Database -InputText ($Query+"`n")}
}
function Get-Cp6ActualDatabase($Context,[string]$Name){
    Assert-Cp6 ($Name -cmatch '^CP6Compat_WP6_[0-9]{8}_[a-f0-9]{8}_[a-z]+$') 'CP6_COMPAT_DATABASE_NAME_INVALID'
    if($Context.Provider -ceq 'SqlServer'){
        $exists=Invoke-Cp6Query $Context master "SELECT CAST(CASE WHEN DB_ID(N'$Name') IS NULL THEN 0 ELSE 1 END AS bit) AS [Exists] FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;"
        if(!$exists.Exists){return [pscustomobject]@{Exists=$false}}
        return Invoke-Cp6Query $Context $Name @"
SELECT CAST(1 AS bit) AS [Exists],DB_NAME() AS DatabaseName,DB_ID() AS DatabaseId,
 CONVERT(varchar(33),d.create_date,126) AS CreatedAt,SUSER_SNAME(d.owner_sid) AS DatabaseOwner,SUSER_SNAME() AS Principal,
 CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ServerVersion,
 (SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask') AS Task,
 (SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner') AS Owner,
 (SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped=0) AS UserTables,
 CAST(CASE WHEN IS_SRVROLEMEMBER('sysadmin')=1 OR HAS_PERMS_BY_NAME(NULL,NULL,'VIEW SERVER STATE')=1 OR HAS_PERMS_BY_NAME(NULL,NULL,'VIEW SERVER PERFORMANCE STATE')=1 THEN 1 ELSE 0 END AS bit) AS SessionVisibility,
 (SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE database_id=DB_ID() AND session_id<>@@SPID) AS Sessions,
 JSON_QUERY((SELECT file_id AS FileId,name AS LogicalName,physical_name AS PhysicalName,type AS FileType FROM sys.database_files ORDER BY file_id FOR JSON PATH)) AS Files
FROM sys.databases d WHERE d.database_id=DB_ID() FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
"@
    }
    $exists=Invoke-Cp6Query $Context postgres "SELECT json_build_object('Exists',EXISTS(SELECT 1 FROM pg_database WHERE datname='$Name'));"
    if(!$exists.Exists){return [pscustomobject]@{Exists=$false}}
    return Invoke-Cp6Query $Context $Name @"
SELECT json_build_object('Exists',true,'DatabaseName',current_database(),'DatabaseId',d.oid,'DatabaseOwner',pg_get_userbyid(d.datdba),'Principal',current_user,'ServerVersion',current_setting('server_version'),
 'Task',split_part(coalesce(shobj_description(d.oid,'pg_database'),''),':',1),'Owner',split_part(coalesce(shobj_description(d.oid,'pg_database'),''),':',2),'Marker',shobj_description(d.oid,'pg_database'),
 'UserTables',((SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%')+(SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema'))+(SELECT count(*) FROM pg_namespace WHERE nspname NOT IN ('public','pg_catalog','information_schema') AND nspname NOT LIKE 'pg_%')),
 'Sessions',(SELECT count(*) FROM pg_stat_activity WHERE datid=d.oid AND pid<>pg_backend_pid()),
 'DatabaseSettings',(SELECT coalesce(json_agg(json_build_object('Role',CASE WHEN s.setrole=0 THEN '' ELSE pg_get_userbyid(s.setrole) END,'Settings',s.setconfig) ORDER BY s.setrole),'[]'::json) FROM pg_db_role_setting s WHERE s.setdatabase=d.oid),
 'DatabaseAcl',(SELECT coalesce(json_agg(json_build_object('Grantor',pg_get_userbyid(a.grantor),'Grantee',CASE WHEN a.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(a.grantee) END,'Privilege',a.privilege_type,'Grantable',a.is_grantable) ORDER BY a.grantor,a.grantee,a.privilege_type),'[]'::json) FROM aclexplode(coalesce(d.datacl,acldefault('d',d.datdba))) a))
FROM pg_database d WHERE d.datname=current_database();
"@
}
function Get-Cp6PhysicalIdentity($Context,$Actual){
    if($Context.Provider -ceq 'SqlServer'){return [pscustomobject]@{DatabaseId=$Actual.DatabaseId;CreatedAt=$Actual.CreatedAt;Files=$Actual.Files}}
    return [pscustomobject]@{DatabaseId=$Actual.DatabaseId}
}
function Set-Cp6Marker($Context,$Receipt){
    if($Context.Provider -ceq 'SqlServer'){
        Invoke-Cp6Statement $Context $Receipt.DatabaseName "IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask') EXEC sys.sp_updateextendedproperty @name=N'CP6CompatTask',@value=N'$script:task'; ELSE EXEC sys.sp_addextendedproperty @name=N'CP6CompatTask',@value=N'$script:task'; IF EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner') EXEC sys.sp_updateextendedproperty @name=N'CP6CompatOwner',@value=N'$($Receipt.Owner)'; ELSE EXEC sys.sp_addextendedproperty @name=N'CP6CompatOwner',@value=N'$($Receipt.Owner)';"
    }else{Invoke-Cp6Statement $Context postgres ('COMMENT ON DATABASE "'+$Receipt.DatabaseName+'" IS '''+$script:task+':'+$Receipt.Owner+''';')}
}
function New-Cp6OwnedDatabase {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context,[Parameter(Mandatory)][string]$Role)
    $null=Get-Cp6Private $Context
    Assert-Cp6 ($Role -cin $script:roles) 'CP6_COMPAT_ROLE_INVALID'
    $owner=[guid]::NewGuid().ToString('N');$name='CP6Compat_WP6_'+[datetime]::UtcNow.ToString('yyyyMMdd')+'_'+$owner.Substring(0,8)+'_'+$Role
    $receipt=[pscustomobject]@{RunId=$Context.RunId;Task=$script:task;Provider=$Context.Provider;Endpoint=$Context.Endpoint;DatabaseName=$name;Owner=$owner;Role=$Role;Status='Planned';PhysicalIdentity=$null;Principal=$null;DatabaseOwner=$null;ServerVersion=$null;CreatedUtc=[datetime]::UtcNow.ToString('O');ReceiptPath=(Join-Path $Context.ReceiptDirectory ($name+'.json'));Events=@()}
    Write-Cp6Json $receipt.ReceiptPath $receipt
    $actual=Get-Cp6ActualDatabase $Context $name
    Assert-Cp6 (!$actual.Exists) 'CP6_COMPAT_DATABASE_ALREADY_EXISTS'
    if($Context.Provider -ceq 'SqlServer'){Invoke-Cp6Statement $Context master "CREATE DATABASE [$name];"}else{Invoke-Cp6Statement $Context postgres ('CREATE DATABASE "'+$name+'" TEMPLATE template0;')}
    $receipt.Status='Created';Write-Cp6Json $receipt.ReceiptPath $receipt
    Set-Cp6Marker $Context $receipt
    $actual=Get-Cp6ActualDatabase $Context $name
    Assert-Cp6 ($actual.Exists -and $actual.DatabaseName -ceq $name -and $actual.Task -ceq $script:task -and $actual.Owner -ceq $owner -and $actual.UserTables -eq 0) 'CP6_COMPAT_CREATED_DATABASE_MISMATCH'
    if($Context.Provider -ceq 'PostgreSql'){Assert-Cp6 ($actual.Marker -ceq ($script:task+':'+$owner)) 'CP6_COMPAT_ACTUAL_OWNER_MISMATCH'}
    $receipt.PhysicalIdentity=Get-Cp6PhysicalIdentity $Context $actual;$receipt.Principal=$actual.Principal;$receipt.DatabaseOwner=$actual.DatabaseOwner;$receipt.ServerVersion=$actual.ServerVersion;$receipt.Status='Owned'
    Write-Cp6Json $receipt.ReceiptPath $receipt
    return $receipt
}
function Test-Cp6OwnedDatabase {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context,[Parameter(Mandatory)]$Receipt,[switch]$RequireEmpty,[switch]$RequireNoSessions)
    Assert-Cp6Receipt $Context $Receipt
    Assert-Cp6 ($Receipt.Status -cin @('Owned','Restored','DropPending','Absent')) 'CP6_COMPAT_RECEIPT_NOT_OWNED'
    $actual=Get-Cp6ActualDatabase $Context $Receipt.DatabaseName
    if(!$actual.Exists){return $actual}
    Assert-Cp6 ($actual.DatabaseName -ceq $Receipt.DatabaseName -and $actual.Task -ceq $Receipt.Task -and $actual.Owner -ceq $Receipt.Owner) 'CP6_COMPAT_ACTUAL_OWNER_MISMATCH'
    if($Context.Provider -ceq 'PostgreSql'){Assert-Cp6 ($actual.Marker -ceq ($script:task+':'+$Receipt.Owner)) 'CP6_COMPAT_ACTUAL_OWNER_MISMATCH'}
    $physical=Get-Cp6PhysicalIdentity $Context $actual
    Assert-Cp6 (($physical|ConvertTo-Json -Depth 10 -Compress) -ceq ($Receipt.PhysicalIdentity|ConvertTo-Json -Depth 10 -Compress)) 'CP6_COMPAT_PHYSICAL_IDENTITY_MISMATCH'
    Assert-Cp6 ($actual.Principal -ceq $Receipt.Principal -and $actual.DatabaseOwner -ceq $Receipt.DatabaseOwner) 'CP6_COMPAT_PRINCIPAL_MISMATCH'
    if($RequireEmpty){Assert-Cp6 ($actual.UserTables -eq 0) 'CP6_COMPAT_DATABASE_NOT_EMPTY'}
    if($RequireNoSessions){
        if($Context.Provider -ceq 'SqlServer'){Assert-Cp6 $actual.SessionVisibility 'CP6_COMPAT_SESSION_VISIBILITY_REQUIRED'}
        Assert-Cp6 ($actual.Sessions -eq 0) 'CP6_COMPAT_DATABASE_HAS_SESSIONS'
    }
    return $actual
}
function Wait-Cp6OwnedDatabaseIdle {
    param($Context,$Receipt,[ValidateRange(0,60)][int]$SessionWaitSeconds)
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while($true){
        try{
            # Recheck receipt, owner, principal and physical identity on every poll.
            return Test-Cp6OwnedDatabase $Context $Receipt -RequireNoSessions
        }catch{
            if($_.Exception.Message -cne 'CP6_COMPAT_DATABASE_HAS_SESSIONS' -or
                $watch.Elapsed.TotalSeconds -ge $SessionWaitSeconds){throw}
            Start-Sleep -Milliseconds 250
        }
    }
}
function Remove-Cp6OwnedDatabases {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context,[Parameter(Mandatory)][array]$Receipts,
        [ValidateRange(0,60)][int]$SessionWaitSeconds=15)
    Assert-Cp6 ($Receipts.Count -gt 0 -and @($Receipts.DatabaseName|Sort-Object -Unique).Count -eq $Receipts.Count) 'CP6_COMPAT_CLEANUP_TARGETS_INVALID'
    foreach($receipt in $Receipts){$null=Wait-Cp6OwnedDatabaseIdle $Context $receipt $SessionWaitSeconds}
    $results=@()
    foreach($receipt in $Receipts){
        $actual=Wait-Cp6OwnedDatabaseIdle $Context $receipt $SessionWaitSeconds
        if($actual.Exists){
            $receipt.Status='DropPending';Write-Cp6Json $receipt.ReceiptPath $receipt
            if($Context.Provider -ceq 'SqlServer'){Invoke-Cp6Statement $Context master "DROP DATABASE [$($receipt.DatabaseName)];"}else{Invoke-Cp6Statement $Context postgres ('DROP DATABASE "'+$receipt.DatabaseName+'";')}
            Assert-Cp6 (!(Get-Cp6ActualDatabase $Context $receipt.DatabaseName).Exists) 'CP6_COMPAT_DROP_NOT_ABSENT'
        }
        $receipt.Status='Absent';Write-Cp6Json $receipt.ReceiptPath $receipt;$results+=$receipt
    }
    return $results
}
function ConvertTo-Cp6SqlLiteral([string]$Value){return "N'"+$Value.Replace("'","''")+"'"}
function ConvertTo-Cp6PgIdentifier([string]$Value){return '"'+$Value.Replace('"','""')+'"'}
function ConvertTo-Cp6PgLiteral([string]$Value){return "'"+$Value.Replace("'","''")+"'"}
function Get-Cp6NativeToolIdentity($Context,[string]$Tool){
    $private=Get-Cp6Private $Context;$path=$private.Tools[$Tool]
    $arguments=if($Tool -ceq 'sqlcmd'){@('-?')}else{@('--version')}
    $version=Invoke-Cp6Native $Context $Tool $(if($Context.Provider -ceq 'SqlServer'){'master'}else{'postgres'}) -Arguments $arguments
    return [pscustomobject]@{Tool=$Tool;Path=$path;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;NativeVersion=$version}
}
function Backup-Cp6OwnedDatabase {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context,[Parameter(Mandatory)]$Receipt)
    $source=Test-Cp6OwnedDatabase $Context $Receipt -RequireNoSessions
    Assert-Cp6 $source.Exists 'CP6_COMPAT_BACKUP_SOURCE_ABSENT'
    $extension=if($Context.Provider -ceq 'SqlServer'){'.bak'}else{'.dump'}
    $archive=Join-Path $Context.PrivateDirectory ('cp6-wp6-'+$Context.RunId+'-'+$Receipt.Owner+$extension)
    Assert-Cp6 (!(Test-Path -LiteralPath $archive)) 'CP6_COMPAT_BACKUP_EXISTS'
    $backup=[pscustomobject]@{RunId=$Context.RunId;Provider=$Context.Provider;Task=$script:task;SourceDatabaseName=$Receipt.DatabaseName;SourceOwner=$Receipt.Owner;SourcePhysicalIdentity=$Receipt.PhysicalIdentity;SourceState=$source;ArchivePath=$archive;Sha256=$null;SqlServerArchivePath=$null;SqlSourceFiles=@();ToolIdentity=$null;ServerVersion=$source.ServerVersion;Status='Planned';NativeExitCode=$null;ReceiptPath=($archive+'.json')}
    Write-Cp6Json $backup.ReceiptPath $backup
    if($Context.Provider -ceq 'SqlServer'){
        $paths=Invoke-Cp6Query $Context master @"
SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)) AS BackupDirectory,
 JSON_QUERY((SELECT file_id AS FileId,name AS LogicalName,physical_name AS PhysicalName,type AS FileType FROM sys.master_files WHERE database_id=DB_ID(N'$($Receipt.DatabaseName)') ORDER BY file_id FOR JSON PATH)) AS Files FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
"@
        Assert-Cp6 (@($paths.Files).Count -gt 0) 'CP6_COMPAT_BACKUP_SOURCE_FILES_MISSING'
        $directory=[string]$paths.BackupDirectory
        if(!$directory){
            $dataFile=@($paths.Files|Where-Object FileType -EQ 0)[0].PhysicalName
            $last=[Math]::Max($dataFile.LastIndexOf('/'),$dataFile.LastIndexOf('\'))
            Assert-Cp6 ($last -gt 0) 'CP6_COMPAT_BACKUP_DIRECTORY_UNKNOWN'
            $directory=$dataFile.Substring(0,$last)
        }
        $separator=if($directory.Contains('\')){'\'}else{'/'}
        $serverArchive=$directory.TrimEnd('\','/')+$separator+[IO.Path]::GetFileName($archive)
        $backup.SqlServerArchivePath=$serverArchive;$backup.SqlSourceFiles=@($paths.Files)
        Write-Cp6Json $backup.ReceiptPath $backup
        $pathLiteral=ConvertTo-Cp6SqlLiteral $serverArchive
        $null=Test-Cp6OwnedDatabase $Context $Receipt -RequireNoSessions
        Invoke-Cp6Statement $Context master "BACKUP DATABASE [$($Receipt.DatabaseName)] TO DISK=$pathLiteral WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=$pathLiteral WITH CHECKSUM;"
        $backup.Status='VerifiedServerArchive';Write-Cp6Json $backup.ReceiptPath $backup
        try{Copy-Item -LiteralPath $serverArchive -Destination $archive -ErrorAction Stop}catch{throw 'CP6_COMPAT_BACKUP_ARCHIVE_COPY_FAILED'}
        $backup.ToolIdentity=Get-Cp6NativeToolIdentity $Context sqlcmd
    }else{
        $null=Test-Cp6OwnedDatabase $Context $Receipt -RequireNoSessions
        $null=Invoke-Cp6Native $Context pg_dump $Receipt.DatabaseName -Arguments @('-Fc','--file',$archive)
        $backup.ToolIdentity=Get-Cp6NativeToolIdentity $Context pg_dump
    }
    Assert-Cp6 ((Test-Path -LiteralPath $archive -PathType Leaf) -and (Get-Item -LiteralPath $archive).Length -gt 0) 'CP6_COMPAT_BACKUP_ARCHIVE_MISSING'
    $backup.Sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash;$backup.Status='BackedUp';$backup.NativeExitCode=0
    $null=Test-Cp6OwnedDatabase $Context $Receipt -RequireNoSessions
    Write-Cp6Json $backup.ReceiptPath $backup
    return $backup
}
function Sync-Cp6PostgreSqlDatabaseMetadata($Context,$Source,$TargetReceipt,$Before){
    Assert-Cp6 ($Source.DatabaseOwner -ceq $Before.DatabaseOwner -and $Source.Principal -ceq $Before.Principal) 'CP6_COMPAT_RESTORE_DATABASE_OWNER_MISMATCH'
    Assert-Cp6 (@($Before.DatabaseSettings).Count -eq 0) 'CP6_COMPAT_RESTORE_TARGET_SETTINGS_NOT_EMPTY'
    $target=ConvertTo-Cp6PgIdentifier $TargetReceipt.DatabaseName
    $statements=[Collections.Generic.List[string]]::new()
    foreach($grantee in @($Before.DatabaseAcl.Grantee|Sort-Object -Unique)){
        $role=if($grantee -ceq 'PUBLIC'){'PUBLIC'}else{ConvertTo-Cp6PgIdentifier $grantee}
        $statements.Add("REVOKE ALL PRIVILEGES ON DATABASE $target FROM $role;")
    }
    foreach($grant in $Source.DatabaseAcl){
        Assert-Cp6 ($grant.Grantor -ceq $Source.DatabaseOwner -and $grant.Privilege -cin @('CONNECT','CREATE','TEMPORARY')) 'CP6_COMPAT_RESTORE_DATABASE_GRANT_UNSUPPORTED'
        $role=if($grant.Grantee -ceq 'PUBLIC'){'PUBLIC'}else{ConvertTo-Cp6PgIdentifier $grant.Grantee}
        $option=if($grant.Grantable){' WITH GRANT OPTION'}else{''}
        $statements.Add("GRANT $($grant.Privilege) ON DATABASE $target TO $role$option;")
    }
    foreach($row in $Source.DatabaseSettings){
        foreach($setting in $row.Settings){
            $pair=$setting.Split('=',2)
            Assert-Cp6 ($pair.Count -eq 2 -and $pair[0] -cmatch '^[a-z_][a-z0-9_.]*$') 'CP6_COMPAT_RESTORE_DATABASE_SETTING_INVALID'
            $prefix=if($row.Role){'ALTER ROLE '+(ConvertTo-Cp6PgIdentifier $row.Role)+' IN DATABASE '+$target}else{'ALTER DATABASE '+$target}
            $statements.Add($prefix+' SET '+$pair[0]+' TO '+(ConvertTo-Cp6PgLiteral $pair[1])+';')
        }
    }
    Invoke-Cp6Statement $Context postgres ('BEGIN;'+($statements -join "`n")+'COMMIT;')
    $after=Get-Cp6ActualDatabase $Context $TargetReceipt.DatabaseName
    foreach($field in @('DatabaseOwner','DatabaseSettings','DatabaseAcl')){
        Assert-Cp6 (($Source.$field|ConvertTo-Json -Depth 20 -Compress) -ceq ($after.$field|ConvertTo-Json -Depth 20 -Compress)) 'CP6_COMPAT_RESTORE_DATABASE_METADATA_MISMATCH'
    }
    return $after
}
function Restore-Cp6OwnedDatabase {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context,[Parameter(Mandatory)]$SourceReceipt,[Parameter(Mandatory)]$TargetReceipt,[Parameter(Mandatory)]$Backup)
    $source=Test-Cp6OwnedDatabase $Context $SourceReceipt -RequireNoSessions
    $target=Test-Cp6OwnedDatabase $Context $TargetReceipt -RequireEmpty -RequireNoSessions
    Assert-Cp6 ($source.Exists -and $target.Exists -and $TargetReceipt.Role -ceq 'restore' -and $SourceReceipt.DatabaseName -cne $TargetReceipt.DatabaseName -and $SourceReceipt.Owner -cne $TargetReceipt.Owner) 'CP6_COMPAT_RESTORE_TARGET_INVALID'
    Assert-Cp6 ($Backup.RunId -ceq $Context.RunId -and $Backup.Provider -ceq $Context.Provider -and $Backup.Task -ceq $script:task -and $Backup.SourceDatabaseName -ceq $SourceReceipt.DatabaseName -and $Backup.SourceOwner -ceq $SourceReceipt.Owner -and $Backup.Status -ceq 'BackedUp') 'CP6_COMPAT_BACKUP_RECEIPT_MISMATCH'
    Assert-Cp6 (($Backup.SourcePhysicalIdentity|ConvertTo-Json -Depth 10 -Compress) -ceq ($SourceReceipt.PhysicalIdentity|ConvertTo-Json -Depth 10 -Compress)) 'CP6_COMPAT_BACKUP_IDENTITY_MISMATCH'
    $expectedArchive=Join-Path $Context.PrivateDirectory ('cp6-wp6-'+$Context.RunId+'-'+$SourceReceipt.Owner+$(if($Context.Provider -ceq 'SqlServer'){'.bak'}else{'.dump'}))
    Assert-Cp6 ($Backup.ArchivePath -ceq $expectedArchive -and $Backup.ReceiptPath -ceq ($expectedArchive+'.json')) 'CP6_COMPAT_BACKUP_PATH_MISMATCH'
    $recorded=Get-Content -LiteralPath $Backup.ReceiptPath -Raw | ConvertFrom-Json
    Assert-Cp6 (($Backup|ConvertTo-Json -Depth 30 -Compress) -ceq ($recorded|ConvertTo-Json -Depth 30 -Compress)) 'CP6_COMPAT_BACKUP_RECEIPT_CHANGED'
    Assert-Cp6 ((Get-FileHash -LiteralPath $Backup.ArchivePath -Algorithm SHA256).Hash -ceq $Backup.Sha256) 'CP6_COMPAT_BACKUP_HASH_MISMATCH'
    $moves=@()
    if($Context.Provider -ceq 'SqlServer'){
        Assert-Cp6 (@($Backup.SqlSourceFiles).Count -eq @($target.Files).Count) 'CP6_COMPAT_RESTORE_FILE_LAYOUT_UNSUPPORTED'
        foreach($type in @(0,1)){
            $sourceFiles=@($Backup.SqlSourceFiles|Where-Object FileType -EQ $type|Sort-Object FileId)
            $targetFiles=@($target.Files|Where-Object FileType -EQ $type|Sort-Object FileId)
            Assert-Cp6 ($sourceFiles.Count -eq $targetFiles.Count) 'CP6_COMPAT_RESTORE_FILE_LAYOUT_UNSUPPORTED'
            for($i=0;$i -lt $sourceFiles.Count;$i++){
                Assert-Cp6 ($targetFiles[$i].PhysicalName -cnotin @($Backup.SqlSourceFiles.PhysicalName)) 'CP6_COMPAT_RESTORE_FILE_OVERLAP'
                $moves+='MOVE '+(ConvertTo-Cp6SqlLiteral $sourceFiles[$i].LogicalName)+' TO '+(ConvertTo-Cp6SqlLiteral $targetFiles[$i].PhysicalName)
            }
        }
        Assert-Cp6 ($moves.Count -eq @($target.Files).Count) 'CP6_COMPAT_RESTORE_FILE_LAYOUT_UNSUPPORTED'
        # Bind the server-side file used by RESTORE to the already hashed private archive.
        Assert-Cp6 ((Get-FileHash -LiteralPath $Backup.SqlServerArchivePath -Algorithm SHA256).Hash -ceq $Backup.Sha256) 'CP6_COMPAT_BACKUP_SERVER_HASH_MISMATCH'
    }
    $null=Test-Cp6OwnedDatabase $Context $SourceReceipt -RequireNoSessions
    $null=Test-Cp6OwnedDatabase $Context $TargetReceipt -RequireEmpty -RequireNoSessions
    $TargetReceipt.Status='RestoreStarted';$TargetReceipt.Events+=@([pscustomobject]@{Action='RestoreStarted';SourceDatabaseName=$SourceReceipt.DatabaseName;ArchiveSha256=$Backup.Sha256;Utc=[datetime]::UtcNow.ToString('O')});Write-Cp6Json $TargetReceipt.ReceiptPath $TargetReceipt
    if($Context.Provider -ceq 'SqlServer'){
        Invoke-Cp6Statement $Context master ('RESTORE DATABASE ['+$TargetReceipt.DatabaseName+'] FROM DISK='+(ConvertTo-Cp6SqlLiteral $Backup.SqlServerArchivePath)+' WITH REPLACE,RECOVERY,CHECKSUM,'+($moves -join ',')+';')
        $restored=Get-Cp6ActualDatabase $Context $TargetReceipt.DatabaseName
        Assert-Cp6 ($restored.DatabaseId -eq $target.DatabaseId -and (@($restored.Files.PhysicalName|Sort-Object) -join '|') -ceq (@($target.Files.PhysicalName|Sort-Object) -join '|') -and $restored.Task -ceq $script:task -and $restored.Owner -ceq $SourceReceipt.Owner) 'CP6_COMPAT_RESTORE_PHYSICAL_OR_SOURCE_MARKER_MISMATCH'
        $tool=Get-Cp6NativeToolIdentity $Context sqlcmd
    }else{
        $null=Invoke-Cp6Native $Context pg_restore $TargetReceipt.DatabaseName -Arguments @('--exit-on-error','--single-transaction','--dbname',$TargetReceipt.DatabaseName,$Backup.ArchivePath)
        $restored=Get-Cp6ActualDatabase $Context $TargetReceipt.DatabaseName
        Assert-Cp6 ($restored.DatabaseId -eq $target.DatabaseId -and $restored.Task -ceq $script:task -and $restored.Owner -ceq $TargetReceipt.Owner -and $restored.Marker -ceq ($script:task+':'+$TargetReceipt.Owner)) 'CP6_COMPAT_RESTORE_PHYSICAL_OR_SOURCE_MARKER_MISMATCH'
        $restored=Sync-Cp6PostgreSqlDatabaseMetadata $Context $Backup.SourceState $TargetReceipt $restored
        $tool=Get-Cp6NativeToolIdentity $Context pg_restore
    }
    $null=Test-Cp6OwnedDatabase $Context $SourceReceipt -RequireNoSessions
    # SQL copied the source marker. Physical target identity is verified before remarking only this target.
    Set-Cp6Marker $Context $TargetReceipt
    $TargetReceipt.PhysicalIdentity=Get-Cp6PhysicalIdentity $Context $restored
    $TargetReceipt.Status='Restored';$TargetReceipt.Events+=@([pscustomobject]@{Action='RestoreVerifiedAndTargetMarkerReconciled';Provider=$Context.Provider;SourceOwner=$SourceReceipt.Owner;TargetOwner=$TargetReceipt.Owner;Utc=[datetime]::UtcNow.ToString('O')});Write-Cp6Json $TargetReceipt.ReceiptPath $TargetReceipt
    $final=Test-Cp6OwnedDatabase $Context $TargetReceipt -RequireNoSessions
    return [pscustomobject]@{RunId=$Context.RunId;Provider=$Context.Provider;SourceDatabaseName=$SourceReceipt.DatabaseName;TargetDatabaseName=$TargetReceipt.DatabaseName;ArchiveSha256=$Backup.Sha256;ToolIdentity=$tool;ServerVersion=$final.ServerVersion;NativeExitCode=0;Status='Restored';TargetPhysicalIdentity=$TargetReceipt.PhysicalIdentity;DatabaseMetadataReconciled=($Context.Provider -ceq 'PostgreSql');DataReconciliation='Required separately through ApplicationProbe';ActualState=$final}
}
Export-ModuleMember -Function New-Cp6LifecycleContext,New-Cp6OwnedDatabase,Get-Cp6OwnedDatabaseConnection,Test-Cp6OwnedDatabase,Remove-Cp6OwnedDatabases,Backup-Cp6OwnedDatabase,Restore-Cp6OwnedDatabase
