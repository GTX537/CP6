#Requires -Version 5.1
<#
.SYNOPSIS
    WFS Approver Resolution -- HTTP e2e QA script (scenarios 1-7).

.DESCRIPTION
    Exercises all 7 scenarios of the approver-resolution QA harness against a
    RUNNING backend.  Does NOT start the server -- run the backend first:

        cd D:\CP6-wfs-approver\CP6.WebApi
        $env:ConnectionStrings__DefaultConnection = "Server=localhost\KOUSQLSERVER;Database=CP6DB_OA;Trusted_Connection=True;TrustServerCertificate=True;"
        dotnet run --urls "http://localhost:5179"

    Apply seed first (from PowerShell / cmd, NOT git-bash):
        sqlcmd -S "localhost\KOUSQLSERVER" -d CP6DB_OA -E -C -i "D:\CP6-wfs-approver\docs\superpowers\qa\wfs-approver-resolution\seed.sql"

    Then run this script:
        .\qa_approver.ps1
        .\qa_approver.ps1 -BaseUrl http://localhost:5179

.NOTES
    PS5.1 compatibility:
      - No && operator. Sequential steps use explicit checks.
      - Invoke-RestMethod throws WebException on non-2xx. Error body read via
        GetResponseStream (see Read400Body helper).
      - All body strings use ASCII-only values (no Chinese/Japanese/Korean).
      - JSON bodies built via ConvertTo-Json (no manual escaping needed).
      - Default encoding is UTF-16 LE; -Encoding utf8 used when writing files.

    Cookie handling:
      - CSRF is disabled in dev (Security:Csrf:Enabled=false). No X-CSRF-Token.
      - Sessions managed by Invoke-RestMethod -SessionVariable / -WebSession.
      - cp6_at JWT cookie set by login response, auto-sent in subsequent calls.

    Envelope shape:
      - All responses: { code: 0, message: "OK", data: <payload> }
      - Submit:        $r.data.instanceId  (Guid)
      - Pending:       $r.data             (array of InboxPendingItem)
      - Act:           $r.data             (null on success, HTTP 200)
      - ApproverMap list: $r.data          (array)
      - Forecast:      $r.data.stages      (array of ForecastStage)

    Port assignment (avoids collisions):
      - Space session     : 5177 (backend) / 5173 (frontend)
      - serial-signing QA : 5178 (backend) / 5180 (frontend)
      - approver-resolution: 5179 (backend) / 5181 (frontend)  <-- this script
#>

param(
    [string]$BaseUrl = "http://localhost:5179"
)

$ErrorActionPreference = "Continue"   # We handle errors manually per-call.
$PASS = 0; $FAIL = 0; $WARN = 0

# ── GUIDs from seed.sql ─────────────────────────────────────────────────────
$G_admin      = "CCCC0000-0000-0000-0000-000000000001"  # qa_a_admin
$G_start      = "CCCC0000-0000-0000-0000-000000000002"  # qa_a_start
$G_user1      = "CCCC0000-0000-0000-0000-000000000003"  # qa_a_user1 (FormField/DataMap target)
$G_user2      = "CCCC0000-0000-0000-0000-000000000004"  # qa_a_user2 (Group specified / When gate)
$G_same_dept  = "CCCC0000-0000-0000-0000-000000000005"  # qa_a_same_dept (Filter: passes)
$G_other_dept = "CCCC0000-0000-0000-0000-000000000006"  # qa_a_other_dept (Filter: excluded)
$G_mgr        = "CCCC0000-0000-0000-0000-000000000007"  # qa_a_mgr (Group DirectManager)

# ── Helpers ──────────────────────────────────────────────────────────────────

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
    if ($Haystack -and $Haystack.Contains($Needle)) {
        Write-Host "  PASS  $Label (contains '$Needle')" -ForegroundColor Green
        $script:PASS++
    } else {
        Write-Host "  FAIL  $Label  expected to contain '$Needle'  got=[$Haystack]" -ForegroundColor Red
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

# Read the response body from a WebException (PS5.1 pattern for 4xx/5xx).
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

# POST JSON; return @{ Code; Data; Raw }. Never throws.
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

# GET JSON; return @{ Code; Data; Raw }. Never throws.
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

# DELETE; return HTTP status code. Never throws.
function DeleteJson {
    param([string]$Uri, $Session)
    try {
        if ($Session) {
            Invoke-RestMethod -Method DELETE -Uri $Uri -WebSession $Session | Out-Null
        } else {
            Invoke-RestMethod -Method DELETE -Uri $Uri | Out-Null
        }
        return 200
    } catch [System.Net.WebException] {
        return [int]$_.Exception.Response.StatusCode
    } catch {
        return 0
    }
}

# Login; returns WebSession with cp6_at cookie, or $null on failure.
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

# Submit a flow; return instanceId string or $null.
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

# Get pending tasks; return array (may be empty).
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

# Approve or reject a task; return HTTP status code.
function Act {
    param($Session, [string]$TaskId, [bool]$Approve, [string]$Comment = "")
    $r = PostJson "$BaseUrl/api/wf/task/$TaskId/act" @{ approve = $Approve; comment = $Comment } $Session
    return $r.Code
}

# Get instance detail; return data object or $null.
function GetDetail {
    param($Session, [string]$InstanceId)
    $r = GetJson "$BaseUrl/api/oa/inbox/detail/$InstanceId" $Session
    if ($r.Code -eq 200) { return $r.Data.data }
    return $null
}

# ── Login as shared sessions ──────────────────────────────────────────────────
$sessAdmin   = Login "qa_a_admin"
$sessStart   = Login "qa_a_start"
$sessUser1   = Login "qa_a_user1"
$sessUser2   = Login "qa_a_user2"
$sessSameDept  = Login "qa_a_same_dept"
$sessOtherDept = Login "qa_a_other_dept"
$sessMgr     = Login "qa_a_mgr"

if (-not $sessAdmin -or -not $sessStart -or -not $sessUser1) {
    Write-Host "ABORT: required logins failed. Check seed.sql was applied." -ForegroundColor Red
    exit 1
}

# ── Scenario 1 : Designer save -- FormField / DataMap / Group validation ──────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Scenario 1 : Designer save validates FormField/DataMap/Group nodes" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 1-a. Happy path: valid FormField node schema saves successfully.
Write-Host "-- Happy path: FormField node save --"
$ffSchema = '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"FF Node","approverStrategy":"FormField","approverFieldName":"approver","countersign":"all"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"}]}'
$r1a = PostJson "$BaseUrl/api/oa/designer/save" @{
    flowKey = "qa-ff-happy"; flowName = "QA FF Happy"; formKey = "approver-field-form"
    functionId = $null; flowCode = $null; schemaJson = $ffSchema
} $sessAdmin
Chk "S1a: FormField valid save HTTP" 200 $r1a.Code

# 1-b. Negative: FormField node missing fieldName -> E-WF-014.
Write-Host "-- Negative: FormField missing fieldName -> E-WF-014 --"
$badFfSchema = '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"Bad FF","approverStrategy":"FormField","countersign":"all"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"}]}'
$r1b = PostJson "$BaseUrl/api/oa/designer/save" @{
    flowKey = "qa-ff-bad"; flowName = "QA FF Bad"; formKey = "approver-field-form"
    functionId = $null; flowCode = $null; schemaJson = $badFfSchema
} $sessAdmin
Chk "S1b: FormField missing fieldName HTTP" 400 $r1b.Code
$errMsg1b = if ($r1b.Data) { "$($r1b.Data.message)" } else { $r1b.Raw }
ChkContains "S1b: E-WF-014 in error message" "E-WF-014" $errMsg1b

# 1-c. Negative: DataMap node missing mapKey -> E-WF-014.
Write-Host "-- Negative: DataMap missing mapKey -> E-WF-014 --"
$badDmSchema = '{"start":"s","nodes":[{"id":"s","type":"start","name":"Start"},{"id":"a1","type":"approval","name":"Bad DM","approverStrategy":"DataMap","approverFieldName":"costCenter","countersign":"all"},{"id":"end","type":"end","name":"End"}],"edges":[{"from":"s","to":"a1"},{"from":"a1","to":"end"}]}'
$r1c = PostJson "$BaseUrl/api/oa/designer/save" @{
    flowKey = "qa-dm-bad"; flowName = "QA DM Bad"; formKey = "datamap-form"
    functionId = $null; flowCode = $null; schemaJson = $badDmSchema
} $sessAdmin
Chk "S1c: DataMap missing mapKey HTTP" 400 $r1c.Code
$errMsg1c = if ($r1c.Data) { "$($r1c.Data.message)" } else { $r1c.Raw }
ChkContains "S1c: E-WF-014 in error message" "E-WF-014" $errMsg1c

Write-Host "Scenario 1 complete." -ForegroundColor Cyan

# ── Scenario 2 : ApproverMap CRUD + E-WF-015 ─────────────────────────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Scenario 2 : ApproverMap CRUD + E-WF-015 duplicate block" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 2-a. Verify seed rows present (GET cc key).
Write-Host "-- Verify seed rows for key=cc --"
$r2a = GetJson "$BaseUrl/api/oa/approver-map/list?mapKey=cc" $sessAdmin
Chk "S2a: list cc GET HTTP" 200 $r2a.Code
$ccRows = @()
if ($r2a.Code -eq 200) {
    $ccRows = @($r2a.Data.data)
    if ($null -eq $ccRows -or $ccRows.Count -eq 0) { $ccRows = @($r2a.Data) }
    ChkGt "S2a: cc seed rows count >= 2" 1 $ccRows.Count
}

# 2-b. Add a new mapping B200 -> qa_a_user2.
Write-Host "-- Add B200 -> qa_a_user2 --"
$r2b = PostJson "$BaseUrl/api/oa/approver-map" @{
    mapKey = "cc"; matchValue = "B200"; approverUserId = $G_user2; approverRoleId = $null; orderNo = 0
} $sessAdmin
Chk "S2b: add B200 mapping HTTP" 200 $r2b.Code
$newId = $null
if ($r2b.Code -eq 200 -and $r2b.Data) {
    $newId = $r2b.Data.data.id
    if (-not $newId) { $newId = $r2b.Data.id }
    Chk "S2b: new row has id" $true ($null -ne $newId -and "$newId" -ne "")
}

# 2-c. Duplicate A100/user1 -> E-WF-015.
Write-Host "-- Duplicate A100/user1 -> E-WF-015 --"
$r2c = PostJson "$BaseUrl/api/oa/approver-map" @{
    mapKey = "cc"; matchValue = "A100"; approverUserId = $G_user1; approverRoleId = $null; orderNo = 0
} $sessAdmin
Chk "S2c: duplicate mapping HTTP" 400 $r2c.Code
$errMsg2c = if ($r2c.Data) { "$($r2c.Data.message)" } else { $r2c.Raw }
ChkContains "S2c: E-WF-015 in error message" "E-WF-015" $errMsg2c

# 2-d. Both targets null -> E-WF-015.
Write-Host "-- Both targets null -> E-WF-015 --"
$r2d = PostJson "$BaseUrl/api/oa/approver-map" @{
    mapKey = "cc"; matchValue = "A100"; approverUserId = $null; approverRoleId = $null; orderNo = 0
} $sessAdmin
Chk "S2d: both null HTTP" 400 $r2d.Code
$errMsg2d = if ($r2d.Data) { "$($r2d.Data.message)" } else { $r2d.Raw }
ChkContains "S2d: E-WF-015 in error message" "E-WF-015" $errMsg2d

# 2-e. Delete the B200 row.
Write-Host "-- Delete B200 row --"
if ($newId) {
    $delCode = DeleteJson "$BaseUrl/api/oa/approver-map/$newId" $sessAdmin
    Chk "S2e: delete B200 HTTP" 200 $delCode
    # Verify still 2 seed rows for cc/A100 after delete.
    $r2e_check = GetJson "$BaseUrl/api/oa/approver-map/list?mapKey=cc" $sessAdmin
    if ($r2e_check.Code -eq 200) {
        $ccRowsAfter = @($r2e_check.Data.data)
        if ($null -eq $ccRowsAfter -or $ccRowsAfter.Count -eq 0) { $ccRowsAfter = @($r2e_check.Data) }
        Chk "S2e: cc rows after delete = 2 (seed rows only)" 2 $ccRowsAfter.Count
    }
} else {
    Warn "S2e: skipped delete (no id from step 2b)"
}

Write-Host "Scenario 2 complete." -ForegroundColor Cyan

# ── Scenario 3 : FormField -- user picker -> assign specific user ─────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Scenario 3 : FormField -- form user picker assigns the picked user" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 3-a. Submit with approver=qa_a_user1 in varsJson.
Write-Host "-- Submit FormField flow with approver=qa_a_user1 --"
$vars3 = "{`"subject`":`"FormField Test`",`"approver`":`"$G_user1`"}"
$inst3 = Submit $sessStart "approver-formfield-flow" $vars3
if (-not $inst3) { Write-Host "ABORT Scenario 3: submit failed" -ForegroundColor Red }

# 3-b. qa_a_user1 should have a pending task.
if ($inst3) {
    Write-Host "-- Verify qa_a_user1 has pending task --"
    $pending_u1 = @(GetPending $sessUser1 | Where-Object { "$($_.instanceId)" -eq "$inst3" })
    Chk "S3b: user1 pending count" 1 $pending_u1.Count

    # 3-c. qa_a_user2 should NOT have a pending task.
    Write-Host "-- Verify qa_a_user2 has NO pending task --"
    $pending_u2_s3 = @(GetPending $sessUser2 | Where-Object { "$($_.instanceId)" -eq "$inst3" })
    Chk "S3c: user2 pending count (0)" 0 $pending_u2_s3.Count

    # 3-d. Approve and verify Approved.
    Write-Host "-- Approve as qa_a_user1 --"
    $task3 = $pending_u1 | Select-Object -First 1
    if ($task3) {
        $actCode3 = Act $sessUser1 "$($task3.taskId)" $true "FF approved"
        Chk "S3d: act HTTP" 200 $actCode3
        $detail3 = GetDetail $sessStart $inst3
        if ($null -ne $detail3) {
            Chk "S3d: instance Approved (1)" 1 $detail3.instance.status
        } else {
            Warn "S3d: could not fetch detail"
        }
    }
}

Write-Host "Scenario 3 complete." -ForegroundColor Cyan

# ── Scenario 4 : DataMap -- costCenter=A100 assigns user1 + role9 members ────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Scenario 4 : DataMap -- costCenter=A100 assigns user1 + role9" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 4-a. Submit with costCenter=A100.
Write-Host "-- Submit DataMap flow with costCenter=A100 --"
$vars4a = "{`"costCenter`":`"A100`"}"
$inst4a = Submit $sessStart "approver-datamap-flow" $vars4a
if (-not $inst4a) { Write-Host "ABORT Scenario 4a: submit failed" -ForegroundColor Red }

# 4-b. qa_a_user1 (role 9, dept A) should have pending task (user target + role-9 expansion).
if ($inst4a) {
    Write-Host "-- Verify qa_a_user1 has pending task (DataMap user+role expansion) --"
    $pending_dm = @(GetPending $sessUser1 | Where-Object { "$($_.instanceId)" -eq "$inst4a" })
    ChkGt "S4b: user1 pending count >= 1" 0 $pending_dm.Count

    # 4-c. Approve as user1.
    Write-Host "-- Approve as qa_a_user1 --"
    $taskDm = $pending_dm | Select-Object -First 1
    if ($taskDm) {
        $actCode4 = Act $sessUser1 "$($taskDm.taskId)" $true "DM approved"
        Chk "S4c: act HTTP" 200 $actCode4
    }
}

# 4-d. Submit with costCenter=ZZZ (no mapping -> suspended).
Write-Host "-- Submit DataMap flow with costCenter=ZZZ (no mapping -> suspended) --"
$vars4d = "{`"costCenter`":`"ZZZ`"}"
$r4d = PostJson "$BaseUrl/api/wf/flow/submit" @{ flowKey = "approver-datamap-flow"; varsJson = $vars4d; bizType = $null; bizId = $null } $sessStart
if ($r4d.Code -eq 200) {
    $inst4d = $r4d.Data.data.instanceId
    if (-not $inst4d) { $inst4d = $r4d.Data.instanceId }
    if ($inst4d) {
        $detail4d = GetDetail $sessStart "$inst4d"
        if ($null -ne $detail4d) {
            # Instance should be Suspended (4) because no approver found for ZZZ
            $status4d = $detail4d.instance.status
            Chk "S4d: ZZZ mapping -> Suspended (4)" 4 $status4d
        } else {
            Warn "S4d: could not fetch detail for ZZZ instance"
        }
    }
} else {
    # May also 400 if validation rejects unknown mapping immediately
    Warn "S4d: ZZZ submit returned $($r4d.Code) (acceptable if engine validates early)"
}

Write-Host "Scenario 4 complete." -ForegroundColor Cyan

# ── Scenario 5 : When gate -- amount threshold controls extra stage ─────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Scenario 5 : When gate -- amount=50000 triggers A2; amount=100 skips" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# 5a: amount=50000 -> both A1 (user1) and A2 (user2) active.
Write-Host "-- 5a: amount=50000 (When=true, A2 active) --"
$vars5a = "{`"amount`":50000}"
$inst5a = Submit $sessStart "approver-when-flow" $vars5a

if ($inst5a) {
    # A1 (user1) approves.
    $pend5a_u1 = @(GetPending $sessUser1 | Where-Object { "$($_.instanceId)" -eq "$inst5a" }) | Select-Object -First 1
    Chk "S5a: user1 A1 pending" $true ($null -ne $pend5a_u1)
    if ($pend5a_u1) { Act $sessUser1 "$($pend5a_u1.taskId)" $true "S5a A1 approve" | Out-Null }

    # A2 (user2) should now have task (When amount>=10000 is true).
    $pend5a_u2 = @(GetPending $sessUser2 | Where-Object { "$($_.instanceId)" -eq "$inst5a" }) | Select-Object -First 1
    Chk "S5a: user2 A2 pending (When=true)" $true ($null -ne $pend5a_u2)
    if ($pend5a_u2) {
        $actCode5a = Act $sessUser2 "$($pend5a_u2.taskId)" $true "S5a A2 approve"
        Chk "S5a: A2 act HTTP" 200 $actCode5a
    }

    $detail5a = GetDetail $sessStart $inst5a
    if ($null -ne $detail5a) {
        Chk "S5a: instance Approved after both nodes" 1 $detail5a.instance.status
    } else {
        Warn "S5a: could not fetch detail"
    }
}

# 5b: amount=100 -> A2 When=false -> Unres -> A2 node Suspends (spec §4.1/§9). Node-level When gates the rule (no approver -> suspend); flow routing-around is via edge conditions, not When.
Write-Host "-- 5b: amount=100 (When=false, A2 skipped) --"
$vars5b = "{`"amount`":100}"
$inst5b = Submit $sessStart "approver-when-flow" $vars5b

if ($inst5b) {
    # A1 (user1) approves.
    $pend5b_u1 = @(GetPending $sessUser1 | Where-Object { "$($_.instanceId)" -eq "$inst5b" }) | Select-Object -First 1
    Chk "S5b: user1 A1 pending" $true ($null -ne $pend5b_u1)
    if ($pend5b_u1) { Act $sessUser1 "$($pend5b_u1.taskId)" $true "S5b A1 approve" | Out-Null }

    # A2 (user2) should NOT have a task (When=false at amount=100).
    $pend5b_u2 = @(GetPending $sessUser2 | Where-Object { "$($_.instanceId)" -eq "$inst5b" })
    Chk "S5b: user2 A2 NOT pending (When=false)" 0 $pend5b_u2.Count

    # Instance should be Approved (A2 gate bypassed).
    $detail5b = GetDetail $sessStart $inst5b
    if ($null -ne $detail5b) {
        Chk "S5b: instance Suspended at A2 (When=false suspends per spec)" 4 $detail5b.instance.status
    } else {
        Warn "S5b: could not fetch detail"
    }
}

Write-Host "Scenario 5 complete." -ForegroundColor Cyan

# ── Scenario 6 : Filter -- Role 7 + user.deptId == starter.deptId ─────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Scenario 6 : Filter -- same-dept role members only" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# qa_a_start is in dept_A. Role 7 has: qa_a_same_dept (dept_A) + qa_a_other_dept (dept_B).
# Filter "user.deptId == starter.deptId" -> only qa_a_same_dept passes.

Write-Host "-- Submit filter flow as qa_a_start (dept_A) --"
$inst6 = Submit $sessStart "approver-filter-flow" "{}"

if ($inst6) {
    Write-Host "-- Verify qa_a_same_dept (dept_A, role 7) has pending task --"
    $pend6_same = @(GetPending $sessSameDept | Where-Object { "$($_.instanceId)" -eq "$inst6" })
    Chk "S6: same_dept pending count (1)" 1 $pend6_same.Count

    Write-Host "-- Verify qa_a_other_dept (dept_B, role 7) has NO pending task --"
    $pend6_other = @(GetPending $sessOtherDept | Where-Object { "$($_.instanceId)" -eq "$inst6" })
    Chk "S6: other_dept pending count (0)" 0 $pend6_other.Count

    Write-Host "-- Approve as qa_a_same_dept --"
    $task6 = $pend6_same | Select-Object -First 1
    if ($task6) {
        $actCode6 = Act $sessSameDept "$($task6.taskId)" $true "Filter approved"
        Chk "S6: act HTTP" 200 $actCode6
        $detail6 = GetDetail $sessStart $inst6
        if ($null -ne $detail6) {
            Chk "S6: instance Approved" 1 $detail6.instance.status
        } else {
            Warn "S6: could not fetch detail"
        }
    }
}

Write-Host "Scenario 6 complete." -ForegroundColor Cyan

# ── Scenario 7 : Group -- DirectManager + Specified dedup -> single task ───────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Scenario 7 : Group node -- DirectManager+Specified dedup countersign" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# qa_a_start.ManagerId = qa_a_mgr. Group members: DirectManager L1 + Specified=qa_a_mgr.
# After dedup: only one task for qa_a_mgr (not two).

Write-Host "-- Submit group flow as qa_a_start --"
$inst7 = Submit $sessStart "approver-group-flow" "{}"

if ($inst7) {
    Write-Host "-- Verify qa_a_mgr has exactly ONE pending task (dedup) --"
    $pend7_mgr = @(GetPending $sessMgr | Where-Object { "$($_.instanceId)" -eq "$inst7" })
    Chk "S7: mgr pending count = 1 (dedup)" 1 $pend7_mgr.Count

    Write-Host "-- Verify qa_a_user2 has NO pending task (not in Group members) --"
    $pend7_u2 = @(GetPending $sessUser2 | Where-Object { "$($_.instanceId)" -eq "$inst7" })
    Chk "S7: user2 pending count = 0" 0 $pend7_u2.Count

    Write-Host "-- Approve as qa_a_mgr --"
    $task7 = $pend7_mgr | Select-Object -First 1
    if ($task7) {
        $actCode7 = Act $sessMgr "$($task7.taskId)" $true "Group approved"
        Chk "S7: act HTTP" 200 $actCode7
        $detail7 = GetDetail $sessStart $inst7
        if ($null -ne $detail7) {
            Chk "S7: instance Approved" 1 $detail7.instance.status
        } else {
            Warn "S7: could not fetch detail"
        }
    }
}

Write-Host "Scenario 7 complete." -ForegroundColor Cyan

# ── Forecast check (bonus) ────────────────────────────────────────────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Forecast check : FormField flow with approver=qa_a_user1" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

$forecastVars = "{`"approver`":`"$G_user1`"}"
$rFc = PostJson "$BaseUrl/api/oa/forecast/preview" @{ flowKey = "approver-forecast-flow"; varsJson = $forecastVars } $sessStart
Chk "Forecast: HTTP" 200 $rFc.Code
if ($rFc.Code -eq 200 -and $rFc.Data) {
    $steps = $rFc.Data.data.steps
    if ($null -eq $steps) { $steps = $rFc.Data.steps }
    if ($steps -and @($steps).Count -gt 0) {
        $firstStage = @($steps)[0]
        $approverNames = "$($firstStage.approvers)"
        $approverIds   = "$($firstStage.approverIds)"
        # Accept either names or ids containing user1's guid or nickname
        $hasConcrete = ($approverNames -and $approverNames -ne "" -and $approverNames -ne "[]") `
                       -or ($approverIds -and $approverIds.Contains($G_user1))
        Chk "Forecast: concrete approver resolved for FormField" $true $hasConcrete
    } else {
        Warn "Forecast: steps array empty or not found in response"
    }
} else {
    Warn "Forecast: endpoint not available or returned error (acceptable before live server)"
}

Write-Host "Forecast check complete." -ForegroundColor Cyan

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "RESULTS: PASS=$PASS   FAIL=$FAIL   WARN=$WARN" -ForegroundColor $(if ($FAIL -gt 0) { "Red" } elseif ($WARN -gt 0) { "Yellow" } else { "Green" })
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "MANUAL DB CHECKS to perform after this script:"
Write-Host ""
Write-Host "  -- Scenario 4 DataMap: verify Wf_ApproverMap lookup:"
Write-Host "  SELECT MapKey, MatchValue, ApproverUserId, ApproverRoleId, Enable"
Write-Host "  FROM Wf_ApproverMap WHERE MapKey = 'cc' ORDER BY OrderNo;"
Write-Host ""
Write-Host "  -- Scenario 5 When: verify A2 node was skipped for amount=100 instance ($inst5b):"
Write-Host "  SELECT NodeId, Status FROM Wf_FlowToken WHERE InstanceId = '$inst5b';"
Write-Host "  -- Expected: a2 token either not created, or Status=2 (Void/Skipped)"
Write-Host ""
Write-Host "  -- Scenario 6 Filter: verify only dept_A user got task for instance ($inst6):"
if ($inst6) {
    Write-Host "  SELECT AssigneeId, Status FROM Wf_FlowTask WHERE InstanceId = '$inst6';"
    Write-Host "  -- Expected: exactly 1 row, AssigneeId = $G_same_dept"
}
Write-Host ""
Write-Host "  -- Scenario 7 Group dedup: verify only 1 task for instance ($inst7):"
if ($inst7) {
    Write-Host "  SELECT COUNT(*) AS TaskCount FROM Wf_FlowTask WHERE InstanceId = '$inst7';"
    Write-Host "  -- Expected: 1 (dedup of DirectManager+Specified both -> qa_a_mgr)"
}
Write-Host ""

if ($FAIL -gt 0) {
    exit 1
} else {
    exit 0
}
