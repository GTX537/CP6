# Real HTTP clients only. The caller owns application PID/listener validation and database lifecycle.
# Serialize only each result's Public property. Private contains cookies, credentials, cursors and bodies.
Set-StrictMode -Version Latest
$script:Cp6HttpDiagnostics=[Collections.Generic.List[object]]::new()

function Get-Cp6HttpField($Object,[string]$Name) {
    if($Object -is [Collections.IDictionary]){if($Object.Contains($Name)){return ,$Object[$Name]}}
    elseif($null -ne $Object -and $null -ne $Object.PSObject.Properties[$Name]){return ,$Object.$Name}
    return $null
}
function Assert-Cp6Http([bool]$Condition,[string]$Code) {
    if(!$Condition){throw [InvalidOperationException]::new('CP6_WP6_HTTP_'+$Code)}
}
function Get-Cp6HttpDigest([string]$Value) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))).ToLowerInvariant()
}
function ConvertTo-Cp6HttpGuid($Value,[string]$Code) {
    $parsed=[guid]::Empty
    Assert-Cp6Http ($Value -is [string] -and [guid]::TryParse($Value,[ref]$parsed) -and $parsed -ne [guid]::Empty) $Code
    return $parsed
}
function Set-Cp6PrivateDisplay($Object) {
    # Default formatting cannot accidentally expand the private fixture into a public transcript.
    $display=[Management.Automation.PSPropertySet]::new('DefaultDisplayPropertySet',[string[]]@('Public'))
    $Object | Add-Member -MemberType MemberSet -Name PSStandardMembers -Value ([Management.Automation.PSMemberInfo[]]@($display))
    return $Object
}
function Resolve-Cp6HttpBase([uri]$BaseUri) {
    Assert-Cp6Http ($BaseUri.IsAbsoluteUri -and $BaseUri.Scheme -cin @('http','https') -and
        $BaseUri.DnsSafeHost -cin @('localhost','127.0.0.1','::1') -and
        $BaseUri.Port -ge 1 -and $BaseUri.Port -le 65535 -and
        [string]::IsNullOrEmpty($BaseUri.UserInfo) -and [string]::IsNullOrEmpty($BaseUri.Query) -and
        [string]::IsNullOrEmpty($BaseUri.Fragment) -and $BaseUri.AbsolutePath -ceq '/') 'LOOPBACK_BASE_REQUIRED'
    return $BaseUri
}
function New-Cp6HttpContainer([uri]$BaseUri,[string]$UserName,[string]$TenantCode) {
    $base=Resolve-Cp6HttpBase $BaseUri
    $cookies=[Net.CookieContainer]::new()
    $handler=[Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect=$false
    $handler.UseProxy=$false
    $handler.UseCookies=$true
    $handler.CookieContainer=$cookies
    $client=[Net.Http.HttpClient]::new($handler,$true)
    $client.Timeout=[TimeSpan]::FromSeconds(45)
    $client.MaxResponseContentBufferSize=16777216
    $client.DefaultRequestHeaders.Accept.ParseAdd('application/json')
    return (Set-Cp6PrivateDisplay ([pscustomobject]@{
        Public=[pscustomobject]@{Success=$false;BaseUri=$base.AbsoluteUri;UserName=$UserName;TenantCode=$TenantCode;UserId=$null;TenantId=$null;Connected=$true}
        Private=[pscustomobject]@{BaseUri=$base;Client=$client;Cookies=$cookies;LoginProfile=$null;UserId=$null;TenantId=$null;Diagnostics=$script:Cp6HttpDiagnostics}
    }))
}
function Invoke-Cp6Api($Session,[string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{}) {
    Assert-Cp6Http ($null -ne $Session -and $null -ne $Session.Private.Client -and $Session.Public.Connected) 'SESSION_REQUIRED'
    Assert-Cp6Http ($Path.StartsWith('/api/',[StringComparison]::Ordinal) -and !$Path.Contains('#') -and !$Path.Contains("`r") -and !$Path.Contains("`n")) 'API_PATH_REQUIRED'
    $uri=[uri]::new($Session.Private.BaseUri,$Path)
    Assert-Cp6Http ($uri.Authority -ceq $Session.Private.BaseUri.Authority -and $uri.Scheme -ceq $Session.Private.BaseUri.Scheme) 'SAME_ORIGIN_REQUIRED'
    Assert-Cp6Http ($uri.AbsolutePath.StartsWith('/api/',[StringComparison]::Ordinal)) 'CANONICAL_API_PATH_REQUIRED'
    $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method),$uri)
    $response=$null
    try {
        if($Method -cin @('POST','PUT','PATCH','DELETE') -and $uri.AbsolutePath -cne '/api/auth/login') {
            $csrf=$Session.Private.Cookies.GetCookies($uri)['cp6_csrf']
            Assert-Cp6Http ($null -ne $csrf -and ![string]::IsNullOrWhiteSpace($csrf.Value)) 'CSRF_COOKIE_REQUIRED'
            $request.Headers.Add('X-CSRF-Token',$csrf.Value)
        }
        foreach($name in $Headers.Keys){$request.Headers.Add($name,[string]$Headers[$name])}
        if($null -ne $Body){$request.Content=[Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 30 -Compress),[Text.Encoding]::UTF8,'application/json')}
        try {
            $response=$Session.Private.Client.SendAsync($request).GetAwaiter().GetResult()
            $text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        }
        catch {
            $script:Cp6HttpDiagnostics.Add([pscustomobject]@{AtUtc=[DateTimeOffset]::UtcNow;Method=$Method;Path=$uri.AbsolutePath;StatusCode=$null;Body=$null;PrivateTransportError=$_.Exception.ToString()})
            throw [InvalidOperationException]::new('CP6_WP6_HTTP_TRANSPORT_FAILED')
        }
        $status=[int]$response.StatusCode
        $script:Cp6HttpDiagnostics.Add([pscustomobject]@{AtUtc=[DateTimeOffset]::UtcNow;Method=$Method;Path=$uri.AbsolutePath;StatusCode=$status;Body=$text;PrivateTransportError=$null})
        Assert-Cp6Http ($status -lt 300 -or $status -ge 400) 'REDIRECT_REJECTED'
        $json=$null
        if(![string]::IsNullOrWhiteSpace($text)) { try{$json=ConvertFrom-Json -InputObject $text -ErrorAction Stop}catch{ $json=$null } }
        return [pscustomobject]@{StatusCode=$status;Json=$json;PrivateBody=$text}
    }
    finally {if($null -ne $response){$response.Dispose()};$request.Dispose()}
}
function Read-Cp6HttpIdentity($Session) {
    $cookies=$Session.Private.Cookies.GetCookies([uri]::new($Session.Private.BaseUri,'/api/auth/profile'))
    foreach($name in @('cp6_at','cp6_rt','cp6_csrf')) {
        Assert-Cp6Http ($null -ne $cookies[$name] -and ![string]::IsNullOrWhiteSpace($cookies[$name].Value)) 'LOGIN_COOKIES_REQUIRED'
    }
    try {
        # Identity extraction only. The server authenticates this cookie on the actual profile request.
        $parts=$cookies['cp6_at'].Value.Split('.')
        Assert-Cp6Http ($parts.Count -eq 3) 'ACCESS_COOKIE_SHAPE'
        $payload=$parts[1].Replace('-','+').Replace('_','/')
        $payload=$payload.PadRight($payload.Length+((4-$payload.Length%4)%4),'=')
        $claims=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json -ErrorAction Stop
        $user=Get-Cp6HttpField $claims 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'
        if($null -eq $user){$user=Get-Cp6HttpField $claims 'nameid'}
        $tenant=Get-Cp6HttpField $claims 'tenant_id'
        $userId=[guid]$user;$tenantId=[guid]$tenant
        Assert-Cp6Http ($userId -ne [guid]::Empty -and $tenantId -ne [guid]::Empty) 'IDENTITY_GUIDS_REQUIRED'
        $Session.Public.UserId=$userId.ToString('D');$Session.Public.TenantId=$tenantId.ToString('D')
        $Session.Private.UserId=$Session.Public.UserId;$Session.Private.TenantId=$Session.Public.TenantId
    }
    catch { throw [InvalidOperationException]::new('CP6_WP6_HTTP_LOGIN_IDENTITY_INVALID') }
}
function Connect-Cp6HttpUser([uri]$BaseUri,[string]$UserName,[string]$Password,[string]$TenantCode,[bool]$AllowPasswordChange) {
    $session=New-Cp6HttpContainer $BaseUri $UserName $TenantCode
    try {
        $login=Invoke-Cp6Api $session POST '/api/auth/login' @{userName=$UserName;password=$Password;tenantCode=$TenantCode}
        Assert-Cp6Http ($login.StatusCode -eq 200 -and $null -ne $login.Json) 'LOGIN_200_REQUIRED'
        Assert-Cp6Http ((Get-Cp6HttpField $login.Json 'userName') -ceq $UserName -and (Get-Cp6HttpField $login.Json 'mustChangePassword') -is [bool]) 'LOGIN_PROFILE_REQUIRED'
        Assert-Cp6Http ($AllowPasswordChange -or $login.Json.mustChangePassword -eq $false) 'PASSWORD_CHANGE_REQUIRED'
        $session.Private.LoginProfile=$login.Json
        Read-Cp6HttpIdentity $session
        if(!$login.Json.mustChangePassword) {
            $profile=Invoke-Cp6Api $session GET '/api/auth/profile'
            Assert-Cp6Http ($profile.StatusCode -eq 200 -and (Get-Cp6HttpField $profile.Json 'userName') -ceq $UserName -and
                (Get-Cp6HttpField $profile.Json 'mustChangePassword') -is [bool] -and !$profile.Json.mustChangePassword) 'PROFILE_AUTHENTICATION_REQUIRED'
        }
        $session.Public.Success=$true
        return $session
    }
    catch {Close-Cp6ApiSession $session;throw}
}
function New-Cp6HttpResult([string]$Stage,[array]$Checks,$Private) {
    return (Set-Cp6PrivateDisplay ([pscustomobject]@{
        Public=[pscustomobject]@{Task='DB-COMPAT-01-WP6';Stage=$Stage;Success=(@($Checks | Where-Object {$_.Passed -ne $true}).Count -eq 0);Checks=$Checks;ExpectedCases=$Checks.Count;VerificationKind='Actual loopback HTTP requests; application process ownership is checked by the caller.'}
        Private=$Private
    }))
}
function Add-Cp6HttpCheck($Checks,[string]$Name,[bool]$Passed,[Nullable[int]]$StatusCode=$null) {
    $Checks.Add([pscustomobject]@{Name=$Name;Passed=$Passed;StatusCode=$StatusCode})
    Assert-Cp6Http $Passed ($Name.ToUpperInvariant().Replace('-','_'))
}
function Test-Cp6PermissionDenial($Response,[string]$Permission) {
    return $Response.StatusCode -eq 403 -and (Get-Cp6HttpField $Response.Json 'code') -eq 403 -and
        ([string](Get-Cp6HttpField $Response.Json 'message')).Contains($Permission,[StringComparison]::Ordinal) -and
        !$Response.PrivateBody.Contains('E-SEC-009',[StringComparison]::Ordinal)
}

function New-Cp6ApiSession {
    [CmdletBinding()]
    param([Parameter(Mandatory)][uri]$BaseUri,[Parameter(Mandatory)][string]$AdminPassword,
        [string]$UserName='admin',[string]$TenantCode='DEFAULT')
    Assert-Cp6Http (![string]::IsNullOrWhiteSpace($AdminPassword)) 'PASSWORD_REQUIRED'
    return (Connect-Cp6HttpUser $BaseUri $UserName $AdminPassword $TenantCode $false)
}

function Test-Cp6ApiAuthorization {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Session)
    $checks=[Collections.Generic.List[object]]::new()
    $anonymous=$null;$initial=$null;$limited=$null
    $userName='wp6_'+[guid]::NewGuid().ToString('N')
    $initialPassword='Wp6!aA7-'+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $finalPassword='Wp6!bB8-'+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    try {
        $profile=Invoke-Cp6Api $Session GET '/api/auth/profile'
        Add-Cp6HttpCheck $checks 'admin-profile-authenticated' ($profile.StatusCode -eq 200 -and $profile.Json.userName -ceq $Session.Public.UserName -and $profile.Json.mustChangePassword -eq $false) $profile.StatusCode
        $roles=Invoke-Cp6Api $Session GET '/api/role?page=1&pageSize=100'
        Add-Cp6HttpCheck $checks 'admin-role-query-authorized' ($roles.StatusCode -eq 200 -and (Get-Cp6HttpField $roles.Json 'rows') -is [array]) $roles.StatusCode
        $users=Invoke-Cp6Api $Session GET ('/api/user?page=1&pageSize=100&keyword='+[uri]::EscapeDataString($Session.Public.UserName))
        $self=@((Get-Cp6HttpField $users.Json 'rows') | Where-Object {$_.userName -ceq $Session.Public.UserName -and $_.id -eq $Session.Public.UserId})
        Add-Cp6HttpCheck $checks 'admin-id-matches-user-api' ($users.StatusCode -eq 200 -and $self.Count -eq 1) $users.StatusCode
        $anonymous=New-Cp6HttpContainer $Session.Private.BaseUri 'anonymous' $Session.Public.TenantCode
        $anonRole=Invoke-Cp6Api $anonymous GET '/api/role'
        Add-Cp6HttpCheck $checks 'anonymous-role-rejected' ($anonRole.StatusCode -eq 401) $anonRole.StatusCode
        $anonProfile=Invoke-Cp6Api $anonymous GET '/api/auth/profile'
        Add-Cp6HttpCheck $checks 'anonymous-profile-rejected' ($anonProfile.StatusCode -eq 401) $anonProfile.StatusCode
        $created=Invoke-Cp6Api $Session POST '/api/user' @{userName=$userName;password=$initialPassword;nickName='WP6 permission fixture';roleId=$null;enable=$true;isPlatformAdmin=$false}
        $newId=ConvertTo-Cp6HttpGuid (Get-Cp6HttpField $created.Json 'id') 'CREATED_USER_ID_INVALID'
        Add-Cp6HttpCheck $checks 'unprivileged-user-created-through-api' ($created.StatusCode -eq 200 -and $newId -ne [guid]::Empty -and $null -eq (Get-Cp6HttpField $created.Json 'roleId')) $created.StatusCode
        $initial=Connect-Cp6HttpUser $Session.Private.BaseUri $userName $initialPassword $Session.Public.TenantCode $true
        Add-Cp6HttpCheck $checks 'new-user-requires-password-change' ($initial.Private.LoginProfile.mustChangePassword -eq $true -and $initial.Public.UserId -eq $newId.ToString('D') -and $initial.Public.TenantId -eq $Session.Public.TenantId) 200
        $changed=Invoke-Cp6Api $initial POST '/api/auth/change-password' @{currentPassword=$initialPassword;newPassword=$finalPassword}
        Add-Cp6HttpCheck $checks 'new-user-password-changed-through-api' ($changed.StatusCode -eq 200 -and (Get-Cp6HttpField $changed.Json 'code') -eq 0) $changed.StatusCode
        $limited=Connect-Cp6HttpUser $Session.Private.BaseUri $userName $finalPassword $Session.Public.TenantCode $false
        $limitedProfile=Invoke-Cp6Api $limited GET '/api/auth/profile'
        Add-Cp6HttpCheck $checks 'unprivileged-profile-authenticated-after-relogin' ($limitedProfile.StatusCode -eq 200 -and $limitedProfile.Json.mustChangePassword -eq $false -and $limitedProfile.Json.isPlatformAdmin -eq $false -and $null -eq $limitedProfile.Json.roleId -and $limited.Public.UserId -eq $newId.ToString('D')) $limitedProfile.StatusCode
        $denied=Invoke-Cp6Api $limited GET '/api/role'
        Add-Cp6HttpCheck $checks 'unprivileged-role-denied-by-permission' (Test-Cp6PermissionDenial $denied 'role:query') $denied.StatusCode
        $inbox=Invoke-Cp6Api $limited GET '/api/oa/notification/list?page=1&pageSize=100'
        Add-Cp6HttpCheck $checks 'unprivileged-own-inbox-readable' ($inbox.StatusCode -eq 200 -and (Get-Cp6HttpField $inbox.Json 'code') -eq 0) $inbox.StatusCode
        $readDenied=Invoke-Cp6Api $limited POST '/api/oa/notification/read-all' @{}
        Add-Cp6HttpCheck $checks 'unprivileged-inbox-write-denied-by-permission' (Test-Cp6PermissionDenial $readDenied 'oa-inbox:read') $readDenied.StatusCode
        $result=New-Cp6HttpResult 'authorization' $checks.ToArray() ([pscustomobject]@{UserName=$userName;Password=$finalPassword;UserId=$newId.ToString('D');TenantId=$Session.Public.TenantId;TenantCode=$Session.Public.TenantCode;AdminUserId=$Session.Public.UserId;AdminTenantId=$Session.Public.TenantId;Diagnostics=$script:Cp6HttpDiagnostics})
        $result.Public | Add-Member UserId $newId.ToString('D')
        return $result
    }
    finally {foreach($client in @($anonymous,$initial,$limited)){if($null -ne $client){Close-Cp6ApiSession $client}}}
}

function New-Cp6AssetCursorFixture {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Session)
    $checks=[Collections.Generic.List[object]]::new()
    $nonce=[guid]::NewGuid().ToString('N');$category='wp6-'+$nonce
    $ids=[Collections.Generic.List[string]]::new()
    foreach($suffix in @('a','b')) {
        $code='wp6-'+$nonce+'-'+$suffix
        $created=Invoke-Cp6Api $Session POST '/api/space/design/v1/assets' @{
            assetCode=$code;name=('WP6 asset '+$suffix);category=$category;format='Glb';parameterSchemaJson='{}'
            contentHash=(Get-Cp6HttpDigest ('WP6 procedural fixture '+$code));scope='Tenant'
        } @{'Idempotency-Key'=('wp6-'+[guid]::NewGuid().ToString('N'))}
        $asset=Get-Cp6HttpField $created.Json 'asset';$id=ConvertTo-Cp6HttpGuid (Get-Cp6HttpField $asset 'id') 'CREATED_ASSET_ID_INVALID'
        Add-Cp6HttpCheck $checks ('asset-'+$suffix+'-created-through-api') ($created.StatusCode -eq 201 -and $id -ne [guid]::Empty -and
            (Get-Cp6HttpField $asset 'scope') -ceq 'Tenant' -and (Get-Cp6HttpField $asset 'assetCode') -ceq $code -and
            (Get-Cp6HttpField $asset 'category') -ceq $category -and (Get-Cp6HttpField $created.Json 'idempotentReplay') -eq $false) $created.StatusCode
        $ids.Add($id.ToString('D'))
    }
    Assert-Cp6Http ($ids[0] -cne $ids[1]) 'ASSET_IDS_DISTINCT'
    $path='/api/space/design/v1/assets?scope=Tenant&category='+[uri]::EscapeDataString($category)+'&limit=1'
    $first=Invoke-Cp6Api $Session GET $path
    $firstItems=Get-Cp6HttpField $first.Json 'items';$cursor=Get-Cp6HttpField $first.Json 'nextCursor'
    Add-Cp6HttpCheck $checks 'asset-first-page-exact-and-protected-cursor-issued' ($first.StatusCode -eq 200 -and $firstItems -is [array] -and $firstItems.Count -eq 1 -and $firstItems[0].id -eq $ids[0] -and
        $cursor -is [string] -and ![string]::IsNullOrWhiteSpace($cursor)) $first.StatusCode
    $issued=[DateTimeOffset]::UtcNow
    $second=Invoke-Cp6Api $Session GET ($path+'&cursor='+[uri]::EscapeDataString($cursor))
    $secondItems=Get-Cp6HttpField $second.Json 'items'
    Add-Cp6HttpCheck $checks 'asset-second-page-exact-before-backup' ($second.StatusCode -eq 200 -and $secondItems -is [array] -and $secondItems.Count -eq 1 -and $secondItems[0].id -eq $ids[1] -and
        [string]::IsNullOrEmpty([string](Get-Cp6HttpField $second.Json 'nextCursor'))) $second.StatusCode
    $result=New-Cp6HttpResult 'asset-cursor-capture' $checks.ToArray() ([pscustomobject]@{Cursor=$cursor;Path=$path;Category=$category;FirstId=$ids[0];SecondId=$ids[1];UserId=$Session.Public.UserId;TenantId=$Session.Public.TenantId;CursorIssuedUtc=$issued;Diagnostics=$script:Cp6HttpDiagnostics})
    $result.Public | Add-Member AssetIds @($ids.ToArray())
    $result.Public | Add-Member CursorIssuedUtc $issued
    return $result
}

function Test-Cp6RestoredApiFixture {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Session,[Parameter(Mandatory)]$AuthorizationFixture,[Parameter(Mandatory)]$AssetFixture,
        [string]$NotificationIdSha256)
    $checks=[Collections.Generic.List[object]]::new();$limited=$null;$anonymous=$null
    try {
        Add-Cp6HttpCheck $checks 'restored-session-same-user-and-tenant' ($Session.Public.UserId -eq $AssetFixture.Private.UserId -and $Session.Public.TenantId -eq $AssetFixture.Private.TenantId) 200
        $profile=Invoke-Cp6Api $Session GET '/api/auth/profile'
        Add-Cp6HttpCheck $checks 'restored-admin-profile-authenticated' ($profile.StatusCode -eq 200 -and $profile.Json.userName -ceq $Session.Public.UserName -and $profile.Json.mustChangePassword -eq $false) $profile.StatusCode
        $roles=Invoke-Cp6Api $Session GET '/api/role'
        Add-Cp6HttpCheck $checks 'restored-admin-role-query-authorized' ($roles.StatusCode -eq 200 -and (Get-Cp6HttpField $roles.Json 'rows') -is [array]) $roles.StatusCode
        $continued=Invoke-Cp6Api $Session GET ($AssetFixture.Private.Path+'&cursor='+[uri]::EscapeDataString($AssetFixture.Private.Cursor))
        $items=Get-Cp6HttpField $continued.Json 'items'
        Add-Cp6HttpCheck $checks 'restored-original-cursor-returns-exact-second-asset' ($continued.StatusCode -eq 200 -and $items -is [array] -and $items.Count -eq 1 -and
            $items[0].id -eq $AssetFixture.Private.SecondId -and $items[0].scope -ceq 'Tenant' -and $items[0].category -ceq $AssetFixture.Private.Category -and
            [string]::IsNullOrEmpty([string](Get-Cp6HttpField $continued.Json 'nextCursor'))) $continued.StatusCode
        $limited=Connect-Cp6HttpUser $Session.Private.BaseUri $AuthorizationFixture.Private.UserName $AuthorizationFixture.Private.Password $AuthorizationFixture.Private.TenantCode $false
        Add-Cp6HttpCheck $checks 'restored-unprivileged-user-same-identity' ($limited.Public.UserId -eq $AuthorizationFixture.Private.UserId -and $limited.Public.TenantId -eq $AuthorizationFixture.Private.TenantId -and
            $limited.Private.LoginProfile.mustChangePassword -eq $false -and $limited.Private.LoginProfile.isPlatformAdmin -eq $false -and $null -eq $limited.Private.LoginProfile.roleId) 200
        $denied=Invoke-Cp6Api $limited GET '/api/role'
        Add-Cp6HttpCheck $checks 'restored-unprivileged-role-denied-by-permission' (Test-Cp6PermissionDenial $denied 'role:query') $denied.StatusCode
        $adminInbox=Invoke-Cp6Api $Session GET '/api/oa/notification/list?page=1&pageSize=100'
        $adminRows=Get-Cp6HttpField $adminInbox.Json 'data'
        Add-Cp6HttpCheck $checks 'restored-admin-inbox-readable' ($adminInbox.StatusCode -eq 200 -and (Get-Cp6HttpField $adminInbox.Json 'code') -eq 0 -and $adminRows -is [array]) $adminInbox.StatusCode
        $limitedInbox=Invoke-Cp6Api $limited GET '/api/oa/notification/list?page=1&pageSize=100'
        $limitedRows=Get-Cp6HttpField $limitedInbox.Json 'data'
        Add-Cp6HttpCheck $checks 'restored-unprivileged-own-inbox-readable' ($limitedInbox.StatusCode -eq 200 -and (Get-Cp6HttpField $limitedInbox.Json 'code') -eq 0 -and $limitedRows -is [array]) $limitedInbox.StatusCode
        if(![string]::IsNullOrWhiteSpace($NotificationIdSha256)) {
            Assert-Cp6Http ($NotificationIdSha256 -cmatch '\A[0-9a-fA-F]{64}\z') 'NOTIFICATION_DIGEST_REQUIRED'
            $matching=@($adminRows | Where-Object {(Get-Cp6HttpDigest ((ConvertTo-Cp6HttpGuid $_.id 'NOTIFICATION_ID_INVALID').ToString('D'))) -ieq $NotificationIdSha256})
            $leaked=@($limitedRows | Where-Object {(Get-Cp6HttpDigest ((ConvertTo-Cp6HttpGuid $_.id 'NOTIFICATION_ID_INVALID').ToString('D'))) -ieq $NotificationIdSha256})
            Add-Cp6HttpCheck $checks 'restored-exact-notification-visible-to-owner' ($matching.Count -eq 1) $adminInbox.StatusCode
            Add-Cp6HttpCheck $checks 'restored-exact-notification-hidden-from-other-user' ($leaked.Count -eq 0) $limitedInbox.StatusCode
        }
        $readDenied=Invoke-Cp6Api $limited POST '/api/oa/notification/read-all' @{}
        Add-Cp6HttpCheck $checks 'restored-unprivileged-inbox-write-denied-by-permission' (Test-Cp6PermissionDenial $readDenied 'oa-inbox:read') $readDenied.StatusCode
        $anonymous=New-Cp6HttpContainer $Session.Private.BaseUri 'anonymous' $Session.Public.TenantCode
        $anon=Invoke-Cp6Api $anonymous GET '/api/oa/notification/list'
        Add-Cp6HttpCheck $checks 'restored-anonymous-inbox-rejected' ($anon.StatusCode -eq 401) $anon.StatusCode
        $result=New-Cp6HttpResult 'restored-api' $checks.ToArray() ([pscustomobject]@{Diagnostics=$script:Cp6HttpDiagnostics})
        $result.Public | Add-Member NotificationIdentityChecked (![string]::IsNullOrWhiteSpace($NotificationIdSha256))
        return $result
    }
    finally {foreach($client in @($limited,$anonymous)){if($null -ne $client){Close-Cp6ApiSession $client}}}
}

function Close-Cp6ApiSession {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Session)
    if($null -ne $Session.Private.Client){$Session.Private.Client.Dispose();$Session.Private.Client=$null}
    $Session.Private.Cookies=[Net.CookieContainer]::new()
    $Session.Private.LoginProfile=$null
    $Session.Public.Connected=$false
}
function Get-Cp6HttpPrivateDiagnostics {
    [CmdletBinding()]
    param()
    # Explicit private-only accessor. Bodies may contain cursors or sensitive server diagnostics.
    return ,$script:Cp6HttpDiagnostics.ToArray()
}
Export-ModuleMember -Function New-Cp6ApiSession,Test-Cp6ApiAuthorization,New-Cp6AssetCursorFixture,Test-Cp6RestoredApiFixture,Close-Cp6ApiSession,Get-Cp6HttpPrivateDiagnostics
