#Requires -Version 5.1
<#
.SYNOPSIS
    WFS Service Task (E-T3) -- HTTP e2e QA script (scenarios 1-6).

.DESCRIPTION
    Exercises all 6 service-task scenarios against a RUNNING backend. Does NOT
    start the server. Apply seed.sql first, then run the backend against the
    isolated CP6DB_OA database:

        sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i seed.sql

        cd <repo>\CP6.WebApi
        $env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
        dotnet run --urls "http://localhost:5180"

    Then:
        .\qa_service_task.ps1
        .\qa_service_task.ps1 -BaseUrl http://localhost:5180 -WaitSeconds 90

    IMPORTANT: the WfServiceJobScanWorker scans every 20s and timer flows use a
    10s duration, so async scenarios (2-6) can take up to ~30-40s to settle.
    -WaitSeconds bounds the poll loop; bump it on a slow box.

.NOTES
    PS5.1 compatibility (mirrors wfs-serial-signing/qa_serial.ps1):
      - No && operator; sequential steps use explicit checks.
      - Invoke-RestMethod throws WebException on non-2xx; body read via Read400Body.
      - All request bodies use ASCII-only values (no CJK).
      - JSON bodies built via ConvertTo-Json.
      - CSRF disabled in dev (Security:Csrf:Enabled=false); JWT cp6_at cookie
        flows automatically via -SessionVariable / -WebSession.

    Envelope shape:
      - All responses: { code: 0, message: "OK", data: <payload> }
      - Submit:  $r.data.instanceId
      - Pending: $r.data  (array of InboxPendingItem, item.instanceId)
      - Detail:  $r.data  (InboxDetail: .instance.status, .instance.varsJson,
                 .currentDataJson  -- both carry the merged flow variables)

    Instance status codes (FlowInstanceStatus):
      0 Running   1 Approved   2 Rejected   3 Withdrawn   4 Suspended   5 Draft
#>

param(
    [string]$BaseUrl     = "http://localhost:5180",
    [int]   $WaitSeconds = 90,
    [int]   $PollSeconds = 5
)

$ErrorActionPreference = "Continue"
$PASS = 0; $FAIL = 0; $WARN = 0

# ── FlowKeys from seed.sql ──────────────────────────────────────────────────
$FK_SYNC   = "svc-sync-writeback"
$FK_ASYNC  = "svc-async-webapi"
$FK_TWAIT  = "svc-timer-wait"
$FK_TACT   = "svc-timer-action"
$FK_FERR   = "svc-fail-erroredge"
$FK_FSUS   = "svc-fail-suspend"

# Status codes
$ST_RUNNING   = 0
$ST_APPROVED  = 1
$ST_SUSPENDED = 4

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

# Submit a flow with a given varsJson string; returns instanceId or $null.
function Submit {
    param($Session, [string]$FlowKey, [string]$VarsJson = "{}")
    $r = PostJson "$BaseUrl/api/wf/flow/submit" @{ flowKey = $FlowKey; varsJson = $VarsJson; bizType = $null; bizId = $null } $Session
    Chk "Submit $FlowKey HTTP" 200 $r.Code
    if ($r.Code -eq 200) {
        $id = $r.Data.data.instanceId
        if (-not $id) { $id = $r.Data.instanceId }
        return "$id"
    }
    Write-Host "  Submit error: $($r.Raw)" -ForegroundColor Red
    return $null
}

# Get InboxDetail data object (or $null). Contains .instance.status + .currentDataJson.
function GetDetail {
    param($Session, [string]$InstanceId)
    $r = GetJson "$BaseUrl/api/oa/inbox/detail/$InstanceId" $Session
    if ($r.Code -eq 200) { return $r.Data.data }
    return $null
}

# Poll detail until instance.status == TargetStatus, or WaitSeconds elapse.
# Returns the final detail object (may still be non-target on timeout).
function WaitForStatus {
    param($Session, [string]$InstanceId, [int]$TargetStatus)
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    $detail = $null
    while ((Get-Date) -lt $deadline) {
        $detail = GetDetail $Session $InstanceId
        if ($null -ne $detail -and [int]$detail.instance.status -eq $TargetStatus) { return $detail }
        Start-Sleep -Seconds $PollSeconds
    }
    return $detail
}

# Poll a user's pending inbox until an item for InstanceId appears, or timeout.
# Returns the matching pending item (or $null).
function WaitForPending {
    param($Session, [string]$InstanceId)
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        $r = GetJson "$BaseUrl/api/oa/inbox/pending" $Session
        if ($r.Code -eq 200) {
            $items = $r.Data.data
            if ($null -ne $items) {
                $hit = @($items | Where-Object { "$($_.instanceId)" -eq "$InstanceId" }) | Select-Object -First 1
                if ($hit) { return $hit }
            }
        }
        Start-Sleep -Seconds $PollSeconds
    }
    return $null
}

# Pull the flow-variable JSON blob out of a detail object (two field names cover
# both wrapper depths / naming); returns "" if absent.
function VarsOf {
    param($Detail)
    if ($null -eq $Detail) { return "" }
    $v = $Detail.currentDataJson
    if (-not $v) { $v = $Detail.instance.varsJson }
    return "$v"
}

# ── Preflight: login the two seeded users ───────────────────────────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "WFS Service Task QA  ($BaseUrl)   wait<=${WaitSeconds}s poll=${PollSeconds}s" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

$sess = Login "qa_svc_starter"
if (-not $sess) { Write-Host "ABORT: cannot login as qa_svc_starter (seed applied?)" -ForegroundColor Red; exit 1 }
$sessAppr = Login "qa_svc_appr"
if (-not $sessAppr) { Warn "cannot login qa_svc_appr; scenario 5 pending check will be skipped" }

# ── Scenario 1 : sync dataWriteback -> Approved immediately ─────────────────
Write-Host ""
Write-Host "-- Scenario 1 : sync dataWriteback (sampleWriteback) --" -ForegroundColor Cyan
$i1 = Submit $sess $FK_SYNC '{"subject":"s1","amount":100}'
if ($i1) {
    Write-Host "  Instance: $i1" -ForegroundColor Gray
    $d1 = GetDetail $sess $i1     # sync path finishes within the submit call
    Chk "S1: instance Approved immediately" $ST_APPROVED ([int]$d1.instance.status)
    ChkContains "S1: VarsJson has writebackEcho" "writebackEcho" (VarsOf $d1)
} else { Warn "S1 skipped (submit failed)" }

# ── Scenario 2 : async webApi (erpEcho) -> job -> worker -> Approved ─────────
Write-Host ""
Write-Host "-- Scenario 2 : async webApi (erpEcho) --" -ForegroundColor Cyan
$i2 = Submit $sess $FK_ASYNC '{"subject":"s2","amount":42}'
if ($i2) {
    Write-Host "  Instance: $i2" -ForegroundColor Gray
    $d2early = GetDetail $sess $i2
    Chk "S2: Running before worker picks up job" $ST_RUNNING ([int]$d2early.instance.status)
    Write-Host "  waiting for WfServiceJobScanWorker (<=${WaitSeconds}s)..." -ForegroundColor Gray
    $d2 = WaitForStatus $sess $i2 $ST_APPROVED
    Chk "S2: instance Approved after worker scan" $ST_APPROVED ([int]$d2.instance.status)
    ChkContains "S2: VarsJson has echoedPath" "echoedPath" (VarsOf $d2)
} else { Warn "S2 skipped (submit failed)" }

# ── Scenario 3 : timer pure wait (PT10S) -> advance -> Approved ─────────────
Write-Host ""
Write-Host "-- Scenario 3 : timer pure wait (PT10S, none) --" -ForegroundColor Cyan
$i3 = Submit $sess $FK_TWAIT '{"subject":"s3","amount":1}'
if ($i3) {
    Write-Host "  Instance: $i3" -ForegroundColor Gray
    $d3early = GetDetail $sess $i3
    Chk "S3: Running while timer pending" $ST_RUNNING ([int]$d3early.instance.status)
    Write-Host "  waiting for timer due + worker scan (<=${WaitSeconds}s)..." -ForegroundColor Gray
    $d3 = WaitForStatus $sess $i3 $ST_APPROVED
    Chk "S3: instance Approved after timer fires" $ST_APPROVED ([int]$d3.instance.status)
} else { Warn "S3 skipped (submit failed)" }

# ── Scenario 4 : timer + webApi action (erpEcho) at due time ────────────────
Write-Host ""
Write-Host "-- Scenario 4 : timer + webApi action (PT10S + erpEcho) --" -ForegroundColor Cyan
$i4 = Submit $sess $FK_TACT '{"subject":"s4","amount":7}'
if ($i4) {
    Write-Host "  Instance: $i4" -ForegroundColor Gray
    Write-Host "  waiting for timer due + worker scan (<=${WaitSeconds}s)..." -ForegroundColor Gray
    $d4 = WaitForStatus $sess $i4 $ST_APPROVED
    Chk "S4: instance Approved after timer action" $ST_APPROVED ([int]$d4.instance.status)
    ChkContains "S4: VarsJson has echoedPath (action ran)" "echoedPath" (VarsOf $d4)
} else { Warn "S4 skipped (submit failed)" }

# ── Scenario 5 : fail -> retry exhausted -> IsError edge -> human node ───────
Write-Host ""
Write-Host "-- Scenario 5 : fail -> error edge -> human (qa_svc_appr) --" -ForegroundColor Cyan
$i5 = Submit $sess $FK_FERR '{"subject":"s5","amount":9}'
if ($i5) {
    Write-Host "  Instance: $i5" -ForegroundColor Gray
    Write-Host "  waiting for fail-route to human node (<=${WaitSeconds}s)..." -ForegroundColor Gray
    if ($sessAppr) {
        $p5 = WaitForPending $sessAppr $i5
        Chk "S5: qa_svc_appr has a pending task (routed to error edge)" $true ($null -ne $p5)
    } else {
        Warn "S5: appr session missing; cannot verify pending task"
    }
    $d5 = GetDetail $sess $i5
    Chk "S5: instance still Running (not suspended -- error edge exists)" $ST_RUNNING ([int]$d5.instance.status)
    ChkContains "S5: VarsJson has wf.serviceError" "serviceError" (VarsOf $d5)
} else { Warn "S5 skipped (submit failed)" }

# ── Scenario 6 : fail -> retry exhausted -> no error edge -> Suspend ────────
Write-Host ""
Write-Host "-- Scenario 6 : fail -> suspend (no error edge) --" -ForegroundColor Cyan
$i6 = Submit $sess $FK_FSUS '{"subject":"s6","amount":9}'
if ($i6) {
    Write-Host "  Instance: $i6" -ForegroundColor Gray
    Write-Host "  waiting for fail-route to suspend (<=${WaitSeconds}s)..." -ForegroundColor Gray
    $d6 = WaitForStatus $sess $i6 $ST_SUSPENDED
    Chk "S6: instance Suspended after fail with no error edge" $ST_SUSPENDED ([int]$d6.instance.status)
    ChkContains "S6: VarsJson has wf.serviceError" "serviceError" (VarsOf $d6)
} else { Warn "S6 skipped (submit failed)" }

# ── Summary ─────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "RESULTS: PASS=$PASS   FAIL=$FAIL   WARN=$WARN" -ForegroundColor $(if ($FAIL -gt 0) { "Red" } elseif ($WARN -gt 0) { "Yellow" } else { "Green" })
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "MANUAL DB CHECKS (sqlcmd -S localhost\KOUSQLSERVER -d CP6DB_OA -E -C):"
Write-Host "  -- Wf_ServiceJob lifecycle per instance (Status 2=Succeeded,3=Failed,4=Cancelled):"
Write-Host "  SELECT InstanceId, NodeId, Kind, Status, AttemptCount, MaxAttempts, DueAtUtc, LastError"
Write-Host "  FROM Wf_ServiceJob ORDER BY CreateDate;"
Write-Host ""
Write-Host "  -- Scenario 5/6 error variable (wf.serviceError injected into VarsJson):"
Write-Host "  SELECT Id, Status, VarsJson FROM Wf_FlowInstance"
Write-Host "  WHERE FlowKey IN ('svc-fail-erroredge','svc-fail-suspend') ORDER BY CreateDate DESC;"
Write-Host ""
Write-Host "  -- Scenario 5 error-branch human token parked at h1:"
Write-Host "  SELECT t.InstanceId, t.NodeId, t.Status FROM Wf_FlowToken t"
Write-Host "  JOIN Wf_FlowInstance i ON i.Id = t.InstanceId WHERE i.FlowKey = 'svc-fail-erroredge';"
Write-Host ""
if ($FAIL -gt 0) { exit 1 } else { exit 0 }
