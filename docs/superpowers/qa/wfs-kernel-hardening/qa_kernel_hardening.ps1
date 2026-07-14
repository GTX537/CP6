#Requires -Version 5.1
<#
.SYNOPSIS
    WFS Kernel Hardening (E-T2) -- HTTP e2e QA script (scenarios 1-6).

.DESCRIPTION
    Exercises the inclusive-gateway / branch-reject / send-back hardening against a
    RUNNING backend. Does NOT start the server. Apply seed.sql first, then run the
    backend against the isolated CP6DB_OA database:

        sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql

        cd <repo>\CP6.WebApi
        $env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
        dotnet run --urls "http://localhost:5181"

    Then:
        .\qa_kernel_hardening.ps1
        .\qa_kernel_hardening.ps1 -BaseUrl http://localhost:5181

    Every submit/approve here is SYNCHRONOUS (no background worker involved), so the
    run is fast. Scenario 7 (designer drag/drop + client validation i18n) is a manual
    real-browser step -- see README section 5.

    *** STATUS: written, not run. *** Authored per task E-T2 (write-only). Live QA is
    executed later by the main agent with a QA user present. Nothing here has run.

.NOTES
    PS5.1 compatibility (mirrors wfs-service-task/qa_service_task.ps1):
      - No && operator; sequential steps use explicit checks.
      - Invoke-RestMethod throws WebException on non-2xx; body read via Read400Body.
      - All request bodies use ASCII-only values (no CJK).
      - CSRF disabled in dev (Security:Csrf:Enabled=false); JWT cp6_at cookie flows
        automatically via -SessionVariable / -WebSession.

    Endpoints:
      - Login    : POST /api/auth/login              { userName, password } -> cp6_at cookie
      - Submit   : POST /api/wf/flow/submit          { flowKey, varsJson } -> data.instanceId
      - Act      : POST /api/wf/task/{taskId}/act     { approve, comment }
      - SendBack : POST /api/wf/advanced/sendback     { taskId, targetNodeId, comment }
      - Pending  : GET  /api/oa/inbox/pending         -> data: InboxPendingItem[]  (.taskId .instanceId .nodeId)
      - Detail   : GET  /api/oa/inbox/detail/{id}     -> data: InboxDetail (.instance.status)
      - Notif    : GET  /api/oa/notification/list     -> data: NotificationItem[]  (.type .instanceId)

    Envelope: { code: 0, message: "OK", data: <payload> }
    Instance status (FlowInstanceStatus): 0 Running  1 Approved  2 Rejected  3 Withdrawn  4 Suspended  5 Draft
    Notification type (WfNotificationType): 5 = BranchPruned
#>

param(
    [string]$BaseUrl     = "http://localhost:5181",
    [int]   $WaitSeconds = 20,
    [int]   $PollSeconds = 2
)

$ErrorActionPreference = "Continue"
$PASS = 0; $FAIL = 0; $WARN = 0

# ── FlowKeys from seed.sql ──────────────────────────────────────────────────
$FK_INCL = "khd-inclusive"
$FK_PRUNE = "khd-prune"
$FK_CASC  = "khd-cascade"
$FK_SB    = "khd-sameback"

# Status codes
$ST_RUNNING  = 0
$ST_APPROVED = 1
$ST_REJECTED = 2
$NT_BRANCH_PRUNED = 5

# ── Helpers ─────────────────────────────────────────────────────────────────

function Chk {
    param([string]$Label, $Expected, $Actual)
    if ("$Expected" -eq "$Actual") {
        Write-Host "  PASS  $Label (=$Actual)" -ForegroundColor Green
        $script:PASS++
    } else {
        Write-Host "  FAIL  $Label  expected=[$Expected]  got=[$Actual]" -ForegroundColor Red
        $script:FAIL++
    }
}

function ChkTrue {
    param([string]$Label, $Cond)
    if ($Cond) {
        Write-Host "  PASS  $Label" -ForegroundColor Green
        $script:PASS++
    } else {
        Write-Host "  FAIL  $Label" -ForegroundColor Red
        $script:FAIL++
    }
}

function ChkContains {
    param([string]$Label, [string]$Needle, [string]$Haystack)
    if ("$Haystack".Contains($Needle)) {
        Write-Host "  PASS  $Label (found '$Needle')" -ForegroundColor Green
        $script:PASS++
    } else {
        Write-Host "  FAIL  $Label  '$Needle' not in [$Haystack]" -ForegroundColor Red
        $script:FAIL++
    }
}

function Warn([string]$msg) {
    Write-Host "  WARN  $msg" -ForegroundColor Yellow
    $script:WARN++
}

function Read400Body {
    param($WebException)
    try {
        $stream = $WebException.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        return $reader.ReadToEnd()
    } catch { return "" }
}

function PostJson {
    param([string]$Uri, $Body, $Session)
    $json = $Body | ConvertTo-Json -Compress
    try {
        if ($Session) {
            $r = Invoke-RestMethod -Method POST -Uri $Uri -ContentType "application/json" -Body $json -WebSession $Session
        } else {
            $r = Invoke-RestMethod -Method POST -Uri $Uri -ContentType "application/json" -Body $json
        }
        return @{ Code = 200; Data = $r }
    } catch [System.Net.WebException] {
        $status = [int]$_.Exception.Response.StatusCode
        $rawBody = Read400Body $_.Exception
        $parsed = $null
        try { $parsed = $rawBody | ConvertFrom-Json } catch {}
        return @{ Code = $status; Data = $parsed; Raw = $rawBody }
    } catch {
        return @{ Code = 0; Data = $null; Raw = $_.Exception.Message }
    }
}

function GetJson {
    param([string]$Uri, $Session)
    try {
        if ($Session) {
            $r = Invoke-RestMethod -Method GET -Uri $Uri -WebSession $Session
        } else {
            $r = Invoke-RestMethod -Method GET -Uri $Uri
        }
        return @{ Code = 200; Data = $r }
    } catch [System.Net.WebException] {
        $status = [int]$_.Exception.Response.StatusCode
        $rawBody = Read400Body $_.Exception
        $parsed = $null
        try { $parsed = $rawBody | ConvertFrom-Json } catch {}
        return @{ Code = $status; Data = $parsed; Raw = $rawBody }
    } catch {
        return @{ Code = 0; Data = $null; Raw = $_.Exception.Message }
    }
}

function Login {
    param([string]$UserName, [string]$Password = "123456")
    $body = @{ userName = $UserName; password = $Password }
    try {
        $r = Invoke-RestMethod -Method POST -Uri "$BaseUrl/api/auth/login" `
            -ContentType "application/json" -Body ($body | ConvertTo-Json -Compress) `
            -SessionVariable sess
        return $sess
    } catch [System.Net.WebException] {
        $raw = Read400Body $_.Exception
        Write-Host "  LOGIN FAILED for $UserName : $raw" -ForegroundColor Red
        $script:FAIL++
        return $null
    }
}

# Submit a flow; returns instanceId or $null.
function Submit {
    param($Session, [string]$FlowKey, [string]$VarsJson = "{}")
    $r = PostJson "$BaseUrl/api/wf/flow/submit" @{ flowKey = $FlowKey; varsJson = $VarsJson; bizType = $null; bizId = $null } $Session
    if ($r.Code -ne 200) { Write-Host "  Submit $FlowKey error: $($r.Raw)" -ForegroundColor Red; $script:FAIL++; return $null }
    $id = $r.Data.data.instanceId
    if (-not $id) { $id = $r.Data.instanceId }
    return "$id"
}

# Find a user's pending task for a given instance (and optional nodeId).
# Bounded poll (approval tasks are created synchronously on submit/act, but a short
# retry absorbs any read-after-write lag). Returns the matching item or $null.
function FindTask {
    param($Session, [string]$InstanceId, [string]$NodeId = $null)
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        $r = GetJson "$BaseUrl/api/oa/inbox/pending" $Session
        if ($r.Code -eq 200) {
            $items = $r.Data.data
            if ($null -ne $items) {
                $hit = @($items | Where-Object {
                    "$($_.instanceId)" -eq "$InstanceId" -and ($null -eq $NodeId -or "$($_.nodeId)" -eq "$NodeId")
                }) | Select-Object -First 1
                if ($hit) { return $hit }
            }
        }
        Start-Sleep -Seconds $PollSeconds
    }
    return $null
}

# Count a user's pending tasks for an instance.
function CountTasks {
    param($Session, [string]$InstanceId)
    $r = GetJson "$BaseUrl/api/oa/inbox/pending" $Session
    if ($r.Code -ne 200) { return -1 }
    $items = $r.Data.data
    if ($null -eq $items) { return 0 }
    return @($items | Where-Object { "$($_.instanceId)" -eq "$InstanceId" }).Count
}

function Act {
    param($Session, [string]$TaskId, [bool]$Approve, [string]$Comment = $null)
    return PostJson "$BaseUrl/api/wf/task/$TaskId/act" @{ approve = $Approve; comment = $Comment } $Session
}

function SendBack {
    param($Session, [string]$TaskId, [string]$TargetNodeId, [string]$Comment = $null)
    return PostJson "$BaseUrl/api/wf/advanced/sendback" @{ taskId = $TaskId; targetNodeId = $TargetNodeId; comment = $Comment } $Session
}

# Instance status via OA inbox detail (any authenticated user can read detail today).
function GetStatus {
    param($Session, [string]$InstanceId)
    $r = GetJson "$BaseUrl/api/oa/inbox/detail/$InstanceId" $Session
    if ($r.Code -ne 200) { return -1 }
    $d = $r.Data.data
    if ($null -eq $d) { return -1 }
    return [int]$d.instance.status
}

# Does the starter have a notification of $Type for $InstanceId?
function HasNotification {
    param($Session, [string]$InstanceId, [int]$Type)
    $r = GetJson "$BaseUrl/api/oa/notification/list" $Session
    if ($r.Code -ne 200) { return $false }
    $items = $r.Data.data
    if ($null -eq $items) { return $false }
    $hit = @($items | Where-Object { [int]$_.type -eq $Type -and "$($_.instanceId)" -eq "$InstanceId" })
    return ($hit.Count -gt 0)
}

# ── Preflight: login seeded users ───────────────────────────────────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "WFS Kernel Hardening QA  ($BaseUrl)" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

$sStart = Login "qa_khd_starter"
if (-not $sStart) { Write-Host "ABORT: cannot login qa_khd_starter (seed applied?)" -ForegroundColor Red; exit 1 }
$sA = Login "qa_khd_a"
$sB = Login "qa_khd_b"
$sC = Login "qa_khd_c"
$sD = Login "qa_khd_d"
if (-not ($sA -and $sB -and $sC -and $sD)) { Write-Host "ABORT: branch approver login failed (seed applied?)" -ForegroundColor Red; exit 1 }

# ── Scenario 1 : inclusive 2 of 3 real edges -> exactly 2 todos -> Approved ──
Write-Host ""
Write-Host "-- Scenario 1 : inclusive split, 2 of 3 conditions true --" -ForegroundColor Cyan
$i1 = Submit $sStart $FK_INCL '{"subject":"s1","goA":1,"goB":1}'
if ($i1) {
    Write-Host "  Instance: $i1" -ForegroundColor Gray
    Chk "S1: instance Running after split" $ST_RUNNING (GetStatus $sStart $i1)
    $tA = FindTask $sA $i1 "a"
    $tB = FindTask $sB $i1 "b"
    ChkTrue "S1: branch A (goA>0) has a todo" ($null -ne $tA)
    ChkTrue "S1: branch B (goB>0) has a todo" ($null -ne $tB)
    Chk "S1: branch C (goC false) has NO todo" 0 (CountTasks $sC $i1)
    Chk "S1: default D (not taken, >=1 cond true) has NO todo" 0 (CountTasks $sD $i1)
    if ($tA -and $tB) {
        $rA = Act $sA $tA.taskId $true "A ok"
        Chk "S1: approve A HTTP" 200 $rA.Code
        Chk "S1: still Running after 1 of 2 branches" $ST_RUNNING (GetStatus $sStart $i1)
        $rB = Act $sB $tB.taskId $true "B ok"
        Chk "S1: approve B HTTP" 200 $rB.Code
        Chk "S1: inclusiveJoin dyn-counts 2 -> Approved" $ST_APPROVED (GetStatus $sStart $i1)
    }
} else { Warn "S1 skipped (submit failed)" }

# ── Scenario 2 : inclusive all-false -> default fallback only -> Approved ────
Write-Host ""
Write-Host "-- Scenario 2 : inclusive split, all conditions false -> default --" -ForegroundColor Cyan
$i2 = Submit $sStart $FK_INCL '{"subject":"s2"}'
if ($i2) {
    Write-Host "  Instance: $i2" -ForegroundColor Gray
    Chk "S2: branch A has NO todo (goA false)" 0 (CountTasks $sA $i2)
    Chk "S2: branch B has NO todo (goB false)" 0 (CountTasks $sB $i2)
    Chk "S2: branch C has NO todo (goC false)" 0 (CountTasks $sC $i2)
    $tD = FindTask $sD $i2 "d"
    ChkTrue "S2: default branch D has the sole todo" ($null -ne $tD)
    if ($tD) {
        $rD = Act $sD $tD.taskId $true "D ok"
        Chk "S2: approve default D HTTP" 200 $rD.Code
        Chk "S2: default-only -> Approved" $ST_APPROVED (GetStatus $sStart $i2)
    }
} else { Warn "S2 skipped (submit failed)" }

# ── Scenario 3 : prune one branch -> instance Running + BranchPruned notif ───
Write-Host ""
Write-Host "-- Scenario 3 : parallelSplit onBranchReject=prune --" -ForegroundColor Cyan
$i3 = Submit $sStart $FK_PRUNE '{"subject":"s3"}'
if ($i3) {
    Write-Host "  Instance: $i3" -ForegroundColor Gray
    $tA3 = FindTask $sA $i3 "a"
    $tB3exists = ($null -ne (FindTask $sB $i3 "b"))
    ChkTrue "S3: branch A todo present" ($null -ne $tA3)
    ChkTrue "S3: branch B todo present" $tB3exists
    if ($tA3) {
        $rA3 = Act $sA $tA3.taskId $false "A rejects (prune)"
        Chk "S3: reject A HTTP" 200 $rA3.Code
        Chk "S3: instance STAYS Running (prune, no cascade)" $ST_RUNNING (GetStatus $sStart $i3)
        ChkTrue "S3: sibling B todo still alive" ($null -ne (FindTask $sB $i3 "b"))
        ChkTrue "S3: starter got BranchPruned notification (type=5)" (HasNotification $sStart $i3 $NT_BRANCH_PRUNED)
        $tB3 = FindTask $sB $i3 "b"
        if ($tB3) {
            $rB3 = Act $sB $tB3.taskId $true "B ok"
            Chk "S3: approve B HTTP" 200 $rB3.Code
            Chk "S3: join dyn-count (Pruned drops) -> Approved" $ST_APPROVED (GetStatus $sStart $i3)
        }
    }
} else { Warn "S3 skipped (submit failed)" }

# ── Scenario 4 : cascade default -> reject whole instance ───────────────────
Write-Host ""
Write-Host "-- Scenario 4 : parallelSplit cascade (no onBranchReject) --" -ForegroundColor Cyan
$i4 = Submit $sStart $FK_CASC '{"subject":"s4"}'
if ($i4) {
    Write-Host "  Instance: $i4" -ForegroundColor Gray
    $tA4 = FindTask $sA $i4 "a"
    ChkTrue "S4: branch A todo present" ($null -ne $tA4)
    if ($tA4) {
        $rA4 = Act $sA $tA4.taskId $false "A rejects (cascade)"
        Chk "S4: reject A HTTP" 200 $rA4.Code
        Chk "S4: whole instance Rejected (cascade)" $ST_REJECTED (GetStatus $sStart $i4)
        Chk "S4: sibling B todo voided (no pending)" 0 (CountTasks $sB $i4)
        ChkTrue "S4: starter got NO BranchPruned notification" (-not (HasNotification $sStart $i4 $NT_BRANCH_PRUNED))
    }
} else { Warn "S4 skipped (submit failed)" }

# ── Scenario 5 : SameBranch send-back -> only A stripped, B survives ─────────
Write-Host ""
Write-Host "-- Scenario 5 : send-back within branch (SameBranch) --" -ForegroundColor Cyan
$i5 = Submit $sStart $FK_SB '{"subject":"s5"}'
if ($i5) {
    Write-Host "  Instance: $i5" -ForegroundColor Gray
    $t_a1 = FindTask $sA $i5 "a1"
    ChkTrue "S5: a1 todo present" ($null -ne $t_a1)
    if ($t_a1) {
        $rA1 = Act $sA $t_a1.taskId $true "a1 ok"
        Chk "S5: approve a1 -> a2 HTTP" 200 $rA1.Code
        $t_a2 = FindTask $sC $i5 "a2"
        ChkTrue "S5: a2 todo present after a1" ($null -ne $t_a2)
        if ($t_a2) {
            $sbkResp = SendBack $sC $t_a2.taskId "a1" "same-branch back"
            Chk "S5: send-back a2 -> a1 HTTP" 200 $sbkResp.Code
            Chk "S5: instance still Running" $ST_RUNNING (GetStatus $sStart $i5)
            ChkTrue "S5: sibling b1 todo NOT disturbed" ($null -ne (FindTask $sB $i5 "b1"))
            $t_a1r = FindTask $sA $i5 "a1"
            ChkTrue "S5: a1 reborn todo present" ($null -ne $t_a1r)
            if ($t_a1r) {
                $x = Act $sA $t_a1r.taskId $true "a1 redo"
                $t_a2r = FindTask $sC $i5 "a2"
                if ($t_a2r) { $x = Act $sC $t_a2r.taskId $true "a2 redo" }
                $t_b1 = FindTask $sB $i5 "b1"
                if ($t_b1) { $x = Act $sB $t_b1.taskId $true "b1 ok" }
                Chk "S5: rerun A + approve B -> Approved (join recognises kin)" $ST_APPROVED (GetStatus $sStart $i5)
            }
        }
    }
} else { Warn "S5 skipped (submit failed)" }

# ── Scenario 6 : SiblingBranch send-back -> E-WF-019, nothing mutated ────────
Write-Host ""
Write-Host "-- Scenario 6 : send-back to sibling branch (E-WF-019) --" -ForegroundColor Cyan
$i6 = Submit $sStart $FK_SB '{"subject":"s6"}'
if ($i6) {
    Write-Host "  Instance: $i6" -ForegroundColor Gray
    $t6_a1 = FindTask $sA $i6 "a1"
    if ($t6_a1) {
        $x = Act $sA $t6_a1.taskId $true "a1 ok"
        $t6_a2 = FindTask $sC $i6 "a2"
        ChkTrue "S6: a2 todo present" ($null -ne $t6_a2)
        if ($t6_a2) {
            $sb6 = SendBack $sC $t6_a2.taskId "b1" "illegal cross-branch"
            Chk "S6: send-back a2 -> b1 rejected (HTTP 400)" 400 $sb6.Code
            ChkContains "S6: error body carries E-WF-019" "E-WF-019" ("$($sb6.Raw)$($sb6.Data.message)")
            # verify-before-write: a2 todo untouched, b1 still pending, instance Running
            ChkTrue "S6: a2 todo still pending (no mutation)" ($null -ne (FindTask $sC $i6 "a2"))
            ChkTrue "S6: b1 todo still pending (no mutation)" ($null -ne (FindTask $sB $i6 "b1"))
            Chk "S6: instance still Running" $ST_RUNNING (GetStatus $sStart $i6)
        }
    } else { Warn "S6: a1 todo missing (submit path?)" }
} else { Warn "S6 skipped (submit failed)" }

# ── Summary ─────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "RESULTS: PASS=$PASS   FAIL=$FAIL   WARN=$WARN" -ForegroundColor $(if ($FAIL -gt 0) { "Red" } elseif ($WARN -gt 0) { "Yellow" } else { "Green" })
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "SCENARIO 7 (designer real browser) is MANUAL -- see README section 5."
Write-Host ""
Write-Host "MANUAL DB CHECKS (sqlcmd -S localhost\KOUSQLSERVER -d CP6DB_OA -E -C):"
Write-Host "  -- Pruned token + BranchPruned notification (scenario 3):"
Write-Host "  SELECT t.NodeId, t.Status FROM Wf_FlowToken t JOIN Wf_FlowInstance i ON i.Id=t.InstanceId WHERE i.FlowKey='khd-prune';"
Write-Host "  SELECT Type, Title, InstanceId FROM Wf_Notification WHERE Type=5 ORDER BY CreateDate DESC;  -- Type 5 = BranchPruned"
Write-Host "  -- branchPruned history rows:"
Write-Host "  SELECT InstanceId, NodeId, Action FROM Wf_FlowHistory WHERE Action IN ('branchPruned','sendback') ORDER BY CreateDate DESC;"
Write-Host ""
if ($FAIL -gt 0) { exit 1 } else { exit 0 }
