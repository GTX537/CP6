#Requires -Version 5.1
<#
.SYNOPSIS
    WFS 波④ Inbox UX (E-T2) -- HTTP e2e QA script (scenarios 1-4).

.DESCRIPTION
    Exercises the testable parts of the four wave-④ inbox UX features against a RUNNING
    backend. Does NOT start the server. Apply seed.sql first, then run the backend against
    the isolated CP6DB_OA database:

        sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql

        cd <repo>\CP6.WebApi
        $env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
        dotnet run --urls "http://localhost:5181"

    Then:
        .\qa_inbox_ux.ps1
        .\qa_inbox_ux.ps1 -BaseUrl http://localhost:5181

    *** STATUS: written, not run. *** Authored per task E-T2 (write-only). Live QA is
    executed later by the main agent with a QA user present. Nothing here has run.

.NOTES
    PS5.1 compatibility (mirrors wfs-flow-trigger/qa_flow_trigger.ps1):
      - No && operator; sequential steps use explicit checks.
      - HttpSend wraps Invoke-WebRequest -UseBasicParsing and captures the REAL status code
        (needed to tell 200 from 403 apart, and to read {code,message} error bodies).
      - All request bodies use ASCII-only values (no CJK) -- comments in payloads stay ASCII.
      - Dev backend MUST have CSRF disabled (Security:Csrf:Enabled=false, the dev default): the
        admin/settings/inbox POSTs are cookie-auth'd (cp6_at cookie flows via -WebSession) and
        would 403 on the CSRF double-submit check otherwise. In PRODUCTION posture these POSTs
        require cookie + X-Csrf-Token; the 403 negative test below is a PERMISSION 403, distinct
        from any CSRF 403.

    Scenario coverage (browser-only scenarios 5-6 live in README sections 5-6):
      1 notification matrix gating   : starter sets pref via POST /api/oa/pref/save {merge:true};
                                       reject a flow; assert starter's Wf_Notification Type=3 row
                                       appears (inApp on) / does NOT grow when inApp is also off.
                                       "no email" is a LogEmailSender log check (README / DB).
      2 (folded into 1)              : legacy flat pref compatibility -- README manual (DB direct-write).
      3 batch transfer + preview     : 30 pending -> 1 handled (dirty) -> preview total 29 + sample 10
                                       -> execute 29 ok / 0 fail -> explicit-taskId retry of the handled
                                       one -> total 1, 0 ok, 1 fail E-WF-002. Plus 403 for non-role user.
      4 rowMode merged/expanded      : parallel-3 instance -> merged=1 row / expanded=3 rows ->
                                       pref-driven default (rowMode omitted follows saved pref).

    Cross-checked endpoints (file:line in the E-T2 report):
      - Login        : POST /api/auth/login                       { userName, password } -> cp6_at cookie
      - Submit       : POST /api/wf/flow/submit                    { flowKey, varsJson } -> data.instanceId   [oa-form-catalog:submit]
      - Act/Reject   : POST /api/wf/task/{id}/act                  { approve, comment }                        [oa-inbox:approve]
      - Pending      : GET  /api/oa/inbox/pending?rowMode=&page=&pageSize=  -> data: InboxPendingItem[]        [Authorize]
      - SavePref     : POST /api/oa/pref/save                      { prefsJson, merge }                        [oa-settings:edit]
      - GetPref      : GET  /api/oa/pref/get                       -> data.prefsJson
      - NotifyMatrix : GET  /api/oa/pref/notify-matrix             -> data: NotifyMatrixRow[]                   [Authorize]
      - NotifyList   : GET  /api/oa/notification/list              -> data (list w/ Type)                       [Authorize]
      - BTPreview    : POST /api/oa/inbox/batch-transfer/preview   { fromUserId, toUserId, filter } -> {total, sample}  [oa-inbox:batch-transfer]
      - BTExecute    : POST /api/oa/inbox/batch-transfer           { fromUserId, toUserId, comment, filter } -> {total, succeeded, failed}  [oa-inbox:batch-transfer]

    Admin/OA envelope: { code: 0, message: "OK", data: <payload> }. Errors: { code:400|403, message }.
#>

param(
    [string]$BaseUrl = "http://localhost:5181",
    [int]   $N       = 30    # line flows submitted for the batch-transfer fixture
)

$ErrorActionPreference = "Continue"
$PASS = 0; $FAIL = 0; $WARN = 0

# Seed constants (MUST match seed.sql)
$U_FROM   = "CCCC0000-0000-0000-0000-0000000000C0"   # qa_bt_from  (line approver / transfer FROM)
$U_TO     = "CCCC0000-0000-0000-0000-0000000000D0"   # qa_bt_to    (transfer TO, enabled)
$FLOW_LINE = "qa-bt-line"
$FLOW_PAR  = "qa-bt-par3"

$NT_FLOW_REJECTED = 3   # WfNotificationType.FlowRejected (WfNotificationType.cs:16)

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
    return HttpSend "POST" $Uri $Session ($BodyObj | ConvertTo-Json -Depth 8 -Compress) $null }
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

# Submit a flow as $Session -> returns instanceId (or $null).
function Submit { param($Session, [string]$FlowKey)
    $r = PostJson "$BaseUrl/api/wf/flow/submit" $Session @{ flowKey = $FlowKey; varsJson = "{}" }
    if ($r.Status -eq 200 -and $r.Json) { return "$($r.Json.data.instanceId)" }
    return $null
}

# GET pending task ids for the logged-in user (expanded = per task).
function PendingTaskIds { param($Session, [string]$RowMode = "expanded")
    $r = GetJson "$BaseUrl/api/oa/inbox/pending?rowMode=$RowMode" $Session
    if ($r.Status -ne 200 -or $null -eq $r.Json) { return @() }
    return @($r.Json.data | ForEach-Object { "$($_.taskId)" })
}
function PendingCount { param($Session, [string]$RowMode)
    $q = if ($RowMode) { "?rowMode=$RowMode" } else { "" }
    $r = GetJson "$BaseUrl/api/oa/inbox/pending$q" $Session
    if ($r.Status -ne 200 -or $null -eq $r.Json) { return -1 }
    return @($r.Json.data).Count
}

# Save a partial pref via merge-write. $PartialJson is a RAW json string (server merges top-level keys).
function SaveMerge { param($Session, [string]$PartialJson)
    return PostJson "$BaseUrl/api/oa/pref/save" $Session @{ prefsJson = $PartialJson; merge = $true } }

# Count Type=N notifications for the logged-in user.
# NotificationController.List returns data = NotificationItem[] (bare array; camelCase `type`,
# NotificationService.cs:30-33 / NotificationModels.cs:7-16).
function NotifyCountOfType { param($Session, [int]$Type)
    $r = GetJson "$BaseUrl/api/oa/notification/list?pageSize=200" $Session
    if ($r.Status -ne 200 -or $null -eq $r.Json) { return -1 }
    return @(@($r.Json.data) | Where-Object { [int]$_.type -eq $Type }).Count
}

# ---- Preflight ---------------------------------------------------------------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "WFS 波④ Inbox UX QA  ($BaseUrl)" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

$sAdmin   = Login "qa_bt_admin"
$sStarter = Login "qa_bt_starter"
$sFrom    = Login "qa_bt_from"
$sPar     = Login "qa_bt_par"
if (-not $sAdmin -or -not $sStarter -or -not $sFrom -or -not $sPar) {
    Write-Host "ABORT: login failed (seed applied? backend up? CSRF disabled?)" -ForegroundColor Red; exit 1
}

# =============================================================================
# Scenario 1 : notification matrix gating (flowRejected x email OFF, then x inApp OFF)
# =============================================================================
Write-Host ""
Write-Host "-- notification matrix gating (scenario 1) --" -ForegroundColor Cyan

# 1a. matrix metadata: 5 rows incl. branchPruned; timeout row = both channels unsupported.
$mx = GetJson "$BaseUrl/api/oa/pref/notify-matrix" $sStarter
Chk "matrix: HTTP 200" 200 $mx.Status
$rows = @($mx.Json.data)
Chk "matrix: 5 type rows" 5 $rows.Count
$timeout = $rows | Where-Object { "$($_.typeKey)" -eq "timeout" } | Select-Object -First 1
ChkTrue "matrix: timeout row inApp UNsupported" ($timeout -and -not [bool]$timeout.inAppSupported)
ChkTrue "matrix: timeout row email UNsupported" ($timeout -and -not [bool]$timeout.emailSupported)
$pruned = $rows | Where-Object { "$($_.typeKey)" -eq "branchPruned" } | Select-Object -First 1
ChkTrue "matrix: branchPruned row present (reflection axis 5th row)" ($null -ne $pruned)

# 1b. starter closes flowRejected x email (inApp stays on). merge-write, top-level 'notify' key.
$save1 = SaveMerge $sStarter '{"notify":{"flowRejected":{"email":false}}}'
Chk "pref: save flowRejected.email=false HTTP 200" 200 $save1.Status
$getPref = GetJson "$BaseUrl/api/oa/pref/get" $sStarter
ChkContains "pref: persisted json carries flowRejected" "flowRejected" "$($getPref.Json.data.prefsJson)"

$before = NotifyCountOfType $sStarter $NT_FLOW_REJECTED

# Submit a line flow as starter -> task lands on qa_bt_from; from rejects -> starter gets FlowRejected.
$inst1 = Submit $sStarter $FLOW_LINE
ChkTrue "reject-flow: submit created an instance" (-not [string]::IsNullOrWhiteSpace($inst1))
$fromTasks = PendingTaskIds $sFrom "expanded"
$rejTask = $fromTasks | Select-Object -First 1
if ($rejTask) {
    $rej = PostJson "$BaseUrl/api/wf/task/$rejTask/act" $sFrom @{ approve = $false; comment = "qa reject" }
    Chk "reject-flow: from rejects -> HTTP 200" 200 $rej.Status
    Start-Sleep -Milliseconds 300
    $after1 = NotifyCountOfType $sStarter $NT_FLOW_REJECTED
    ChkTrue "notify: starter gained a Type=3 (FlowRejected) row (inApp on)" ($after1 -gt $before)
    Write-Host "        (email suppressed: verify NO '[DEV-EMAIL->qa_bt_starter@example.com]' line in the backend log -- README 4.1)" -ForegroundColor DarkGray

    # 1c. now close flowRejected x inApp too -> next reject creates NO new Type=3 row.
    $save2 = SaveMerge $sStarter '{"notify":{"flowRejected":{"inApp":false,"email":false}}}'
    Chk "pref: save flowRejected inApp+email=false HTTP 200" 200 $save2.Status
    $inst2 = Submit $sStarter $FLOW_LINE
    $fromTasks2 = PendingTaskIds $sFrom "expanded"
    $rejTask2 = $fromTasks2 | Select-Object -First 1
    if ($rejTask2) {
        $rej2 = PostJson "$BaseUrl/api/wf/task/$rejTask2/act" $sFrom @{ approve = $false; comment = "qa reject 2" }
        Chk "reject-flow-2: from rejects -> HTTP 200" 200 $rej2.Status
        Start-Sleep -Milliseconds 300
        $after2 = NotifyCountOfType $sStarter $NT_FLOW_REJECTED
        Chk "notify: inApp off -> NO new Type=3 row" $after1 $after2
    } else { Warn "scenario 1c skipped (no pending task for from on 2nd submit)" }

    # restore defaults (remove flowRejected key -> all channels back on)
    $reset = SaveMerge $sStarter '{"notify":{"flowRejected":null}}'
    Chk "pref: reset flowRejected to default HTTP 200" 200 $reset.Status
} else { Warn "scenario 1 reject skipped (from has no pending task after submit)" }

# =============================================================================
# Scenario 3 : batch transfer + preview + partial-fail + 403
# =============================================================================
Write-Host ""
Write-Host "-- batch transfer + preview (scenario 3) --" -ForegroundColor Cyan

# 3a. submit N line flows -> N pending tasks on qa_bt_from.
$submitted = 0
for ($i = 0; $i -lt $N; $i++) { if (Submit $sStarter $FLOW_LINE) { $submitted++ } }
Chk "bt: submitted $N line flows" $N $submitted

$fromIds = PendingTaskIds $sFrom "expanded"
ChkTrue "bt: from now has >= $N pending tasks" ($fromIds.Count -ge $N)

# 3b. make ONE dirty: from HANDLES (approves) it -> that instance completes, task no longer Pending.
$dirtyTask = $fromIds | Select-Object -First 1
$doneAct = PostJson "$BaseUrl/api/wf/task/$dirtyTask/act" $sFrom @{ approve = $true; comment = "qa handled" }
Chk "bt: from handles one (dirty) -> HTTP 200" 200 $doneAct.Status
$expectCandidates = $N - 1   # the handled one drops out of the Pending candidate set

# 3c. preview as admin: total = N-1, sample = 10.
$prev = PostJson "$BaseUrl/api/oa/inbox/batch-transfer/preview" $sAdmin @{ fromUserId = $U_FROM; toUserId = $U_TO; filter = $null }
Chk "bt-preview: HTTP 200" 200 $prev.Status
Chk "bt-preview: total = N-1 (handled excluded, Pending-only candidates)" $expectCandidates ([int]$prev.Json.data.total)
Chk "bt-preview: sample capped at 10" 10 (@($prev.Json.data.sample).Count)

# 3d. execute as admin: total N-1, succeeded N-1, failed 0.
$exec = PostJson "$BaseUrl/api/oa/inbox/batch-transfer" $sAdmin @{ fromUserId = $U_FROM; toUserId = $U_TO; comment = "offboarding"; filter = $null }
Chk "bt-execute: HTTP 200" 200 $exec.Status
Chk "bt-execute: total = N-1" $expectCandidates ([int]$exec.Json.data.total)
Chk "bt-execute: succeeded = N-1" $expectCandidates ([int]$exec.Json.data.succeeded)
Chk "bt-execute: failed = 0" 0 (@($exec.Json.data.failed).Count)
Write-Host "        (verify per-task engine audit: $expectCandidates Wf_FlowHistory action=transfer ActorId=admin + FormTo Transferred/Pending pairs -- README 4.3)" -ForegroundColor DarkGray

# 3e. explicit-taskId RETRY of the already-handled task (single-retry 口径): total 1, 0 ok, 1 fail E-WF-002.
$retry = PostJson "$BaseUrl/api/oa/inbox/batch-transfer" $sAdmin @{
    fromUserId = $U_FROM; toUserId = $U_TO; comment = "retry"; filter = @{ taskIds = @($dirtyTask) } }
Chk "bt-retry: HTTP 200" 200 $retry.Status
Chk "bt-retry: total = 1 (named, not pre-filtered by status)" 1 ([int]$retry.Json.data.total)
Chk "bt-retry: succeeded = 0" 0 ([int]$retry.Json.data.succeeded)
$failed = @($retry.Json.data.failed)
Chk "bt-retry: 1 failure detail row" 1 $failed.Count
if ($failed.Count -ge 1) { ChkContains "bt-retry: failure carries E-WF-002 (engine verdict)" "E-WF-002" "$($failed[0].error)" }

# 3f. 403: a non-role user (RoleId=2) may NOT call batch-transfer.
$sNoRole = Login "qa_bt_norole"
if ($sNoRole) {
    $forbid = PostJson "$BaseUrl/api/oa/inbox/batch-transfer" $sNoRole @{ fromUserId = $U_FROM; toUserId = $U_TO; filter = $null }
    Chk "bt-403: non-role user -> 403" 403 $forbid.Status
    ChkContains "bt-403: message = no-permission oa-inbox:batch-transfer" "oa-inbox:batch-transfer" "$($forbid.Content)"
    # preview shares the same permission point (C4/C8)
    $forbidPrev = PostJson "$BaseUrl/api/oa/inbox/batch-transfer/preview" $sNoRole @{ fromUserId = $U_FROM; toUserId = $U_TO; filter = $null }
    Chk "bt-403: preview also 403 (same permission point)" 403 $forbidPrev.Status
} else { Warn "403 negative test skipped (qa_bt_norole login failed)" }

# =============================================================================
# Scenario 4 : rowMode merged / expanded + pref-driven default
# =============================================================================
Write-Host ""
Write-Host "-- rowMode merged/expanded (scenario 4) --" -ForegroundColor Cyan

# 4a. one parallel-3 submit -> 3 pending tasks for qa_bt_par in ONE instance.
$instP = Submit $sStarter $FLOW_PAR
ChkTrue "rowMode: parallel-3 submit created an instance" (-not [string]::IsNullOrWhiteSpace($instP))
Start-Sleep -Milliseconds 300

$merged   = PendingCount $sPar "merged"
$expanded = PendingCount $sPar "expanded"
Chk "rowMode: explicit merged -> 1 row (grouped by instance)" 1 $merged
Chk "rowMode: explicit expanded -> 3 rows (per task)" 3 $expanded

# 4b. pref-driven: set rowMode=expanded, omit query param -> follows saved pref (expanded = 3).
$saveExp = SaveMerge $sPar '{"rowMode":"expanded"}'
Chk "rowMode: save pref rowMode=expanded HTTP 200" 200 $saveExp.Status
$defExpanded = PendingCount $sPar $null
Chk "rowMode: omitted param follows pref (expanded=3)" 3 $defExpanded

# 4c. flip pref back to merged (remove key -> default merged) -> omitted param = merged = 1.
$saveMer = SaveMerge $sPar '{"rowMode":null}'
Chk "rowMode: reset pref (remove rowMode) HTTP 200" 200 $saveMer.Status
$defMerged = PendingCount $sPar $null
Chk "rowMode: omitted param now default merged=1" 1 $defMerged

# ---- Summary -----------------------------------------------------------------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "RESULTS: PASS=$PASS   FAIL=$FAIL   WARN=$WARN" -ForegroundColor $(if ($FAIL -gt 0) { "Red" } elseif ($WARN -gt 0) { "Yellow" } else { "Green" })
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "SCENARIO 2 (legacy flat-pref compatibility) is a DB-direct-write manual check -- see README section 4.2."
Write-Host "SCENARIOS 5-6 (mobile 375px / desktop 1280px walkthroughs) are MANUAL (gstack browse) -- see README sections 5-6."
Write-Host ""
Write-Host "MANUAL CHECKS (sqlcmd -S localhost\KOUSQLSERVER -d CP6DB_OA -E -C):"
Write-Host "  -- scenario 1 no-email: the backend console must have NO [DEV-EMAIL->qa_bt_starter@example.com] line for the reject."
Write-Host "  -- scenario 1 notif rows for the starter (Type 3 = FlowRejected):"
Write-Host "  SELECT Type, COUNT(*) FROM Wf_Notification n JOIN Sys_Users u ON u.Id=n.UserId"
Write-Host "  WHERE u.UserName='qa_bt_starter' GROUP BY Type;"
Write-Host "  -- scenario 3 per-task engine audit (transfer history + FormTo pairs, ActorId=qa_bt_admin):"
Write-Host "  SELECT Action, ActorId, COUNT(*) FROM Wf_FlowHistory WHERE Action='transfer' GROUP BY Action, ActorId;"
Write-Host ""
if ($FAIL -gt 0) { exit 1 } else { exit 0 }
