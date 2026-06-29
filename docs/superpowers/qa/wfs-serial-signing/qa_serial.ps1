#Requires -Version 5.1
<#
.SYNOPSIS
    WFS Serial Signing -- HTTP e2e QA script (scenarios 2, 4, 5).

.DESCRIPTION
    Exercises scenarios 2, 4, and 5 of the serial-signing QA harness against a
    RUNNING backend.  Does NOT start the server -- run the backend first:

        cd D:\CP6-wfs-serial\CP6.WebApi
        $env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
        dotnet run --urls "http://localhost:5178"

    Apply seed first:
        sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql

    Then run this script:
        .\qa_serial.ps1
        .\qa_serial.ps1 -BaseUrl http://localhost:5178

.NOTES
    PS5.1 compatibility:
      - No && operator. Sequential steps use try/finally or explicit checks.
      - Invoke-RestMethod throws WebException on non-2xx. Error body read via
        GetResponseStream (see Read400Body helper).
      - All body strings use ASCII-only values (no Chinese/Japanese).
      - JSON bodies built via ConvertTo-Json (no manual escaping needed).
      - Default encoding is UTF-16 LE; -Encoding utf8 used when writing files.

    Cookie handling:
      - CSRF is disabled in dev (Security:Csrf:Enabled=false). No X-CSRF-Token needed.
      - Sessions are managed by Invoke-RestMethod -SessionVariable / -WebSession.
      - The cp6_at JWT cookie is set by the login response and automatically sent
        by subsequent calls in the same WebSession.

    Envelope shape:
      - All responses: { code: 0, message: "OK", data: <payload> }
      - Submit:  $r.data.instanceId  (Guid)
      - Pending: $r.data             (array of InboxPendingItem)
      - Act:     $r.data             (null on success, HTTP 200)
      - Sendback:$r.data             (true on success, HTTP 200)
      - DraftSubmit: $r.data         (null on success, HTTP 200)
#>

param(
    [string]$BaseUrl = "http://localhost:5178"
)

$ErrorActionPreference = "Continue"   # We handle errors manually per-call.
$PASS = 0; $FAIL = 0; $WARN = 0

# ── GUIDs from seed.sql ─────────────────────────────────────────────────────
$G_s0     = "AAAA0000-0000-0000-0000-0000000000A0"   # qa_s_s0
$G_start  = "AAAA0000-0000-0000-0000-0000000000B0"   # qa_s_start
$G_mgr1   = "AAAA0000-0000-0000-0000-0000000000C0"   # qa_s_mgr1
$G_mgr2   = "AAAA0000-0000-0000-0000-0000000000D0"   # qa_s_mgr2
$G_gm     = "AAAA0000-0000-0000-0000-0000000000E0"   # qa_s_gm
$FLOW_KEY = "serial-demo"

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

function ChkGt {
    param([string]$Label, [int]$Threshold, $Actual)
    $v = [int]"$Actual"
    if ($v -gt $Threshold) {
        Write-Host "  PASS  $Label (=$Actual > $Threshold)" -ForegroundColor Green
        $script:PASS++
    } else {
        Write-Host "  FAIL  $Label  expected (>$Threshold)  got=[$Actual]" -ForegroundColor Red
        $script:FAIL++
    }
}

function Warn([string]$msg) {
    Write-Host "  WARN  $msg" -ForegroundColor Yellow
    $script:WARN++
}

# Read the response body from a WebException (PS5.1 pattern for 4xx/5xx bodies).
function Read400Body {
    param($WebException)
    try {
        $stream = $WebException.Response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        return $reader.ReadToEnd()
    } catch {
        return ""
    }
}

# POST a JSON body; return ($statusCode, $responseObject).
# Never throws -- wraps 4xx into returned values.
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

# Login: returns a WebSession with cp6_at cookie set, or $null on failure.
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

# Submit a flow and return the instanceId string (or $null on failure).
function Submit {
    param($Session, [string]$FlowKey)
    $r = PostJson "$BaseUrl/api/wf/flow/submit" @{ flowKey = $FlowKey; varsJson = "{}"; bizType = $null; bizId = $null } $Session
    Chk "Submit $FlowKey HTTP" 200 $r.Code
    if ($r.Code -eq 200) {
        $id = $r.Data.data.instanceId
        if (-not $id) { $id = $r.Data.instanceId }   # handle both wrapper depths
        return "$id"
    }
    Write-Host "  Submit error: $($r.Raw)" -ForegroundColor Red
    return $null
}

# Get pending tasks for the logged-in user; return array (may be empty).
function GetPending {
    param($Session)
    $r = GetJson "$BaseUrl/api/oa/inbox/pending" $Session
    if ($r.Code -ne 200) {
        Write-Host "  GetPending failed: $($r.Raw)" -ForegroundColor Red
        return @()
    }
    $items = $r.Data.data
    if ($null -eq $items) { $items = @() }
    return @($items)
}

# Approve or reject a task. Returns HTTP code.
function Act {
    param($Session, [string]$TaskId, [bool]$Approve, [string]$Comment = "")
    $r = PostJson "$BaseUrl/api/wf/task/$TaskId/act" @{ approve = $Approve; comment = $Comment } $Session
    return $r.Code
}

# Send-back. kind in: prevStage, starter, node. nodeId optional (for kind=node).
# Returns @{ Code; Message }.
function Sendback {
    param($Session, [string]$TaskId, [string]$Kind, [string]$NodeId = $null, [string]$Comment = "")
    $body = @{ taskId = $TaskId; kind = $Kind; comment = $Comment }
    if ($NodeId) { $body.nodeId = $NodeId }
    $r = PostJson "$BaseUrl/api/oa/inbox/sendback" $body $Session
    $msg = ""
    if ($r.Data) {
        try { $msg = $r.Data.message } catch {}
        if (-not $msg -and $r.Raw) { $msg = $r.Raw.Substring(0, [Math]::Min(120, $r.Raw.Length)) }
    }
    return @{ Code = $r.Code; Message = $msg }
}

# Submit a draft instance. Returns HTTP code.
function SubmitDraft {
    param($Session, [string]$InstanceId)
    $r = PostJson "$BaseUrl/api/oa/draft/submit" @{ id = $InstanceId } $Session
    return $r.Code
}

# Get instance detail. Returns the data object or $null.
function GetDetail {
    param($Session, [string]$InstanceId)
    $r = GetJson "$BaseUrl/api/oa/inbox/detail/$InstanceId" $Session
    if ($r.Code -eq 200) { return $r.Data.data }
    return $null
}

# ── Scenario 2 : Happy-path (submit + advance all 4 runtime stages) ─────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Scenario 2 : Submit serial-demo -> advance 4 runtime stages" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 2-a. Login as qa_s_start and submit.
$sessStart = Login "qa_s_start"
if (-not $sessStart) { Write-Host "ABORT Scenario 2: cannot login as qa_s_start" -ForegroundColor Red; exit 1 }

$inst2 = Submit $sessStart $FLOW_KEY
if (-not $inst2) { Write-Host "ABORT Scenario 2: submit failed" -ForegroundColor Red; exit 1 }
Write-Host "  Instance: $inst2" -ForegroundColor Gray

# 2-b. Stage 0 -- qa_s_s0 approves.
Write-Host "-- Stage 0 (qa_s_s0) --"
$sess_s0 = Login "qa_s_s0"
$pending_s0 = GetPending $sess_s0
$task_s0 = @($pending_s0 | Where-Object { "$($_.instanceId)" -eq "$inst2" }) | Select-Object -First 1
Chk "Stage0: pending item found for instance" $true ($null -ne $task_s0)
if ($task_s0) {
    Chk "Stage0: stageIndex" 0 $task_s0.stageIndex
    Chk "Stage0: stageRound" 0 $task_s0.stageRound
    Chk "Stage0: canSendBackPrevStage (must be false at index 0)" $false $task_s0.canSendBackPrevStage
    $code = Act $sess_s0 "$($task_s0.taskId)" $true "Approve S0"
    Chk "Stage0: act HTTP" 200 $code
}

# 2-c. Stage 1 -- qa_s_mgr1.
Write-Host "-- Stage 1 (qa_s_mgr1) --"
$sess_m1 = Login "qa_s_mgr1"
$pending_m1 = GetPending $sess_m1
$task_m1 = @($pending_m1 | Where-Object { "$($_.instanceId)" -eq "$inst2" }) | Select-Object -First 1
Chk "Stage1: pending item found" $true ($null -ne $task_m1)
if ($task_m1) {
    Chk "Stage1: stageIndex" 1 $task_m1.stageIndex
    Chk "Stage1: stageRound" 0 $task_m1.stageRound
    Chk "Stage1: canSendBackPrevStage" $true $task_m1.canSendBackPrevStage
    $code = Act $sess_m1 "$($task_m1.taskId)" $true "Approve S1"
    Chk "Stage1: act HTTP" 200 $code
}

# 2-d. Stage 2 -- qa_s_mgr2.
Write-Host "-- Stage 2 (qa_s_mgr2) --"
$sess_m2 = Login "qa_s_mgr2"
$pending_m2 = GetPending $sess_m2
$task_m2 = @($pending_m2 | Where-Object { "$($_.instanceId)" -eq "$inst2" }) | Select-Object -First 1
Chk "Stage2: pending item found" $true ($null -ne $task_m2)
if ($task_m2) {
    Chk "Stage2: stageIndex" 2 $task_m2.stageIndex
    Chk "Stage2: stageRound" 0 $task_m2.stageRound
    Chk "Stage2: canSendBackPrevStage" $true $task_m2.canSendBackPrevStage
    $code = Act $sess_m2 "$($task_m2.taskId)" $true "Approve S2"
    Chk "Stage2: act HTTP" 200 $code
}

# 2-e. Stage 3 -- qa_s_gm.
Write-Host "-- Stage 3 (qa_s_gm) --"
$sess_gm = Login "qa_s_gm"
$pending_gm = GetPending $sess_gm
$task_gm = @($pending_gm | Where-Object { "$($_.instanceId)" -eq "$inst2" }) | Select-Object -First 1
Chk "Stage3: pending item found" $true ($null -ne $task_gm)
if ($task_gm) {
    Chk "Stage3: stageIndex" 3 $task_gm.stageIndex
    Chk "Stage3: stageRound" 0 $task_gm.stageRound
    $code = Act $sess_gm "$($task_gm.taskId)" $true "Approve S3 GM"
    Chk "Stage3: act HTTP" 200 $code
}

# 2-f. Verify instance Approved via detail.
Write-Host "-- Verify instance Approved --"
# Use qa_s_start's session (starter can always view detail).
$detail2 = GetDetail $sessStart $inst2
if ($null -ne $detail2) {
    $instStatus = $detail2.instance.status
    Chk "Scenario2: instance status Approved (1)" 1 $instStatus
    # Timeline should have 4 rows (one per stage, stageIndex 0..3).
    $tlCount = @($detail2.timeline).Count
    Chk "Scenario2: timeline row count" 4 $tlCount
} else {
    Warn "Scenario2: could not fetch detail; skip status check"
}

Write-Host ""
Write-Host "Scenario 2 complete." -ForegroundColor Cyan

# ── Scenario 4 : prevStage send-back (repeat twice, tally isolation) ─────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Scenario 4 : prevStage send-back x2 (R12 tally isolation)" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 4-a. Submit a fresh instance as qa_s_start.
$inst4 = Submit $sessStart $FLOW_KEY
if (-not $inst4) { Write-Host "ABORT Scenario 4: submit failed" -ForegroundColor Red; exit 1 }
Write-Host "  Instance: $inst4" -ForegroundColor Gray

# 4-b. Approve stage 0.
Write-Host "-- Stage 0 approve --"
$p0 = @(GetPending $sess_s0 | Where-Object { "$($_.instanceId)" -eq "$inst4" }) | Select-Object -First 1
if ($p0) { Act $sess_s0 "$($p0.taskId)" $true "S4 Approve S0" | Out-Null }

# 4-c. Approve stage 1 (first time, round 0).
Write-Host "-- Stage 1 approve (round 0) --"
$p1_r0 = @(GetPending $sess_m1 | Where-Object { "$($_.instanceId)" -eq "$inst4" }) | Select-Object -First 1
Chk "S4: stage1 round0 stageRound" 0 $p1_r0.stageRound
if ($p1_r0) { Act $sess_m1 "$($p1_r0.taskId)" $true "S4 Approve S1 R0" | Out-Null }

# 4-d. Send back prevStage from stage 2, round 0 (first send-back).
Write-Host "-- Stage 2 -> sendback prevStage (first, round 0) --"
$p2_r0 = @(GetPending $sess_m2 | Where-Object { "$($_.instanceId)" -eq "$inst4" }) | Select-Object -First 1
Chk "S4: stage2 present before first sendback" $true ($null -ne $p2_r0)
if ($p2_r0) {
    $sb1 = Sendback $sess_m2 "$($p2_r0.taskId)" "prevStage" $null "SB round 1"
    Chk "S4: first prevStage sendback HTTP" 200 $sb1.Code
}

# 4-e. Stage 1 reappears at stageRound=1.
Write-Host "-- Stage 1 reappears (round 1) --"
$p1_r1 = @(GetPending $sess_m1 | Where-Object { "$($_.instanceId)" -eq "$inst4" }) | Select-Object -First 1
Chk "S4: stage1 round1 found" $true ($null -ne $p1_r1)
if ($p1_r1) {
    Chk "S4: stage1 stageRound=1" 1 $p1_r1.stageRound
    Chk "S4: stage1 stageIndex=1" 1 $p1_r1.stageIndex
    Act $sess_m1 "$($p1_r1.taskId)" $true "S4 Approve S1 R1" | Out-Null
}

# 4-f. Send back prevStage from stage 2 again (second send-back).
Write-Host "-- Stage 2 -> sendback prevStage (second, round 1) --"
$p2_r1 = @(GetPending $sess_m2 | Where-Object { "$($_.instanceId)" -eq "$inst4" }) | Select-Object -First 1
if ($p2_r1) {
    $sb2 = Sendback $sess_m2 "$($p2_r1.taskId)" "prevStage" $null "SB round 2"
    Chk "S4: second prevStage sendback HTTP" 200 $sb2.Code
}

# 4-g. Stage 1 reappears at stageRound=2.
Write-Host "-- Stage 1 reappears (round 2) --"
$p1_r2 = @(GetPending $sess_m1 | Where-Object { "$($_.instanceId)" -eq "$inst4" }) | Select-Object -First 1
Chk "S4: stage1 round2 found" $true ($null -ne $p1_r2)
if ($p1_r2) {
    Chk "S4: stage1 stageRound=2" 2 $p1_r2.stageRound
    Act $sess_m1 "$($p1_r2.taskId)" $true "S4 Approve S1 R2" | Out-Null
}

# 4-h. Now advance stage 2 and stage 3 to completion.
Write-Host "-- Stage 2 final approve --"
$p2_final = @(GetPending $sess_m2 | Where-Object { "$($_.instanceId)" -eq "$inst4" }) | Select-Object -First 1
if ($p2_final) {
    Chk "S4: final stage2 stageRound" 2 $p2_final.stageRound
    Act $sess_m2 "$($p2_final.taskId)" $true "S4 Final Approve S2" | Out-Null
}

Write-Host "-- Stage 3 final approve (GM) --"
$p3_final = @(GetPending $sess_gm | Where-Object { "$($_.instanceId)" -eq "$inst4" }) | Select-Object -First 1
if ($p3_final) {
    Act $sess_gm "$($p3_final.taskId)" $true "S4 Approve GM" | Out-Null
}

# 4-i. Check instance is Approved after the full saga.
Write-Host "-- Verify instance Approved after 2x prevStage sendback --"
$detail4 = GetDetail $sessStart $inst4
if ($null -ne $detail4) {
    Chk "Scenario4: instance status Approved (1)" 1 $detail4.instance.status
    # Stage 1 should appear 3 times in the timeline (rounds 0, 1, 2).
    $stage1Rows = @($detail4.timeline | Where-Object { $_.stageIndex -eq 1 })
    ChkGt "Scenario4: timeline stage1 rows >= 3 (one per round)" 2 $stage1Rows.Count
} else {
    Warn "Scenario4: could not fetch detail"
}

Write-Host ""
Write-Host "Scenario 4 complete." -ForegroundColor Cyan

# ── Scenario 5 : starter send-back -> Draft -> resubmit ──────────────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Scenario 5 : starter send-back -> Draft -> resubmit from stage 0" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 5-a. Submit a fresh instance.
$inst5 = Submit $sessStart $FLOW_KEY
if (-not $inst5) { Write-Host "ABORT Scenario 5: submit failed" -ForegroundColor Red; exit 1 }
Write-Host "  Instance: $inst5" -ForegroundColor Gray

# 5-b. Approve stage 0.
Write-Host "-- Stage 0 approve --"
$p5_s0 = @(GetPending $sess_s0 | Where-Object { "$($_.instanceId)" -eq "$inst5" }) | Select-Object -First 1
if ($p5_s0) { Act $sess_s0 "$($p5_s0.taskId)" $true "S5 Approve S0" | Out-Null }

# 5-c. Stage 1: send back to starter.
Write-Host "-- Stage 1 -> sendback starter --"
$p5_s1 = @(GetPending $sess_m1 | Where-Object { "$($_.instanceId)" -eq "$inst5" }) | Select-Object -First 1
Chk "S5: stage1 pending before sendback" $true ($null -ne $p5_s1)
if ($p5_s1) {
    $sb5 = Sendback $sess_m1 "$($p5_s1.taskId)" "starter" $null "Back to initiator"
    Chk "S5: starter sendback HTTP" 200 $sb5.Code
}

# 5-d. Verify qa_s_mgr1 inbox is now empty for this instance.
Write-Host "-- Verify stage1 task gone (cancelled) --"
$p5_m1_after = @(GetPending $sess_m1 | Where-Object { "$($_.instanceId)" -eq "$inst5" })
Chk "S5: mgr1 has no pending tasks for instance after sendback" 0 $p5_m1_after.Count

# 5-e. Verify instance is in Draft state via draft/list.
Write-Host "-- Verify instance appears in starter draft list --"
$draftR = GetJson "$BaseUrl/api/oa/draft/list" $sessStart
$draftItems = @()
if ($draftR.Code -eq 200) {
    $draftItems = @($draftR.Data.data | Where-Object { "$($_.instanceId)" -eq "$inst5" })
    if ($draftItems.Count -eq 0) {
        # Try alternate field name
        $draftItems = @($draftR.Data.data | Where-Object { "$($_.id)" -eq "$inst5" })
    }
}
Chk "S5: instance in draft list" $true ($draftItems.Count -gt 0)

# 5-f. Resubmit the draft as qa_s_start.
Write-Host "-- Resubmit draft --"
$resubCode = SubmitDraft $sessStart $inst5
Chk "S5: resubmit draft HTTP" 200 $resubCode

# 5-g. Stage 0 should be active again (from stage 0, fresh tally).
Write-Host "-- Verify stage 0 active again after resubmit --"
$p5_s0_after = @(GetPending $sess_s0 | Where-Object { "$($_.instanceId)" -eq "$inst5" }) | Select-Object -First 1
Chk "S5: stage0 pending after resubmit" $true ($null -ne $p5_s0_after)
if ($p5_s0_after) {
    Chk "S5: stage0 stageIndex after resubmit" 0 $p5_s0_after.stageIndex
}

# 5-h. Advance all stages to completion.
Write-Host "-- Advance to completion (post-resubmit) --"
if ($p5_s0_after) { Act $sess_s0 "$($p5_s0_after.taskId)" $true "S5 ReApprove S0" | Out-Null }
$p5_m1_b = @(GetPending $sess_m1 | Where-Object { "$($_.instanceId)" -eq "$inst5" }) | Select-Object -First 1
if ($p5_m1_b) { Act $sess_m1 "$($p5_m1_b.taskId)" $true "S5 ReApprove S1" | Out-Null }
$p5_m2_b = @(GetPending $sess_m2 | Where-Object { "$($_.instanceId)" -eq "$inst5" }) | Select-Object -First 1
if ($p5_m2_b) { Act $sess_m2 "$($p5_m2_b.taskId)" $true "S5 ReApprove S2" | Out-Null }
$p5_gm_b = @(GetPending $sess_gm | Where-Object { "$($_.instanceId)" -eq "$inst5" }) | Select-Object -First 1
if ($p5_gm_b) { Act $sess_gm "$($p5_gm_b.taskId)" $true "S5 ReApprove GM" | Out-Null }

# 5-i. Verify Approved.
$detail5 = GetDetail $sessStart $inst5
if ($null -ne $detail5) {
    Chk "Scenario5: instance Approved after full re-run" 1 $detail5.instance.status
} else {
    Warn "Scenario5: could not fetch detail"
}

Write-Host ""
Write-Host "Scenario 5 complete." -ForegroundColor Cyan

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "RESULTS: PASS=$PASS   FAIL=$FAIL   WARN=$WARN" -ForegroundColor $(if ($FAIL -gt 0) { "Red" } elseif ($WARN -gt 0) { "Yellow" } else { "Green" })
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "MANUAL DB CHECKS to perform after this script:"
Write-Host "  -- Scenario 4 tally isolation (R12):"
Write-Host "  SELECT StageIndex, StageRound, Status, COUNT(*) AS n"
Write-Host "  FROM Wf_FlowTask WHERE InstanceId = '$inst4'"
Write-Host "  GROUP BY StageIndex, StageRound, Status ORDER BY StageIndex, StageRound;"
Write-Host ""
Write-Host "  -- Scenario 2 StagePlanJson frozen (should be 4 entries):"
Write-Host "  SELECT LEN(StagePlanJson), StagePlanJson"
Write-Host "  FROM Wf_FlowToken WHERE InstanceId = '$inst2';"
Write-Host ""
Write-Host "  -- Scenario 5 FormTo SentBack(7) / Voided(6) rows:"
Write-Host "  SELECT Status, COUNT(*) FROM Wf_FlowFormTo WHERE InstanceId = '$inst5'"
Write-Host "  GROUP BY Status;"
Write-Host ""
if ($FAIL -gt 0) {
    exit 1
} else {
    exit 0
}
