#Requires -Version 5.1
<#
.SYNOPSIS
    WFS 波5 Engine Infra six-pack (F-T2) -- HTTP e2e QA script (scenarios 2,3,6,7,8).

.DESCRIPTION
    Exercises the HTTP-testable parts of the six infra features against a RUNNING backend.
    Does NOT start the server. Apply seed.sql first, then run the backend against the isolated
    CP6DB_OA database:

        sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql

        cd <repo>\CP6.WebApi
        $env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
        dotnet run --urls "http://localhost:5181"

    Then:
        .\qa_infra.ps1
        .\qa_infra.ps1 -BaseUrl http://localhost:5181

    *** STATUS: written, not run. *** Authored per task F-T2 (write-only). Live QA is executed
    later by the main agent with a QA user present. NOTHING here has run.

.NOTES
    PS5.1 compatibility (mirrors wfs-inbox-ux/qa_inbox_ux.ps1 + wfs-flow-trigger/qa_flow_trigger.ps1):
      - No && operator; sequential steps use explicit checks.
      - HttpSend wraps Invoke-WebRequest -UseBasicParsing and captures the REAL status code
        (needed to tell 200 from 400 / 403, and to read {code,message,detail} error bodies).
      - ASCII-only banners and request bodies (no CJK) -- wave-4 Minor lesson.
      - Dev backend MUST have CSRF disabled (Security:Csrf:Enabled=false, the dev default): the
        admin/designer/tenant POSTs are cookie-auth'd (cp6_at cookie flows via -WebSession) and
        would 403 on the CSRF double-submit otherwise. In PRODUCTION posture those POSTs require
        cookie + X-Csrf-Token; the scenario-6 403 is a PERMISSION 403, distinct from any CSRF 403.

    Scenario coverage (browser-only 1/4/5 live in the README; DB/worker drills in README sec 4):
      2 calendar empty-state import : POST /api/oa/work-calendar/import-jp -> inserted=35 (first run),
                                      GET .../?year=2026 -> isEmpty=false + items carry holiday rows.
      3 approval timeout errorEdge  : POST /api/oa/designer/save with a timeoutAction="errorEdge"
                                      node that has NO IsError edge -> 400 E-WF-027; add the IsError
                                      edge -> 200. (Runtime void+route drill = README 4.3.)
      6 connector full flow         : create (auth) -> list HasAuth masked -> get authJson null ->
                                      TimeoutSec>=lease(300) -> 400 E-WF-028 (message="E-WF-028",
                                      detail carries the diagnostic) -> non-role user -> 403.
      7 per-node HTTP override      : POST designer save, serviceTask node ServiceTimeoutSec=300
                                      (>=lease) -> 400 E-WF-028; ServiceHttpMethod="PATCH" (out of
                                      {GET,POST,PUT,DELETE}) -> 400 E-WF-028; PUT + 5s -> 200.
      8 tenant time zone            : PUT /api/platform/tenant/{A1} TimeZoneId="Not/AZone" -> 400
                                      E-WF-028; "Asia/Tokyo" -> 200; restored to null afterward.

    Cross-checked endpoints (file:line in the F-T2 report):
      - Login         : POST /api/auth/login                         { userName, password } -> cp6_at cookie
      - CalImport     : POST /api/oa/work-calendar/import-jp          -> data.inserted             [oa-work-calendar:Calendar.Edit]
      - CalList       : GET  /api/oa/work-calendar?year=YYYY          -> data.{year,isEmpty,items} [Authorize]
      - DesignerSave  : POST /api/oa/designer/save                    { flowKey,flowName,formKey,schemaJson } [oa-designer:edit]
      - ConnList      : GET  /api/oa/wf-connector                     -> data: WfConnectorView[]   [Authorize]
      - ConnGet       : GET  /api/oa/wf-connector/{id}                -> data: WfConnectorView     [Authorize]
      - ConnCreate    : POST /api/oa/wf-connector                     { name,displayName,baseUrl,authJson,timeoutSec,enabled } -> {id} [oa-flow-admin:Connector.Edit]
      - TenantUpdate  : PUT  /api/platform/tenant/{id}                { name,expire,remark,timeZoneId }  [RequirePlatformAdmin]
      - TenantGet     : GET  /api/platform/tenant/{id}                -> TenantDetail (timeZoneId)  [RequirePlatformAdmin]

    OA/admin envelope: { code:0, message:"OK", data:<payload> }. OA errors: { code:400|403|404, message, detail? }.
    Platform tenant: List/Get bare objects; Update -> { code:0 }; errors -> BizException 400 { code, message }.
#>

param(
    [string]$BaseUrl  = "http://localhost:5181",
    [string]$TenantId = "00000000-0000-0000-0000-0000000000A1"   # DefaultTenant A1 (matches seed.sql)
)

$ErrorActionPreference = "Continue"
$PASS = 0; $FAIL = 0; $WARN = 0

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
    return HttpSend "POST" $Uri $Session ($BodyObj | ConvertTo-Json -Depth 10 -Compress) $null }
function PutJson  { param([string]$Uri, $Session, $BodyObj)
    return HttpSend "PUT"  $Uri $Session ($BodyObj | ConvertTo-Json -Depth 10 -Compress) $null }
function GetJson  { param([string]$Uri, $Session)
    return HttpSend "GET"  $Uri $Session $null $null }

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

# POST a designer schema (returns the full response so callers can inspect status + error code).
function DesignerSave { param($Session, [string]$FlowKey, [string]$SchemaJson)
    return PostJson "$BaseUrl/api/oa/designer/save" $Session `
        @{ flowKey = $FlowKey; flowName = "F-T2 probe $FlowKey"; formKey = "qa-inf-form"; schemaJson = $SchemaJson } }

# ---- Preflight ---------------------------------------------------------------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "WFS wave5 Engine Infra QA  ($BaseUrl)" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

$sAdmin  = Login "qa_inf_admin"
$sPAdmin = Login "qa_inf_padmin"
if (-not $sAdmin) {
    Write-Host "ABORT: qa_inf_admin login failed (seed applied? backend up? CSRF disabled?)" -ForegroundColor Red; exit 1
}

# =============================================================================
# Scenario 2 : work calendar empty-state import (JP holidays)
# =============================================================================
Write-Host ""
Write-Host "-- work calendar empty-state import (scenario 2) --" -ForegroundColor Cyan
Write-Host "        (NOTE: A1 is pre-seeded 35 JP holidays on boot; to SEE the empty state, clear first --" -ForegroundColor DarkGray
Write-Host "         sqlcmd ... -Q \"DELETE FROM Sys_WorkCalendar WHERE TenantId='$TenantId'\" -- then re-run this.)" -ForegroundColor DarkGray

$imp = PostJson "$BaseUrl/api/oa/work-calendar/import-jp" $sAdmin @{}
Chk "cal-import: HTTP 200" 200 $imp.Status
$inserted = [int]$imp.Json.data.inserted
ChkTrue "cal-import: inserted 35 (fresh) OR 0 (idempotent, already seeded)" ($inserted -eq 35 -or $inserted -eq 0)
Write-Host "        (inserted=$inserted -- 35 when the calendar was empty, 0 when A1 already held the boot seed)" -ForegroundColor DarkGray

$imp2 = PostJson "$BaseUrl/api/oa/work-calendar/import-jp" $sAdmin @{}
Chk "cal-import: second call idempotent -> inserted 0" 0 ([int]$imp2.Json.data.inserted)

$cal = GetJson "$BaseUrl/api/oa/work-calendar?year=2026" $sAdmin
Chk "cal-list: HTTP 200" 200 $cal.Status
Chk "cal-list: year echoed" 2026 ([int]$cal.Json.data.year)
ChkTrue "cal-list: isEmpty=false after import" (-not [bool]$cal.Json.data.isEmpty)
$items = @($cal.Json.data.items)
ChkTrue "cal-list: 2026 carries >=17 holiday rows (18 seeded, 2026-01-01 element-jitsu etc.)" ($items.Count -ge 17)

# =============================================================================
# Scenario 3 : approval timeout errorEdge save-validation (E-WF-027)
# =============================================================================
Write-Host ""
Write-Host "-- approval timeout errorEdge validation (scenario 3, E-WF-027) --" -ForegroundColor Cyan

# 3a. errorEdge node WITHOUT an IsError edge -> E-WF-027 (approval is a legal source; the fault is
#     the missing failure edge -> nowhere to route -> designer-time block).
$badSchema = '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"Approve","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000B0","timeoutHours":1,"timeoutAction":"errorEdge"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"}]}'
$bad = DesignerSave $sAdmin "qa-inf-e027-neg" $badSchema
Chk "e027-neg: errorEdge without IsError edge -> HTTP 400" 400 $bad.Status
ChkContains "e027-neg: message carries E-WF-027" "E-WF-027" "$($bad.Content)"

# 3b. same node but WITH an IsError edge to a handler -> valid -> 200.
$okSchema = '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"Approve","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000B0","timeoutHours":1,"timeoutAction":"errorEdge"},{"id":"handler","type":"approval","name":"Handler","approverStrategy":"Specified","approverUserId":"EEEE0000-0000-0000-0000-0000000000B0"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"},{"from":"a1","to":"handler","isError":true},{"from":"handler","to":"end"}]}'
$ok = DesignerSave $sAdmin "qa-inf-e027-pos" $okSchema
Chk "e027-pos: errorEdge WITH IsError edge -> HTTP 200" 200 $ok.Status
Write-Host "        (runtime void+route drill -- submit qa-inf-erroredge, roll DueAt back, let the 1-min scan fire -- is README 4.3)" -ForegroundColor DarkGray

# =============================================================================
# Scenario 6 : per-tenant connector full flow (create / mask / E-WF-028 / 403)
# =============================================================================
Write-Host ""
Write-Host "-- per-tenant connector full flow (scenario 6) --" -ForegroundColor Cyan

# 6a. create a connector WITH auth (TimeoutSec=30 < lease 300) -> 200 {id}.
$create = PostJson "$BaseUrl/api/oa/wf-connector" $sAdmin @{
    name = "qaInfConn"; displayName = "QA Infra Connector"; baseUrl = "https://example.test/api";
    authJson = '{"type":"bearer","token":"qa-tok-123"}'; timeoutSec = 30; enabled = $true }
# Re-run tolerance: a duplicate name currently 500s (known ticket N1: dup name vs
# UX_Wf_Connector_Name unique index -> DbUpdateException 500, friendly 400/409 pending).
if ($create.Status -eq 500) {
    $pre = GetJson "$BaseUrl/api/oa/wf-connector" $sAdmin
    $prow = @($pre.Json.data) | Where-Object { "$($_.name)" -eq "qaInfConn" } | Select-Object -First 1
    if ($prow) {
        Warn "conn-create 500 = duplicate from a prior run (known ticket N1: dup -> 500 not 400); reusing existing row"
        $create = @{ Status = 200; Json = @{ data = @{ id = "$($prow.id)" } } }
    }
}
Chk "conn-create: HTTP 200" 200 $create.Status
$connId = "$($create.Json.data.id)"
ChkTrue "conn-create: returned an id" (-not [string]::IsNullOrWhiteSpace($connId))

# 6b. list -> the row shows HasAuth=true and NO plaintext (authJson is always null in the view).
$list = GetJson "$BaseUrl/api/oa/wf-connector" $sAdmin
Chk "conn-list: HTTP 200" 200 $list.Status
$mine = @($list.Json.data) | Where-Object { "$($_.name)" -eq "qaInfConn" } | Select-Object -First 1
ChkTrue "conn-list: qaInfConn present" ($null -ne $mine)
ChkTrue "conn-list: hasAuth=true (credential set)" ($mine -and [bool]$mine.hasAuth)
ChkTrue "conn-list: authJson NOT echoed (mask contract)" ($mine -and [string]::IsNullOrEmpty("$($mine.authJson)"))
ChkTrue "conn-list: raw body carries no plaintext token" (-not "$($list.Content)".Contains("qa-tok-123"))

# 6c. get single -> masked too.
if ($connId) {
    $get = GetJson "$BaseUrl/api/oa/wf-connector/$connId" $sAdmin
    Chk "conn-get: HTTP 200" 200 $get.Status
    ChkTrue "conn-get: hasAuth=true, authJson null" ([bool]$get.Json.data.hasAuth -and [string]::IsNullOrEmpty("$($get.Json.data.authJson)"))
}

# 6d. E-WF-028: TimeoutSec >= lease (300) is REJECTED (single call must be strictly shorter than the
#     job lease, else the reaper would re-dispatch -> duplicate outbound call). The controller Err()
#     splits "E-WF-028|timeoutGteLease:..." on '|' so message="E-WF-028" (t()-resolvable) + detail carries the diagnostic.
$e028 = PostJson "$BaseUrl/api/oa/wf-connector" $sAdmin @{
    name = "qaInfConnBad"; displayName = "QA Infra Bad"; baseUrl = "https://example.test/api";
    authJson = $null; timeoutSec = 300; enabled = $true }
Chk "conn-e028: TimeoutSec>=lease -> HTTP 400" 400 $e028.Status
Chk "conn-e028: message is the bare code E-WF-028 (t()-resolvable)" "E-WF-028" "$($e028.Json.message)"
ChkContains "conn-e028: detail carries the diagnostic suffix" "timeoutGteLease" "$($e028.Json.detail)"

# 6e. 403: a non-role user (RoleId=2) may NOT create a connector (Connector.Edit not granted).
$sNoRole = Login "qa_inf_norole"
if ($sNoRole) {
    $forbid = PostJson "$BaseUrl/api/oa/wf-connector" $sNoRole @{
        name = "qaInfConnX"; displayName = "x"; baseUrl = "https://example.test"; timeoutSec = 30; enabled = $true }
    Chk "conn-403: non-role user -> 403" 403 $forbid.Status
    ChkContains "conn-403: message names oa-flow-admin:Connector.Edit" "Connector.Edit" "$($forbid.Content)"
} else { Warn "403 negative test skipped (qa_inf_norole login failed)" }

Write-Host "        (tenant-preferred-over-app EchoConnector 'erpEcho' + real decrypt-and-call is a DB/execution check -- README 6.2)" -ForegroundColor DarkGray

# =============================================================================
# Scenario 7 : per-node HTTP method/timeout override save-validation (E-WF-028)
# =============================================================================
Write-Host ""
Write-Host "-- per-node HTTP override validation (scenario 7, E-WF-028) --" -ForegroundColor Cyan

# A minimal serviceTask webApi node. serviceTimeoutSec >= lease(300) is rejected save-side; an
# out-of-domain method (PATCH) is rejected by the static validator. A PUT + 5s override saves.
# NOTE connector name = "erpEcho" (the DI-registered app-level EchoConnector), NOT the tenant
# connector created in scenario 6: DesignerService.SaveAsync's E-WF-018 registered-name check
# (step 1b, DesignerService.cs:51-64, runs BEFORE the 1c lease check) only sees DI-registered
# IWfConnector names -- tenant Wf_Connector rows resolve at RUNTIME via TenantConnectorResolver,
# invisible to the save-side check. "qaInfConn" here would 400 with E-WF-018, not E-WF-028.
function SvcSchema { param([string]$Method, $TimeoutSec)
    $node = '{"id":"t","type":"serviceTask","name":"Call","serviceKind":"webApi","serviceMode":"async","serviceConnectorName":"erpEcho","servicePath":"/ping"'
    if ($Method)     { $node += ',"serviceHttpMethod":"' + $Method + '"' }
    if ($null -ne $TimeoutSec) { $node += ',"serviceTimeoutSec":' + $TimeoutSec }
    $node += '}'
    return '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},' + $node + ',{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"t"},{"from":"t","to":"end"}]}'
}

# 7a. serviceTimeoutSec = 300 (>= lease) -> save-side E-WF-028.
$n028a = DesignerSave $sAdmin "qa-inf-node028a" (SvcSchema $null 300)
Chk "node-e028a: serviceTimeoutSec>=lease -> HTTP 400" 400 $n028a.Status
ChkContains "node-e028a: message carries E-WF-028" "E-WF-028" "$($n028a.Content)"

# 7b. serviceHttpMethod = PATCH (out of {GET,POST,PUT,DELETE}) -> static E-WF-028.
$n028b = DesignerSave $sAdmin "qa-inf-node028b" (SvcSchema "PATCH" 5)
Chk "node-e028b: method out of domain -> HTTP 400" 400 $n028b.Status
ChkContains "node-e028b: message carries E-WF-028" "E-WF-028" "$($n028b.Content)"

# 7c. PUT + 5s (both in range, timeout < lease) -> valid.
$nOk = DesignerSave $sAdmin "qa-inf-node-ok" (SvcSchema "PUT" 5)
Chk "node-ok: PUT + 5s override -> HTTP 200" 200 $nOk.Status
Write-Host "        (that the node value actually reaches the wire is a real-outbound check -- README 6.3)" -ForegroundColor DarkGray

# =============================================================================
# Scenario 8 : per-tenant time zone save-validation (E-WF-028)
# =============================================================================
Write-Host ""
Write-Host "-- per-tenant time zone validation (scenario 8, E-WF-028) --" -ForegroundColor Cyan

if (-not $sPAdmin) {
    Warn "scenario 8 skipped (qa_inf_padmin login failed -- platform-admin required for /api/platform/tenant)"
} else {
    # Read current tenant detail (to restore name/remark on the round-trip -- Update takes the full record).
    $td = GetJson "$BaseUrl/api/platform/tenant/$TenantId" $sPAdmin
    Chk "tenant-get: HTTP 200" 200 $td.Status
    $tName   = "$($td.Json.name)";   if ([string]::IsNullOrEmpty($tName)) { $tName = "DefaultTenant" }
    $tRemark = "$($td.Json.remark)"

    # 8a. unparseable TimeZoneId -> save-side E-WF-028 (nothing persisted).
    $bad = PutJson "$BaseUrl/api/platform/tenant/$TenantId" $sPAdmin @{
        name = $tName; expire = $null; remark = $tRemark; timeZoneId = "Not/AZone" }
    Chk "tz-e028: unparseable TimeZoneId -> HTTP 400" 400 $bad.Status
    # Platform-domain errors are server-localized (BizException resolves E-WF-028 before response), unlike OA bare codes; accept either.
    $tzMsgOk = ("$($bad.Content)".Contains("E-WF-028")) -or ("$($bad.Content)".Contains("u30BF")) -or ("$($bad.Content)" -match "(?i)timezone")
    ChkTrue "tz-e028: message carries E-WF-028 (bare code or server-localized text)" $tzMsgOk

    # 8b. Asia/Tokyo -> accepted (200); timer untilDate/workdays now interpreted in Tokyo local.
    $tokyo = PutJson "$BaseUrl/api/platform/tenant/$TenantId" $sPAdmin @{
        name = $tName; expire = $null; remark = $tRemark; timeZoneId = "Asia/Tokyo" }
    Chk "tz-set: Asia/Tokyo -> HTTP 200" 200 $tokyo.Status
    $verify = GetJson "$BaseUrl/api/platform/tenant/$TenantId" $sPAdmin
    ChkContains "tz-set: detail reflects Asia/Tokyo" "Asia/Tokyo" "$($verify.Content)"

    # 8c. restore to null (empty -> cleared -> app default) so the QA DB is left neutral.
    $restore = PutJson "$BaseUrl/api/platform/tenant/$TenantId" $sPAdmin @{
        name = $tName; expire = $null; remark = $tRemark; timeZoneId = "" }
    Chk "tz-restore: clear TimeZoneId -> HTTP 200" 200 $restore.Status
    Write-Host "        (the 'change tz does NOT batch-recompute NextDueUtc; self-heals next fire' semantics = README 6.4)" -ForegroundColor DarkGray
}

# ---- Summary -----------------------------------------------------------------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "RESULTS: PASS=$PASS   FAIL=$FAIL   WARN=$WARN" -ForegroundColor $(if ($FAIL -gt 0) { "Red" } elseif ($WARN -gt 0) { "Yellow" } else { "Green" })
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "SCENARIO 1 (workdays=3 timer DueAt), 4 (three existing timeout actions no-regression), 5 (cleanup worker)"
Write-Host "  are BROWSER + DB/worker drills -- see README sections 4 and 5."
Write-Host ""
Write-Host "MANUAL CHECKS (sqlcmd -S localhost\KOUSQLSERVER -d CP6DB_OA -E -C):"
Write-Host "  -- scenario 3 runtime: after rolling qa-inf-erroredge's a1 task DueAt back and letting the scan fire,"
Write-Host "     the a1 task is Cancelled, a new Pending task exists at 'handler', instance stays Running, VarsJson has timeoutError:"
Write-Host "  SELECT Status, NodeId FROM Wf_FlowTask WHERE InstanceId = '<inst>';"
Write-Host "  SELECT VarsJson FROM Wf_FlowInstance WHERE Id = '<inst>';"
Write-Host "  -- scenario 5 cleanup: terminal ServiceJob/TriggerFire older than retention deleted; running + reservation kept:"
Write-Host "  SELECT Status, COUNT(*) FROM Wf_ServiceJob GROUP BY Status;"
Write-Host ""
if ($FAIL -gt 0) { exit 1 } else { exit 0 }
