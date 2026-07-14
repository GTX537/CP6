#Requires -Version 5.1
<#
.SYNOPSIS
    WFS Event-Trigger Start (F-T3) -- HTTP e2e QA script.

.DESCRIPTION
    Exercises the timer / event / message trigger start paths + admin backend +
    validation against a RUNNING backend. Does NOT start the server. Apply seed.sql
    first, then run the backend against the isolated CP6DB_OA database:

        sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql

        cd <repo>\CP6.WebApi
        $env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
        dotnet run --urls "http://localhost:5181"

    Then:
        .\qa_flow_trigger.ps1
        .\qa_flow_trigger.ps1 -BaseUrl http://localhost:5181

    *** STATUS: written, not run. *** Authored per task F-T3 (write-only). Live QA is
    executed later by the main agent with a QA user present. Nothing here has run.

.NOTES
    PS5.1 compatibility (mirrors wfs-kernel-hardening/qa_kernel_hardening.ps1):
      - No && operator; sequential steps use explicit checks.
      - HttpSend wraps Invoke-WebRequest -UseBasicParsing and captures the REAL status
        code (needed to tell 201 new from 200 replay, and 400/401/404 apart).
      - All request bodies use ASCII-only values (no CJK).
      - Dev backend MUST have CSRF disabled (Security:Csrf:Enabled=false) -- the admin
        POSTs are cookie-auth'd; the JWT cp6_at cookie flows via -WebSession. The message
        /fire endpoint is [AllowAnonymous] + X-Api-Key header; under production CSRF
        (Enabled=true) it works ONLY because CsrfMiddleware carries a SHAPE-EXACT exemption
        for /api/oa/flow-triggers/{guid}/fire (wave-3 final-review C-1) -- CSRF *does*
        apply to all sibling admin endpoints, which stay cookie-auth'd and protected.

    Cross-checked endpoints (file:line in report):
      - Login       : POST /api/auth/login                         { userName, password } -> cp6_at cookie
      - Create      : POST /api/oa/flow-triggers                    -> data.{ id, apiKeyPlain }   [FlowTrigger.Edit]
      - Enable      : POST /api/oa/flow-triggers/{id}/enable        { enabled }                   [FlowTrigger.Edit]
      - ResetKey    : POST /api/oa/flow-triggers/{id}/reset-key     -> data.apiKeyPlain           [FlowTrigger.Edit]
      - ManualFire  : POST /api/oa/flow-triggers/{id}/manual-fire   -> 200 data.instanceId / 400 message  [FlowTrigger.Edit]
      - Fires       : GET  /api/oa/flow-triggers/{id}/fires         -> data: TriggerFireListItem[] [Authorize]
      - CronPreview : POST /api/oa/flow-triggers/cron-preview       { cron } -> data.next[5] / 400 [FlowTrigger.View]
      - MsgFire     : POST /api/oa/flow-triggers/{id}/fire          X-Api-Key + Idempotency-Key headers
                          -> 201 {instanceId} new / 200 {instanceId} replay / 400 / 401 / 404  [AllowAnonymous]
      - EchoEvent   : POST /api/oa/wf-trigger-echo/fire             { eventKey, eventId, payloadJson }
                          -> data.{ success, matchedCount, firedCount, message }                 [Authorize]

    Admin envelope: { code: 0, message: "OK", data: <payload> }.
    Message /fire envelope: NONE -- raw { instanceId } (201/200) or { code, message } (400/401/404).
    404 body (both unknown-GUID and disabled trigger): { code: 404, message: "trigger not found" }.
#>

param(
    [string]$BaseUrl     = "http://localhost:5181",
    [int]   $WaitSeconds = 100,   # timer short-cycle: cron */1 + 30s scan -> allow ~<=90s
    [int]   $PollSeconds = 5
)

$ErrorActionPreference = "Continue"
$PASS = 0; $FAIL = 0; $WARN = 0

# Seed constants (must match seed.sql)
$U_STARTER      = "DDDD0000-0000-0000-0000-0000000000B0"   # qa_wtr_starter, RoleId=1
$FLOW_OK        = "wtr-simple"
$FLOW_DISABLED  = "wtr-disabled-flow"
$T_EVENT        = "DDDD0000-0000-0000-0000-0000000000E1"   # wtr-event
$T_SHORT        = "DDDD0000-0000-0000-0000-0000000000E2"   # wtr-shortcycle
$T_BADSTARTER   = "DDDD0000-0000-0000-0000-0000000000E3"   # wtr-badstarter
$UNKNOWN_GUID   = "DDDD9999-9999-9999-9999-999999999999"

# Trigger type ints (WfTriggerType): Timer=0 Event=1 Message=2
$TT_TIMER = 0; $TT_MESSAGE = 2

# ---- Assertions --------------------------------------------------------------
function Chk {
    param([string]$Label, $Expected, $Actual)
    if ("$Expected" -eq "$Actual") { Write-Host "  PASS  $Label (=$Actual)" -ForegroundColor Green; $script:PASS++ }
    else { Write-Host "  FAIL  $Label  expected=[$Expected]  got=[$Actual]" -ForegroundColor Red; $script:FAIL++ }
}
function ChkTrue {
    param([string]$Label, $Cond)
    if ($Cond) { Write-Host "  PASS  $Label" -ForegroundColor Green; $script:PASS++ }
    else { Write-Host "  FAIL  $Label" -ForegroundColor Red; $script:FAIL++ }
}
function ChkContains {
    param([string]$Label, [string]$Needle, [string]$Haystack)
    if ("$Haystack".Contains($Needle)) { Write-Host "  PASS  $Label (found '$Needle')" -ForegroundColor Green; $script:PASS++ }
    else { Write-Host "  FAIL  $Label  '$Needle' not in [$Haystack]" -ForegroundColor Red; $script:FAIL++ }
}
function Warn([string]$msg) { Write-Host "  WARN  $msg" -ForegroundColor Yellow; $script:WARN++ }

# ---- HTTP core: captures the REAL status code -------------------------------
function HttpSend {
    param([string]$Method, [string]$Uri, $Session, $BodyRaw, [hashtable]$Headers)
    try {
        $p = @{ Method = $Method; Uri = $Uri; UseBasicParsing = $true }
        if ($Session) { $p.WebSession = $Session }
        if ($Headers) { $p.Headers = $Headers }
        if ($null -ne $BodyRaw) { $p.Body = $BodyRaw; $p.ContentType = "application/json" }
        $resp = Invoke-WebRequest @p
        $json = $null; try { $json = $resp.Content | ConvertFrom-Json } catch {}
        return @{ Status = [int]$resp.StatusCode; Content = "$($resp.Content)"; Json = $json }
    } catch [System.Net.WebException] {
        $r = $_.Exception.Response
        $status = 0; $body = ""
        if ($r -ne $null) {
            $status = [int]$r.StatusCode
            try { $sr = New-Object System.IO.StreamReader($r.GetResponseStream()); $body = $sr.ReadToEnd() } catch {}
        }
        $json = $null; try { $json = $body | ConvertFrom-Json } catch {}
        return @{ Status = $status; Content = "$body"; Json = $json }
    } catch {
        return @{ Status = 0; Content = "$($_.Exception.Message)"; Json = $null }
    }
}

function PostJson { param([string]$Uri, $Session, $BodyObj)
    return HttpSend "POST" $Uri $Session ($BodyObj | ConvertTo-Json -Compress) $null }
function GetJson  { param([string]$Uri, $Session)
    return HttpSend "GET" $Uri $Session $null $null }

function Login {
    param([string]$UserName, [string]$Password = "123456")
    try {
        Invoke-RestMethod -Method POST -Uri "$BaseUrl/api/auth/login" -UseBasicParsing `
            -ContentType "application/json" -Body (@{ userName = $UserName; password = $Password } | ConvertTo-Json -Compress) `
            -SessionVariable sess | Out-Null
        return $sess
    } catch {
        $raw = ""; try { $sr = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream()); $raw = $sr.ReadToEnd() } catch {}
        Write-Host "  LOGIN FAILED for $UserName : $raw" -ForegroundColor Red; $script:FAIL++; return $null
    }
}

# Create a trigger via the admin API; returns the raw HttpSend result.
function CreateTrigger {
    param($Session, [int]$Type, [string]$FlowKey, [string]$ConfigJson, [bool]$Enabled = $true, [string]$EventKey = $null)
    return PostJson "$BaseUrl/api/oa/flow-triggers" $Session @{
        flowKey = $FlowKey; triggerType = $Type; configJson = $ConfigJson
        enabled = $Enabled; eventKey = $EventKey; starterUserId = $U_STARTER
    }
}

# GET fires count / top row for a trigger.
function FiresList { param($Session, [string]$Id)
    $r = GetJson "$BaseUrl/api/oa/flow-triggers/$Id/fires?take=50" $Session
    if ($r.Status -ne 200 -or $null -eq $r.Json) { return @() }
    return @($r.Json.data)
}

# ---- Preflight ---------------------------------------------------------------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "WFS Event-Trigger Start QA  ($BaseUrl)" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

$s = Login "qa_wtr_starter"
if (-not $s) { Write-Host "ABORT: cannot login qa_wtr_starter (seed applied? backend up?)" -ForegroundColor Red; exit 1 }

# =============================================================================
# Scenario 1(preview) + 7 : cron preview + save validation (E-WF-022 / E-WF-023)
# =============================================================================
Write-Host ""
Write-Host "-- cron-preview + save validation (scenario 1 preview / scenario 7) --" -ForegroundColor Cyan

$prev = PostJson "$BaseUrl/api/oa/flow-triggers/cron-preview" $s @{ cron = "0 9 * * *" }
Chk "preview: daily 09:00 HTTP 200" 200 $prev.Status
if ($prev.Json) { Chk "preview: returns 5 future occurrences" 5 (@($prev.Json.data.next).Count) }

$prevBad = PostJson "$BaseUrl/api/oa/flow-triggers/cron-preview" $s @{ cron = "not a cron" }
Chk "preview: invalid cron -> 400" 400 $prevBad.Status
ChkContains "preview: 400 body carries E-WF-022" "E-WF-022" "$($prevBad.Content)"

# save-validation: bad cron -> E-WF-022
$badCron = CreateTrigger $s $TT_TIMER $FLOW_OK '{"cron":"not a cron"}'
Chk "save: bad cron -> 400" 400 $badCron.Status
ChkContains "save: bad cron -> E-WF-022" "E-WF-022" "$($badCron.Content)"

# save-validation: disabled target flow -> E-WF-023
$badFlow = CreateTrigger $s $TT_TIMER $FLOW_DISABLED '{"cron":"0 9 * * *"}'
Chk "save: disabled flow -> 400" 400 $badFlow.Status
ChkContains "save: disabled flow -> E-WF-023" "E-WF-023" "$($badFlow.Content)"

# =============================================================================
# Scenario 2 : manual test-fire a timer trigger -> instanceId + success fire row
# =============================================================================
Write-Host ""
Write-Host "-- manual test-fire (scenario 2) --" -ForegroundColor Cyan
$mkTimer = CreateTrigger $s $TT_TIMER $FLOW_OK '{"cron":"0 9 * * *"}'
Chk "manual: create timer trigger HTTP 200" 200 $mkTimer.Status
$timerId = $mkTimer.Json.data.id
if ($timerId) {
    $mf = PostJson "$BaseUrl/api/oa/flow-triggers/$timerId/manual-fire" $s @{}
    Chk "manual: manual-fire HTTP 200" 200 $mf.Status
    ChkTrue "manual: response carries instanceId" ($null -ne $mf.Json.data.instanceId)
    $fires = @(FiresList $s $timerId)
    ChkTrue "manual: fire log has >=1 row" ($fires.Count -ge 1)
    if ($fires.Count -ge 1) {
        ChkTrue "manual: top fire row has instanceId (success)" ($null -ne $fires[0].instanceId)
        ChkTrue "manual: top fire row error is null" ($null -eq $fires[0].error)
    }
} else { Warn "manual scenario skipped (create failed)" }

# =============================================================================
# Scenario 5 : message e2e (create+key-once / 201 / 200 replay / 401 / 400 / 400 oversize / whitelist)
# =============================================================================
Write-Host ""
Write-Host "-- message e2e (scenario 5) --" -ForegroundColor Cyan
$mkMsg = CreateTrigger $s $TT_MESSAGE $FLOW_OK '{"varsSchema":["orderNo","amount"]}'
Chk "msg: create message trigger HTTP 200" 200 $mkMsg.Status
$msgId  = $mkMsg.Json.data.id
$apiKey = $mkMsg.Json.data.apiKeyPlain
ChkTrue "msg: create returns one-time apiKeyPlain" (-not [string]::IsNullOrWhiteSpace($apiKey))

if ($msgId -and $apiKey) {
    # key shown only once: a subsequent GET must NOT expose the plaintext (only HasApiKey flag)
    $getBack = GetJson "$BaseUrl/api/oa/flow-triggers/$msgId" $s
    ChkTrue "msg: GET does NOT re-expose apiKeyPlain" ([string]::IsNullOrEmpty("$($getBack.Json.data.apiKeyPlain)"))
    ChkTrue "msg: GET reports hasApiKey=true" ([bool]$getBack.Json.data.hasApiKey)

    $fireUri = "$BaseUrl/api/oa/flow-triggers/$msgId/fire"
    $idem1 = "qa-msg-" + [guid]::NewGuid().ToString("N")

    # (a) first call: 201 + instanceId (raw body, NO envelope)
    $f1 = HttpSend "POST" $fireUri $null '{"orderNo":"O-1","amount":"100"}' @{ "X-Api-Key" = $apiKey; "Idempotency-Key" = $idem1 }
    Chk "msg: first fire -> 201" 201 $f1.Status
    $inst1 = "$($f1.Json.instanceId)"
    ChkTrue "msg: 201 body carries instanceId" (-not [string]::IsNullOrWhiteSpace($inst1))

    # (b) replay same Idempotency-Key: 200 + SAME instanceId
    $f2 = HttpSend "POST" $fireUri $null '{"orderNo":"O-1","amount":"100"}' @{ "X-Api-Key" = $apiKey; "Idempotency-Key" = $idem1 }
    Chk "msg: replay same idem -> 200" 200 $f2.Status
    Chk "msg: replay returns same instanceId" $inst1 "$($f2.Json.instanceId)"

    # (c) wrong key -> 401
    $f3 = HttpSend "POST" $fireUri $null '{"orderNo":"O-2"}' @{ "X-Api-Key" = "totally-wrong-key"; "Idempotency-Key" = ("qa-" + [guid]::NewGuid().ToString("N")) }
    Chk "msg: wrong key -> 401" 401 $f3.Status

    # (d) missing Idempotency-Key -> 400 (checked AFTER key verification in the auth filter)
    $f4 = HttpSend "POST" $fireUri $null '{"orderNo":"O-3"}' @{ "X-Api-Key" = $apiKey }
    Chk "msg: missing Idempotency-Key -> 400" 400 $f4.Status

    # (e) oversize body (> 64KB) -> 400 (checked inside controller, after auth passes)
    $big = '{"orderNo":"' + ("x" * 66000) + '"}'
    $f5 = HttpSend "POST" $fireUri $null $big @{ "X-Api-Key" = $apiKey; "Idempotency-Key" = ("qa-" + [guid]::NewGuid().ToString("N")) }
    Chk "msg: 65KB+ body -> 400" 400 $f5.Status

    # (f) whitelist: extra field 'secret' not in varsSchema -> accepted 201, but dropped from VarsJson
    $idemW = "qa-wl-" + [guid]::NewGuid().ToString("N")
    $f6 = HttpSend "POST" $fireUri $null '{"orderNo":"O-4","amount":"9","secret":"leak"}' @{ "X-Api-Key" = $apiKey; "Idempotency-Key" = $idemW }
    Chk "msg: whitelisted fire -> 201" 201 $f6.Status
    Write-Host "        (verify whitelist in DB: VarsJson of instance $($f6.Json.instanceId) keeps orderNo/amount, drops 'secret')" -ForegroundColor DarkGray

    # =========================================================================
    # Scenario 6 : key reset -> old key 401, new key 201
    # =========================================================================
    Write-Host ""
    Write-Host "-- key reset (scenario 6) --" -ForegroundColor Cyan
    $rk = PostJson "$BaseUrl/api/oa/flow-triggers/$msgId/reset-key" $s @{}
    Chk "reset: HTTP 200" 200 $rk.Status
    $newKey = $rk.Json.data.apiKeyPlain
    ChkTrue "reset: returns a new one-time key" (-not [string]::IsNullOrWhiteSpace($newKey))
    ChkTrue "reset: new key differs from old" ($newKey -ne $apiKey)

    $rOld = HttpSend "POST" $fireUri $null '{"orderNo":"O-5"}' @{ "X-Api-Key" = $apiKey; "Idempotency-Key" = ("qa-" + [guid]::NewGuid().ToString("N")) }
    Chk "reset: old key now -> 401" 401 $rOld.Status
    $rNew = HttpSend "POST" $fireUri $null '{"orderNo":"O-5","amount":"1"}' @{ "X-Api-Key" = $newKey; "Idempotency-Key" = ("qa-" + [guid]::NewGuid().ToString("N")) }
    Chk "reset: new key -> 201" 201 $rNew.Status

    # =========================================================================
    # Scenario 5 tail : 404 does not leak existence (disabled == unknown, byte-identical)
    # =========================================================================
    Write-Host ""
    Write-Host "-- 404 indistinguishability (scenario 5 tail) --" -ForegroundColor Cyan
    $dis = PostJson "$BaseUrl/api/oa/flow-triggers/$msgId/enable" $s @{ enabled = $false }
    Chk "404: disable trigger HTTP 200" 200 $dis.Status
    $fDisabled = HttpSend "POST" $fireUri $null '{"orderNo":"O-6"}' @{ "X-Api-Key" = $newKey; "Idempotency-Key" = ("qa-" + [guid]::NewGuid().ToString("N")) }
    $fUnknown  = HttpSend "POST" "$BaseUrl/api/oa/flow-triggers/$UNKNOWN_GUID/fire" $null '{"orderNo":"O-6"}' @{ "X-Api-Key" = $newKey; "Idempotency-Key" = ("qa-" + [guid]::NewGuid().ToString("N")) }
    Chk "404: disabled trigger -> 404" 404 $fDisabled.Status
    Chk "404: unknown GUID     -> 404" 404 $fUnknown.Status
    Chk "404: disabled body == unknown body (no existence leak)" "$($fUnknown.Content)" "$($fDisabled.Content)"
} else { Warn "message scenarios skipped (create failed)" }

# =============================================================================
# Scenario 4 : event echo linkage + idempotent replay (no second instance)
# =============================================================================
Write-Host ""
Write-Host "-- event echo linkage (scenario 4) --" -ForegroundColor Cyan
$firesBefore = @(FiresList $s $T_EVENT).Count
$evId = "QA-EV-" + [guid]::NewGuid().ToString("N")
$echo1 = PostJson "$BaseUrl/api/oa/wf-trigger-echo/fire" $s @{ eventKey = "QA|OnEchoAsync"; eventId = $evId; payloadJson = '{"OutboundNo":"OB-1"}' }
Chk "event: echo fire HTTP 200" 200 $echo1.Status
if ($echo1.Json) {
    ChkTrue "event: matchedCount >= 1 (wtr-event matched)" ([int]$echo1.Json.data.matchedCount -ge 1)
    ChkTrue "event: firedCount   >= 1 (instance started)"   ([int]$echo1.Json.data.firedCount -ge 1)
}
$firesAfter1 = @(FiresList $s $T_EVENT).Count
ChkTrue "event: a new fire row was written" ($firesAfter1 -gt $firesBefore)

# resend SAME eventId -> idempotent: firedCount still counts it, but NO new fire row / instance
$echo2 = PostJson "$BaseUrl/api/oa/wf-trigger-echo/fire" $s @{ eventKey = "QA|OnEchoAsync"; eventId = $evId; payloadJson = '{"OutboundNo":"OB-1"}' }
Chk "event: resend same eventId HTTP 200" 200 $echo2.Status
if ($echo2.Json) { ChkTrue "event: resend firedCount still >= 1 (idempotent skip counts)" ([int]$echo2.Json.data.firedCount -ge 1) }
$firesAfter2 = @(FiresList $s $T_EVENT).Count
Chk "event: resend adds NO new fire row (idempotent {eventId}:{triggerId})" $firesAfter1 $firesAfter2
Write-Host "        (verify in DB: VarsJson of the started instance = {\"outboundNo\":\"OB-1\"} via varsMap)" -ForegroundColor DarkGray

# =============================================================================
# Scenario 8 : fire log shows a human failure (disabled starter -> E-WF-022)
# =============================================================================
Write-Host ""
Write-Host "-- fire log failure row (scenario 8) --" -ForegroundColor Cyan
$mfBad = PostJson "$BaseUrl/api/oa/flow-triggers/$T_BADSTARTER/manual-fire" $s @{}
Chk "log: manual-fire bad-starter -> 400" 400 $mfBad.Status
ChkContains "log: 400 body carries E-WF-022" "E-WF-022" "$($mfBad.Content)"
$badFires = @(FiresList $s $T_BADSTARTER)
ChkTrue "log: bad-starter fire log has >=1 row" ($badFires.Count -ge 1)
if ($badFires.Count -ge 1) {
    ChkTrue "log: top row has NO instanceId (failed)" ($null -eq $badFires[0].instanceId)
    ChkContains "log: top row Error carries E-WF-022" "E-WF-022" "$($badFires[0].error)"
}

# =============================================================================
# Scenario 3 : timer short-cycle self-fires (needs WfTriggerWorker running)
# =============================================================================
Write-Host ""
Write-Host "-- timer short-cycle auto-fire (scenario 3, needs worker; <= ${WaitSeconds}s) --" -ForegroundColor Cyan
$deadline = (Get-Date).AddSeconds($WaitSeconds)
$hit = $null
while ((Get-Date) -lt $deadline) {
    $sc = @(FiresList $s $T_SHORT)
    $hit = @($sc | Where-Object { $null -ne $_.instanceId }) | Select-Object -First 1
    if ($hit) { break }
    Start-Sleep -Seconds $PollSeconds
}
if ($hit) {
    ChkTrue "timer: short-cycle produced a successful fire (instanceId set)" ($null -ne $hit.instanceId)
    Write-Host "        (also verify NextDueUtc advanced + instance landed in the starter's inbox)" -ForegroundColor DarkGray
} else {
    Warn "timer short-cycle produced no fire within ${WaitSeconds}s -- is WfTriggerWorker running? (background hosted service)"
}

# ---- Summary -----------------------------------------------------------------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "RESULTS: PASS=$PASS   FAIL=$FAIL   WARN=$WARN" -ForegroundColor $(if ($FAIL -gt 0) { "Red" } elseif ($WARN -gt 0) { "Yellow" } else { "Green" })
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "SCENARIO 1 (admin page: build all three trigger types + one-time key modal) is MANUAL -- see README section 5."
Write-Host ""
Write-Host "MANUAL DB CHECKS (sqlcmd -S localhost\KOUSQLSERVER -d CP6DB_OA -E -C):"
Write-Host "  -- message whitelist: VarsJson keeps only orderNo/amount, drops 'secret':"
Write-Host "  SELECT i.VarsJson FROM Wf_FlowInstance i JOIN Wf_TriggerFire f ON f.InstanceId=i.Id"
Write-Host "  JOIN Wf_FlowTrigger t ON t.Id=f.TriggerId WHERE t.TriggerType=2 ORDER BY f.FiredUtc DESC;"
Write-Host "  -- event varsMap: VarsJson = {\"outboundNo\":\"OB-1\"}:"
Write-Host "  SELECT i.VarsJson FROM Wf_FlowInstance i JOIN Wf_TriggerFire f ON f.InstanceId=i.Id"
Write-Host "  WHERE f.TriggerId='$T_EVENT' ORDER BY f.FiredUtc DESC;"
Write-Host "  -- timer short-cycle: NextDueUtc advanced + repeated fires:"
Write-Host "  SELECT NextDueUtc, LastFiredUtc FROM Wf_FlowTrigger WHERE Id='$T_SHORT';"
Write-Host "  SELECT IdempotencyKey, InstanceId, Error FROM Wf_TriggerFire WHERE TriggerId='$T_SHORT' ORDER BY FiredUtc DESC;"
Write-Host ""
if ($FAIL -gt 0) { exit 1 } else { exit 0 }
