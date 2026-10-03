# Pure offline control-flow tests. Invoke-Cp6Api is replaced inside the module before any client call.
# No listener, HTTP request, application process, database or native acceptance runs here.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'DatabaseCompatibilityHttp.psm1') -Force
$module=Get-Module DatabaseCompatibilityHttp
$checked=0
function Assert-Offline([bool]$Condition,[string]$Name){if(!$Condition){throw "HTTP offline assertion failed: $Name"};$script:checked++}
function Assert-Rejected([scriptblock]$Action,[string]$Code){
    $caught=$null;try{& $Action}catch{$caught=$_.Exception.Message}
    Assert-Offline ($null -ne $caught -and $caught.Contains($Code,[StringComparison]::Ordinal)) "rejected $Code; actual=$caught"
}
$null=& $module {
    $script:HttpOriginalInvoke=(Get-Command Invoke-Cp6Api -CommandType Function).ScriptBlock
    $script:HttpOfflineFault=''
    $script:HttpOfflineNotificationId=[guid]::NewGuid().ToString('D')
    $script:HttpOfflineTenant='00000000-0000-0000-0000-0000000000a1'
    $script:HttpOfflineUsers=@{admin=[pscustomobject]@{Id=[guid]::NewGuid().ToString('D');UserName='admin';Password='Offline-admin-only!12';MustChange=$false;RoleId=1;Platform=$true}}
    $script:HttpOfflineAssets=[Collections.Generic.List[object]]::new()
    $script:HttpOfflineCalls=[Collections.Generic.List[object]]::new()
    function script:Invoke-Cp6Api($Session,[string]$Method,[string]$Path,$Body=$null,[hashtable]$Headers=@{}) {
        $script:HttpOfflineCalls.Add([pscustomobject]@{Method=$Method;Path=$Path})
        $user=$script:HttpOfflineUsers[$Session.Public.UserName]
        $status=200;$json=$null
        if($Path -ceq '/api/auth/login') {
            $user=$script:HttpOfflineUsers[$Body.userName]
            Assert-Cp6Http ($null -ne $user -and $Body.password -ceq $user.Password) 'OFFLINE_CREDENTIAL_MISMATCH'
            $claims=@{'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'=$user.Id;tenant_id=$script:HttpOfflineTenant}
            $payload=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($claims | ConvertTo-Json -Compress))).TrimEnd('=').Replace('+','-').Replace('/','_')
            $Session.Private.Cookies.Add($Session.Private.BaseUri,[Net.Cookie]::new('cp6_at','offline.'+$payload+'.signature','/'))
            $Session.Private.Cookies.Add($Session.Private.BaseUri,[Net.Cookie]::new('cp6_rt','offline-private-refresh','/api/auth'))
            if($script:HttpOfflineFault -cne 'missing-csrf'){$Session.Private.Cookies.Add($Session.Private.BaseUri,[Net.Cookie]::new('cp6_csrf','offline-private-csrf','/'))}
            $json=[pscustomobject]@{userName=$user.UserName;mustChangePassword=$user.MustChange;roleId=$user.RoleId;isPlatformAdmin=$user.Platform;menus=@()}
        }
        elseif($Path -ceq '/api/auth/profile') {
            if($null -eq $user){$status=401}
            else{$json=[pscustomobject]@{userName=$user.UserName;mustChangePassword=$user.MustChange;roleId=$user.RoleId;isPlatformAdmin=$user.Platform;menus=@()}}
        }
        elseif($Path.StartsWith('/api/role',[StringComparison]::Ordinal)) {
            if($null -eq $user){$status=401}
            elseif($null -eq $user.RoleId){
                $status=403;$json=@{code=403;message='No permission: role:query'}
                if($script:HttpOfflineFault -ceq 'wrong-403'){$json=@{code='E-SEC-009';message='Password change required'}}
            }
            else{$json=@{rows=@(@{roleId=1;roleName='Admin'});total=1}}
        }
        elseif($Path.StartsWith('/api/user?',[StringComparison]::Ordinal)) {
            $json=@{rows=@($script:HttpOfflineUsers.Values | ForEach-Object {@{id=$_.Id;userName=$_.UserName;roleId=$_.RoleId}});total=$script:HttpOfflineUsers.Count}
        }
        elseif($Path -ceq '/api/user' -and $Method -ceq 'POST') {
            $id=[guid]::NewGuid().ToString('D')
            $script:HttpOfflineUsers[$Body.userName]=[pscustomobject]@{Id=$id;UserName=$Body.userName;Password=$Body.password;MustChange=$true;RoleId=$null;Platform=$false}
            $json=@{id=$id;userName=$Body.userName;roleId=$null;enable=$true}
        }
        elseif($Path -ceq '/api/auth/change-password') {
            Assert-Cp6Http ($Body.currentPassword -ceq $user.Password) 'OFFLINE_OLD_PASSWORD_MISMATCH'
            $user.Password=$Body.newPassword;$user.MustChange=$false;$json=@{code=0}
        }
        elseif($Path -ceq '/api/space/design/v1/assets' -and $Method -ceq 'POST') {
            Assert-Cp6Http ($Headers.ContainsKey('Idempotency-Key') -and $Body.scope -ceq 'Tenant') 'OFFLINE_ASSET_CONTRACT'
            $asset=[pscustomobject]@{id=[guid]::NewGuid().ToString('D');scope='Tenant';assetCode=$Body.assetCode;category=$Body.category}
            $script:HttpOfflineAssets.Add($asset);$status=201;$json=@{asset=$asset;idempotentReplay=$false}
        }
        elseif($Path.StartsWith('/api/space/design/v1/assets?',[StringComparison]::Ordinal)) {
            $query=@{};foreach($part in $Path.Split('?',2)[1].Split('&')){$pair=$part.Split('=',2);$query[$pair[0]]=[uri]::UnescapeDataString($pair[1])}
            $matching=@($script:HttpOfflineAssets | Where-Object category -CEQ $query.category | Sort-Object assetCode)
            $index=if($query.ContainsKey('cursor')){1}else{0}
            $next=if($index -eq 0){'offline-private-protected-cursor'}else{$null}
            $row=$matching[$index]
            if($script:HttpOfflineFault -ceq 'wrong-second' -and $index -eq 1){$row=[pscustomobject]@{id=[guid]::NewGuid().ToString('D');scope='Tenant';category=$query.category}}
            $json=@{items=@($row);nextCursor=$next}
        }
        elseif($Path.StartsWith('/api/oa/notification/list',[StringComparison]::Ordinal)) {
            if($null -eq $user){$status=401}
            else {
                $rows=if($user.UserName -ceq 'admin' -or $script:HttpOfflineFault -ceq 'notification-leak'){@(@{id=$script:HttpOfflineNotificationId})}else{@()}
                $json=@{code=0;data=@($rows)}
            }
        }
        elseif($Path -ceq '/api/oa/notification/read-all'){$status=403;$json=@{code=403;message='No permission: oa-inbox:read'}}
        else{throw 'Unexpected offline fixture route.'}
        if($json -is [hashtable]){$json=$json | ConvertTo-Json -Depth 12 | ConvertFrom-Json}
        $text=if($null -eq $json){''}else{$json | ConvertTo-Json -Depth 12 -Compress}
        $script:Cp6HttpDiagnostics.Add([pscustomobject]@{Method=$Method;Path=$Path.Split('?')[0];StatusCode=$status;Body=$text})
        return [pscustomobject]@{StatusCode=$status;Json=$json;PrivateBody=$text}
    }
}
$session=$null;$restored=$null
try {
    foreach($base in @('http://example.test:1234/','ftp://127.0.0.1:1234/','http://user:password@127.0.0.1:1234/','http://127.0.0.1:1234/api/','http://127.0.0.1:1234/?secret=value','http://127.0.0.1:1234/#fragment')) {
        Assert-Rejected {New-Cp6ApiSession -BaseUri $base -AdminPassword 'offline'} 'LOOPBACK_BASE_REQUIRED'
    }
    $session=New-Cp6ApiSession -BaseUri 'http://127.0.0.1:14321/' -AdminPassword 'Offline-admin-only!12'
    Assert-Offline $session.Public.Success 'session authenticated through fake transport only'
    Assert-Offline ($session.Public.UserId -match '^[0-9a-f-]{36}$' -and $session.Public.TenantId -ceq '00000000-0000-0000-0000-0000000000a1') 'identity extraction'
    $authorization=Test-Cp6ApiAuthorization -Session $session
    Assert-Offline ($authorization.Public.Success -and $authorization.Public.Checks.Count -eq 12) 'authorization control flow'
    $assets=New-Cp6AssetCursorFixture -Session $session
    Assert-Offline ($assets.Public.Success -and $assets.Public.Checks.Count -eq 4) 'two asset pages control flow'
    $restored=New-Cp6ApiSession -BaseUri 'http://127.0.0.1:14322/' -AdminPassword 'Offline-admin-only!12'
    $notificationDigest=& $module {Get-Cp6HttpDigest $script:HttpOfflineNotificationId}
    $recovery=Test-Cp6RestoredApiFixture -Session $restored -AuthorizationFixture $authorization -AssetFixture $assets -NotificationIdSha256 $notificationDigest
    Assert-Offline ($recovery.Public.Success -and $recovery.Public.Checks.Count -eq 12 -and $recovery.Public.NotificationIdentityChecked) 'recovery control flow'
    foreach($fixture in @($authorization,$assets,$recovery)) {
        $public=$fixture.Public | ConvertTo-Json -Depth 15
        foreach($secret in @($authorization.Private.Password,$assets.Private.Cursor,'offline-private-refresh','offline-private-csrf')) {
            Assert-Offline (!$public.Contains($secret,[StringComparison]::Ordinal)) 'private value excluded from public result'
        }
    }
    Assert-Offline ((Get-Cp6HttpPrivateDiagnostics).Count -gt 10) 'diagnostic responses remain available privately'
    & $module {$script:HttpOfflineFault='wrong-403'}
    Assert-Rejected {Test-Cp6ApiAuthorization -Session $session} 'UNPRIVILEGED_ROLE_DENIED_BY_PERMISSION'
    & $module {$script:HttpOfflineFault='wrong-second'}
    Assert-Rejected {Test-Cp6RestoredApiFixture -Session $restored -AuthorizationFixture $authorization -AssetFixture $assets -NotificationIdSha256 $notificationDigest} 'RESTORED_ORIGINAL_CURSOR_RETURNS_EXACT_SECOND_ASSET'
    & $module {$script:HttpOfflineFault='notification-leak'}
    Assert-Rejected {Test-Cp6RestoredApiFixture -Session $restored -AuthorizationFixture $authorization -AssetFixture $assets -NotificationIdSha256 $notificationDigest} 'RESTORED_EXACT_NOTIFICATION_HIDDEN_FROM_OTHER_USER'
    & $module {$script:HttpOfflineFault='missing-csrf'}
    Assert-Rejected {New-Cp6ApiSession -BaseUri 'http://127.0.0.1:14323/' -AdminPassword 'Offline-admin-only!12'} 'LOGIN_COOKIES_REQUIRED'
    & $module {$script:HttpOfflineFault=''}
    $oldTenant=$assets.Private.TenantId;$assets.Private.TenantId=[guid]::NewGuid().ToString('D')
    Assert-Rejected {Test-Cp6RestoredApiFixture -Session $restored -AuthorizationFixture $authorization -AssetFixture $assets} 'RESTORED_SESSION_SAME_USER_AND_TENANT'
    $assets.Private.TenantId=$oldTenant
    # Exercise the original transport wrapper with an in-memory SendAsync object.
    # It returns HttpResponseMessage instances and never opens a socket.
    $wire=& $module {New-Cp6HttpContainer ([uri]'http://127.0.0.1:14324/') 'offline-wire' 'DEFAULT'}
    $wire.Private.Client.Dispose()
    $fake=[pscustomobject]@{StatusCode=200;Payload='{"ok":true}';SeenCsrf=$null;SeenBody=$null;Calls=0}
    $fake | Add-Member ScriptMethod SendAsync {
        param($request)
        $this.Calls++
        $this.SeenCsrf=if($request.Headers.Contains('X-CSRF-Token')){[string]::Join('', $request.Headers.GetValues('X-CSRF-Token'))}else{$null}
        $this.SeenBody=if($null -ne $request.Content){$request.Content.ReadAsStringAsync().GetAwaiter().GetResult()}else{$null}
        $response=[Net.Http.HttpResponseMessage]::new([Net.HttpStatusCode]$this.StatusCode)
        $response.Content=[Net.Http.StringContent]::new($this.Payload)
        return [Threading.Tasks.Task]::FromResult[Net.Http.HttpResponseMessage]($response)
    }
    $fake | Add-Member ScriptMethod Dispose {}
    $wire.Private.Client=$fake
    $wire.Private.Cookies.Add($wire.Private.BaseUri,[Net.Cookie]::new('cp6_csrf','offline-wire-csrf','/'))
    try {
        $wireResult=& $module {param($s) & $script:HttpOriginalInvoke $s POST '/api/user' @{name='offline'}} $wire
        Assert-Offline ($wireResult.StatusCode -eq 200 -and $fake.SeenCsrf -ceq 'offline-wire-csrf' -and $fake.SeenBody -ceq '{"name":"offline"}') 'actual transport wrapper JSON and CSRF'
        $fake.StatusCode=302;$fake.Payload='private-response-must-not-escape'
        Assert-Rejected {& $module {param($s) & $script:HttpOriginalInvoke $s GET '/api/auth/profile'} $wire} 'REDIRECT_REJECTED'
        Assert-Offline (@((Get-Cp6HttpPrivateDiagnostics) | Where-Object Body -CEQ 'private-response-must-not-escape').Count -eq 1) 'redirect body retained privately'
        $fake.StatusCode=200
        Assert-Rejected {& $module {param($s) & $script:HttpOriginalInvoke $s GET '/api/../outside'} $wire} 'CANONICAL_API_PATH_REQUIRED'
        $wire.Private.Cookies=[Net.CookieContainer]::new()
        Assert-Rejected {& $module {param($s) & $script:HttpOriginalInvoke $s POST '/api/user' @{name='offline'}} $wire} 'CSRF_COOKIE_REQUIRED'
        Assert-Offline ($fake.Calls -eq 2) 'invalid requests rejected before fake SendAsync'
    }
    finally {Close-Cp6ApiSession $wire}
    $calls=& $module {$script:HttpOfflineCalls.Count}
    [pscustomobject]@{Status='Passed';Scope='Offline HTTP control-flow tests with replaced transport; zero HTTP/network, application or database execution';Assertions=$checked;SimulatedCalls=$calls}
}
finally {
    foreach($client in @($session,$restored)){if($null -ne $client){Close-Cp6ApiSession $client}}
    Remove-Module DatabaseCompatibilityHttp -Force
}
