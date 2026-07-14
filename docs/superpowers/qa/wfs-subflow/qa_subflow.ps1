#Requires -Version 5.1
<#
.SYNOPSIS
    WFS Sub-flow (F-T2) -- HTTP e2e QA script (scenarios 1, 3, 4, 5, 6, 8).

.DESCRIPTION
    Exercises the sub-flow call-activity / two-phase resume / multi-instance /
    cascade / prune-composition features against a RUNNING backend. Does NOT start
    the server. Apply seed.sql first, then run the backend against isolated CP6DB_OA:

        sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql

        cd <repo>\CP6.WebApi
        $env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
        dotnet run --urls "http://localhost:5181"

    Then:
        .\qa_subflow.ps1
        .\qa_subflow.ps1 -BaseUrl http://localhost:5181

    Child completion resumes the parent via the in-request FAST PATH (the DI-injected
    scoped FlowEngine), so the parent advances within the same request, well under the
    20s scan-worker interval. The WaitSeconds poll (default 20) also absorbs the worker
    fallback path if the fast path is ever bypassed. Scenarios 2 (browser interlink) and
    7 (designer drag/drop + validation) are MANUAL -- see README sections 5.1 / 5.2.
    The "stop worker" / worker-fallback drill for scenario 1 is a manual DB drill -- see
    README section 4.1 (the scan worker is an in-process IHostedService with no runtime
    toggle).

    *** STATUS: written, not run. *** Authored per task F-T2 (write-only). Live QA is
    executed later by the main agent with a QA user present. Nothing here has run.

.NOTES
    PS5.1 compatibility (mirrors wfs-kernel-hardening/qa_kernel_hardening.ps1):
      - No && operator; sequential steps use explicit checks.
      - Invoke-RestMethod throws WebException on non-2xx; body read via Read400Body.
      - All request bodies use ASCII-only values (no CJK).
      - CSRF disabled in dev (Security:Csrf:Enabled=false); JWT cp6_at cookie flows
        automatically via -SessionVariable / -WebSession.

    Endpoints:
      - Login    : POST /api/auth/login             { userName, password } -> cp6_at cookie
      - Submit   : POST /api/wf/flow/submit          { flowKey, varsJson } -> data.instanceId
      - Act      : POST /api/wf/task/{id}/act         { approve, comment }
      - Withdraw : POST /api/wf/flow/{id}/withdraw    (route id; RequirePermission oa-inbox:withdraw)
      - Pending  : GET  /api/oa/inbox/pending         -> data: InboxPendingItem[]  (.taskId .instanceId .nodeId)
      - Detail   : GET  /api/oa/inbox/detail/{id}     -> data: InboxDetail
                     (.instance.status .currentDataJson .subFlowParent .subFlows[])
                     subFlows[]: { instanceId, subIndex, flowKey, flowName, status, nodeId }

    Envelope: { code: 0, message: "OK", data: <payload> }
    Instance status (FlowInstanceStatus): 0 Running  1 Approved  2 Rejected  3 Withdrawn  4 Suspended  5 Draft
#>

param(
    [string]$BaseUrl     = "http://localhost:5181",
    [int]   $WaitSeconds = 20,
    [int]   $PollSeconds = 2
)

$ErrorActionPreference = "Continue"
$PASS = 0; $FAIL = 0; $WARN = 0

# -- FlowKeys from seed.sql --------------------------------------------------
$FK_SINGLE = "sf-parent-single"
$FK_ALL    = "sf-multi-all"
$FK_ANY    = "sf-multi-any"
$FK_COMBO  = "sf-combo-prune"

# Status codes
$ST_RUNNING   = 0
$ST_APPROVED  = 1
$ST_REJECTED  = 2
$ST_WITHDRAWN = 3

# -- Helpers -----------------------------------------------------------------

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
    $json = if ($null -ne $Body) { $Body | ConvertTo-Json -Compress } else { $null }
    try {
        if ($Session) {
            if ($null -ne $json) { $r = Invoke-RestMethod -Method POST -Uri $Uri -ContentType "application/json" -Body $json -WebSession $Session }
            else                 { $r = Invoke-RestMethod -Method POST -Uri $Uri -WebSession $Session }
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
        if ($Session) { $r = Invoke-RestMethod -Method GET -Uri $Uri -WebSession $Session }
        else          { $r = Invoke-RestMethod -Method GET -Uri $Uri }
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

# Find a user's pending task for (instance, node). Bounded poll: absorbs the fast-path
# in-request advance and, as a fallback, the <=20s scan-worker path. Returns item or $null.
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

function Act {
    param($Session, [string]$TaskId, [bool]$Approve, [string]$Comment = $null)
    return PostJson "$BaseUrl/api/wf/task/$TaskId/act" @{ approve = $Approve; comment = $Comment } $Session
}

function Withdraw {
    param($Session, [string]$InstanceId)
    return PostJson "$BaseUrl/api/wf/flow/$InstanceId/withdraw" $null $Session
}

# Full inbox detail payload (data) or $null.
function GetDetail {
    param($Session, [string]$InstanceId)
    $r = GetJson "$BaseUrl/api/oa/inbox/detail/$InstanceId" $Session
    if ($r.Code -ne 200) { return $null }
    return $r.Data.data
}

# Instance status via detail (any authenticated user can read detail today).
function GetStatus {
    param($Session, [string]$InstanceId)
    $d = GetDetail $Session $InstanceId
    if ($null -eq $d) { return -1 }
    return [int]$d.instance.status
}

# The parent's child sub-flow instances (data.subFlows), waiting until >= expected appear.
# Returns an array (possibly empty). Each element: { instanceId, subIndex, status, nodeId }.
function GetSubFlows {
    param($Session, [string]$ParentId, [int]$Expected = 1)
    # Note: PS5.1 ConvertFrom-Json turns an empty JSON array into $null, so filter nulls
    # (@($null).Count is 1 -- a false positive without the Where-Object).
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        $d = GetDetail $Session $ParentId
        if ($null -ne $d) {
            $rows = @($d.subFlows | Where-Object { $null -ne $_ })
            if ($rows.Count -ge $Expected) { return $rows }
        }
        Start-Sleep -Seconds $PollSeconds
    }
    $d = GetDetail $Session $ParentId
    if ($null -eq $d) { return @() }
    return @($d.subFlows | Where-Object { $null -ne $_ })
}

# currentDataJson (parent VarsJson snapshot) with whitespace stripped, for order asserts.
function GetVarsCompact {
    param($Session, [string]$InstanceId)
    $d = GetDetail $Session $InstanceId
    if ($null -eq $d) { return "" }
    return ("$($d.currentDataJson)" -replace '\s', '')
}

# -- Preflight: login seeded users -------------------------------------------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "WFS Sub-flow QA  ($BaseUrl)" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

$sStart  = Login "sf_starter"
if (-not $sStart) { Write-Host "ABORT: cannot login sf_starter (seed applied?)" -ForegroundColor Red; exit 1 }
$sParent = Login "sf_parent"
$sChild  = Login "sf_child"
$sB      = Login "sf_b"
if (-not ($sParent -and $sChild -and $sB)) { Write-Host "ABORT: approver login failed (seed applied?)" -ForegroundColor Red; exit 1 }

# -- Scenario 1 : single instance full chain (submit -> child -> fast-path resume) --
Write-Host ""
Write-Host "-- Scenario 1 : single sub-flow, full chain (fast-path resume) --" -ForegroundColor Cyan
$i1 = Submit $sStart $FK_SINGLE '{"subject":"s1"}'
if ($i1) {
    Write-Host "  Parent instance: $i1" -ForegroundColor Gray
    Chk "S1: parent Running while parked on sub-flow" $ST_RUNNING (GetStatus $sStart $i1)
    $kids1 = GetSubFlows $sStart $i1 1
    Chk "S1: parent detail shows 1 sub-flow instance" 1 $kids1.Count
    if ($kids1.Count -ge 1) {
        $c1 = "$($kids1[0].instanceId)"
        Chk "S1: child instance Running" $ST_RUNNING (GetStatus $sStart $c1)
        $tc1 = FindTask $sChild $c1 "ca"
        ChkTrue "S1: child approval task present (node ca)" ($null -ne $tc1)
        if ($tc1) {
            $rc1 = Act $sChild $tc1.taskId $true "child ok"
            Chk "S1: approve child HTTP" 200 $rc1.Code
            Chk "S1: child instance Approved" $ST_APPROVED (GetStatus $sStart $c1)
            # fast path resumed the parent in-request: the parent approval task should be
            # available immediately (FindTask returns on the first poll well under 20s).
            $tpa = FindTask $sParent $i1 "pa"
            ChkTrue "S1: parent resumed to 'pa' (fast path advanced parent token)" ($null -ne $tpa)
            if ($tpa) {
                $rpa = Act $sParent $tpa.taskId $true "parent ok"
                Chk "S1: approve parent HTTP" 200 $rpa.Code
                Chk "S1: parent Approved (full chain complete)" $ST_APPROVED (GetStatus $sStart $i1)
            }
        }
    }
} else { Warn "S1 skipped (submit failed)" }

# -- Scenario 3 : multi-instance ALL + ordered array write-back --------------
Write-Host ""
Write-Host "-- Scenario 3 : multi-instance ALL, out-of-order finish, array write-back --" -ForegroundColor Cyan
$i3 = Submit $sStart $FK_ALL '{"subject":"s3","items":["itemA","itemB","itemC"]}'
if ($i3) {
    Write-Host "  Parent instance: $i3" -ForegroundColor Gray
    $kids3 = GetSubFlows $sStart $i3 3
    Chk "S3: 3 child instances spawned (one per collection element)" 3 $kids3.Count
    if ($kids3.Count -eq 3) {
        # Approve OUT OF ORDER: subIndex 2 first, then 0, then 1. Write-back must still be
        # ordered by SubIndex: results = ["itemA","itemB","itemC"].
        $ordered = $kids3 | Sort-Object { [int]$_.subIndex }
        foreach ($idx in @(2,0,1)) {
            $cid = "$($ordered[$idx].instanceId)"
            $t = FindTask $sChild $cid "ca"
            if ($t) { $null = Act $sChild $t.taskId $true "child $idx ok" }
            else    { Warn "S3: child subIndex=$idx task missing" }
        }
        Chk "S3: all children Approved -> parent resumed to 'pa'" $true ($null -ne (FindTask $sParent $i3 "pa"))
        $vars = GetVarsCompact $sStart $i3
        ChkContains "S3: parent vars carry write-back key 'results'" '"results"' $vars
        # order proof: itemA appears before itemB before itemC in the compacted vars.
        $iA = $vars.IndexOf("itemA"); $iB = $vars.IndexOf("itemB"); $iC = $vars.IndexOf("itemC")
        ChkTrue "S3: write-back array ordered by SubIndex (A<B<C, despite out-of-order finish)" (($iA -ge 0) -and ($iA -lt $iB) -and ($iB -lt $iC))
        ChkContains "S3: exact ordered array present" '["itemA","itemB","itemC"]' $vars
    }
} else { Warn "S3 skipped (submit failed)" }

# -- Scenario 4 : ALL, one child rejected -> cascade + parent Rejected -------
Write-Host ""
Write-Host "-- Scenario 4 : multi-instance ALL, reject one -> cascade + subFlowError --" -ForegroundColor Cyan
$i4 = Submit $sStart $FK_ALL '{"subject":"s4","items":["p","q","r"]}'
if ($i4) {
    Write-Host "  Parent instance: $i4" -ForegroundColor Gray
    $kids4 = GetSubFlows $sStart $i4 3
    Chk "S4: 3 child instances spawned" 3 $kids4.Count
    if ($kids4.Count -eq 3) {
        $ordered4 = $kids4 | Sort-Object { [int]$_.subIndex }
        $c4rej = "$($ordered4[1].instanceId)"   # reject the middle one
        $tr = FindTask $sChild $c4rej "ca"
        ChkTrue "S4: target child approval task present" ($null -ne $tr)
        if ($tr) {
            $rr = Act $sChild $tr.taskId $false "reject this child"
            Chk "S4: reject child HTTP" 200 $rr.Code
            Chk "S4: parent Rejected (all-policy dead branch, no error edge, no ForkId)" $ST_REJECTED (GetStatus $sStart $i4)
            ChkContains "S4: parent vars carry subFlowError" "subFlowError" (GetVarsCompact $sStart $i4)
            # the other two in-flight children were cascade-withdrawn.
            $after = GetSubFlows $sStart $i4 3
            $withdrawn = @($after | Where-Object { [int]$_.status -eq $ST_WITHDRAWN }).Count
            $rejected  = @($after | Where-Object { [int]$_.status -eq $ST_REJECTED }).Count
            Chk "S4: rejected child count = 1" 1 $rejected
            Chk "S4: other in-flight children cascade-Withdrawn = 2" 2 $withdrawn
        }
    }
} else { Warn "S4 skipped (submit failed)" }

# -- Scenario 5 : multi-instance ANY, first pass -> resume, rest withdrawn ----
Write-Host ""
Write-Host "-- Scenario 5 : multi-instance ANY, first approval resumes, rest withdrawn --" -ForegroundColor Cyan
$i5 = Submit $sStart $FK_ANY '{"subject":"s5","items":["x","y","z"]}'
if ($i5) {
    Write-Host "  Parent instance: $i5" -ForegroundColor Gray
    $kids5 = GetSubFlows $sStart $i5 3
    Chk "S5: 3 child instances spawned" 3 $kids5.Count
    if ($kids5.Count -eq 3) {
        $ordered5 = $kids5 | Sort-Object { [int]$_.subIndex }
        $c5ok = "$($ordered5[0].instanceId)"
        $to = FindTask $sChild $c5ok "ca"
        ChkTrue "S5: first child approval task present" ($null -ne $to)
        if ($to) {
            $ro = Act $sChild $to.taskId $true "first child approves"
            Chk "S5: approve first child HTTP" 200 $ro.Code
            Chk "S5: parent resumed to 'pa' (any first-pass)" $true ($null -ne (FindTask $sParent $i5 "pa"))
            Chk "S5: parent still Running (resumed, not terminal)" $ST_RUNNING (GetStatus $sStart $i5)
            $after5 = GetSubFlows $sStart $i5 3
            $approved5  = @($after5 | Where-Object { [int]$_.status -eq $ST_APPROVED }).Count
            $withdrawn5 = @($after5 | Where-Object { [int]$_.status -eq $ST_WITHDRAWN }).Count
            Chk "S5: exactly 1 child Approved" 1 $approved5
            Chk "S5: the other 2 children cascade-Withdrawn" 2 $withdrawn5
            # finish the parent
            $tpa5 = FindTask $sParent $i5 "pa"
            if ($tpa5) { $null = Act $sParent $tpa5.taskId $true "parent ok"; Chk "S5: parent Approved after 'pa'" $ST_APPROVED (GetStatus $sStart $i5) }
        }
    }
} else { Warn "S5 skipped (submit failed)" }

# -- Scenario 6 : parallel prune composition (sub-flow branch pruned, sibling survives) --
Write-Host ""
Write-Host "-- Scenario 6 : parallel onBranchReject=prune + sub-flow branch --" -ForegroundColor Cyan
$i6 = Submit $sStart $FK_COMBO '{"subject":"s6"}'
if ($i6) {
    Write-Host "  Parent instance: $i6" -ForegroundColor Gray
    $kids6 = GetSubFlows $sStart $i6 1
    Chk "S6: sub-flow branch spawned 1 child" 1 $kids6.Count
    $tb6 = FindTask $sB $i6 "bAppr"
    ChkTrue "S6: sibling branch B task present" ($null -ne $tb6)
    if ($kids6.Count -ge 1) {
        $c6 = "$($kids6[0].instanceId)"
        $tc6 = FindTask $sChild $c6 "ca"
        ChkTrue "S6: sub-flow child approval task present" ($null -ne $tc6)
        if ($tc6) {
            $rc6 = Act $sChild $tc6.taskId $false "reject sub-flow child"
            Chk "S6: reject sub-flow child HTTP" 200 $rc6.Code
            Chk "S6: instance STAYS Running (prune only that branch, no cascade)" $ST_RUNNING (GetStatus $sStart $i6)
            ChkTrue "S6: sibling branch B task still alive after prune" ($null -ne (FindTask $sB $i6 "bAppr"))
            $tb6b = FindTask $sB $i6 "bAppr"
            if ($tb6b) {
                $rb6 = Act $sB $tb6b.taskId $true "branch B ok"
                Chk "S6: approve branch B HTTP" 200 $rb6.Code
                Chk "S6: join dyn-counts (pruned branch drops) -> Approved" $ST_APPROVED (GetStatus $sStart $i6)
            }
        }
    }
} else { Warn "S6 skipped (submit failed)" }

# -- Scenario 8 : withdraw child -> parent resumes via WithdrawAsync fast path (B-T3) --
Write-Host ""
Write-Host "-- Scenario 8 : withdraw child instance -> parent fast-path (B-T3 DI proof) --" -ForegroundColor Cyan
$i8 = Submit $sStart $FK_SINGLE '{"subject":"s8"}'
if ($i8) {
    Write-Host "  Parent instance: $i8" -ForegroundColor Gray
    $kids8 = GetSubFlows $sStart $i8 1
    Chk "S8: 1 child instance spawned (parked at ca)" 1 $kids8.Count
    if ($kids8.Count -ge 1) {
        $c8 = "$($kids8[0].instanceId)"
        $w = Withdraw $sStart $c8   # starter withdraws the CHILD instance
        if ($w.Code -eq 200) {
            Chk "S8: withdraw child HTTP" 200 $w.Code
            Chk "S8: child instance Withdrawn" $ST_WITHDRAWN (GetStatus $sStart $c8)
            # WithdrawAsync enqueued the resume credential AND ran the injected FlowEngine
            # fast path in the same request -> parent already Rejected (all-policy, dead child).
            Chk "S8: parent Rejected via withdraw fast path (scoped FlowEngine DI)" $ST_REJECTED (GetStatus $sStart $i8)
            ChkContains "S8: parent vars carry subFlowError from the withdrawn child" "subFlowError" (GetVarsCompact $sStart $i8)
        } elseif ($w.Code -eq 403) {
            Warn "S8: withdraw returned 403 -- role 1 lacks oa-inbox:withdraw. Grant it (README 4.2) and re-run S8."
        } else {
            Chk "S8: withdraw child HTTP" 200 $w.Code
        }
    }
} else { Warn "S8 skipped (submit failed)" }

# -- Summary -----------------------------------------------------------------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "RESULTS: PASS=$PASS   FAIL=$FAIL   WARN=$WARN" -ForegroundColor $(if ($FAIL -gt 0) { "Red" } elseif ($WARN -gt 0) { "Yellow" } else { "Green" })
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "SCENARIO 2 (parent/child interlink) and SCENARIO 7 (designer real browser) are MANUAL -- see README sections 5.1 / 5.2."
Write-Host "Scenario-1 worker-fallback drill (stop-worker posture) is a MANUAL DB drill -- see README section 4.1."
Write-Host ""
Write-Host "MANUAL DB CHECKS (sqlcmd -S localhost\KOUSQLSERVER -d CP6DB_OA -E -C):"
Write-Host "  -- child instances of a parent (three back-pointer columns):"
Write-Host "  SELECT Id, ParentInstanceId, ParentTokenId, SubIndex, Status FROM Wf_FlowInstance WHERE ParentInstanceId IS NOT NULL ORDER BY CreateDate DESC;"
Write-Host "  -- subFlowResume credentials (TokenId = child instance Id, NodeId = '\$subFlowResume'):"
Write-Host "  SELECT TokenId, NodeId, Kind, Status, LockedBy, CompletedAtUtc FROM Wf_ServiceJob WHERE NodeId = '\$subFlowResume' ORDER BY CreateDate DESC;"
Write-Host "  -- subFlowError / subFlowResumed / subFlowCascadeCancelled history:"
Write-Host "  SELECT InstanceId, NodeId, Action, Comment FROM Wf_FlowHistory WHERE Action LIKE 'subFlow%' ORDER BY CreateDate DESC;"
Write-Host ""
if ($FAIL -gt 0) { exit 1 } else { exit 0 }
