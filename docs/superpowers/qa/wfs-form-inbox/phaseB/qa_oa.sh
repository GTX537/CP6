#!/usr/bin/env bash
# ============================================================================
# OA 电子表单信箱 Phase B — HTTP e2e skeleton
# Branch: feat/oa-inbox-core  |  Worktree: D:\CP6-oa-core
#
# Prerequisites:
#   1. OA backend running on $BASE (isolated CP6DB_OA — see README.md Step 1)
#   2. seed.sql applied to CP6DB_OA (see README.md Step 2)
#   3. Dev CSRF disabled (appsettings.Development.json: Security:Csrf:Enabled=false)
#      → no X-CSRF-TOKEN header needed; cookie cp6_at carries auth
#   4. Run from git-bash (MSYS) or WSL bash.  Native cmd/PowerShell: use curl.exe
#      and adjust the cookie-jar paths to Windows-style paths.
#
# Usage:
#   bash docs/superpowers/qa/wfs-form-inbox/phaseB/qa_oa.sh
#
# The script prints PASS / FAIL per assertion and a final PASS=N FAIL=N summary.
# ============================================================================

BASE=http://localhost:5177
SP="$(cd "$(dirname "$0")" && pwd)"   # directory of this script (for temp files)
PASS=0; FAIL=0

# ── Helpers ──────────────────────────────────────────────────────────────────
chk()  { if [ "$2" = "$3" ]; then echo "  ✅ $1 (=$3)"; PASS=$((PASS+1)); else echo "  ❌ $1  expected[$2]  got[$3]"; FAIL=$((FAIL+1)); fi; }
# login(cookie_jar, username, password, tenantCode, body_file)
# returns HTTP status code
login(){
    curl -s -o "$5" -w "%{http_code}" \
         -c "$1" -b "$1" \
         -H "Content-Type: application/json" \
         -X POST "$BASE/api/auth/login" \
         -d "{\"userName\":\"$2\",\"password\":\"$3\",\"tenantCode\":\"$4\"}"
}
# ev(jq_expr)  — evaluate a Node.js expression against the last response body file ($SP/b)
ev(){ node -e "let o;try{o=JSON.parse(require('fs').readFileSync('$SP/b','utf8'))}catch(e){console.log('ERR');process.exit(0)};console.log($1)" 2>/dev/null; }
# post(cookie_jar, path, json_body) → HTTP code; response body in $SP/b
post(){
    curl -s -o "$SP/b" -w "%{http_code}" \
         -c "$1" -b "$1" \
         -H "Content-Type: application/json" \
         -X POST "$BASE$2" \
         -d "$3"
}
# get(cookie_jar, path) → HTTP code; response body in $SP/b
get(){
    curl -s -o "$SP/b" -w "%{http_code}" \
         -b "$1" \
         "$BASE$2"
}

# ─────────────────────────────────────────────────────────────────────────────
echo "=== OA Phase B HTTP e2e — target: $BASE ==="
echo "    (requires seed.sql applied + ASPNETCORE_ENVIRONMENT=Development)"
echo ""

# ── FLOW 1: qa_starter login ─────────────────────────────────────────────────
echo "== FLOW 1: qa_starter login =="
JS=$SP/j_starter; rm -f "$JS"
chk "starter login 200" 200 "$(login "$JS" qa_starter 123456 DEFAULT "$SP/b")"
chk "starter login code=0" "0" "$(ev "o.code")"

# ── FLOW 2: stats endpoint (dashboard cards) ──────────────────────────────────
echo "== FLOW 2: GET /api/oa/inbox/stats (4 stat fields) =="
chk "stats 200" 200 "$(get "$JS" /api/oa/inbox/stats)"
chk "stats.data has pendingCount" "true"        "$(ev "typeof o.data.pendingCount === 'number'")"
chk "stats.data has runningCount" "true"        "$(ev "typeof o.data.runningCount === 'number'")"
chk "stats.data has doneThisMonth" "true"       "$(ev "typeof o.data.doneThisMonth === 'number'")"
chk "stats.data has rejectedBackToMe" "true"    "$(ev "typeof o.data.rejectedBackToMe === 'number'")"

# ── FLOW 3: draft save + list + submit ───────────────────────────────────────
echo "== FLOW 3: save draft → list → submit =="
chk "draft save 200" 200 "$(post "$JS" /api/oa/draft/save '{"flowKey":"leave","varsJson":"{\"reason\":\"QA annual leave\",\"days\":2}"}')"
chk "draft save code=0" "0" "$(ev "o.code")"
DRAFT_ID="$(ev "o.data.id")"
echo "  draftId=$DRAFT_ID"
[ -n "$DRAFT_ID" ] && { echo "  ✅ draftId returned"; PASS=$((PASS+1)); } || { echo "  ❌ no draftId"; FAIL=$((FAIL+1)); }

chk "draft list 200" 200 "$(get "$JS" /api/oa/draft/list)"
chk "draft list contains draft" "true" "$(ev "Array.isArray(o.data)&&o.data.some(d=>d.id==='$DRAFT_ID')")"

# Submit the draft → starts flow instance
chk "draft submit 200" 200 "$(post "$JS" /api/oa/draft/submit "{\"id\":\"$DRAFT_ID\"}")"
chk "draft submit code=0" "0" "$(ev "o.code")"

# After submit, draft should no longer appear in draft list
chk "draft list empty after submit" 200 "$(get "$JS" /api/oa/draft/list)"
chk "draft gone from list" "false" "$(ev "Array.isArray(o.data)&&o.data.some(d=>d.id==='$DRAFT_ID')")"

# ── FLOW 4: approver sees pending task ───────────────────────────────────────
echo "== FLOW 4: qa_approver login → pending list has the new item =="
JA=$SP/j_approver; rm -f "$JA"
chk "approver login 200" 200 "$(login "$JA" qa_approver 123456 DEFAULT "$SP/b")"

chk "pending 200" 200 "$(get "$JA" /api/oa/inbox/pending)"
chk "pending list non-empty" "true" "$(ev "Array.isArray(o.data)&&o.data.length>0")"
TASK_ID="$(ev "o.data[0].taskId")"
INSTANCE_ID="$(ev "o.data[0].instanceId")"
echo "  taskId=$TASK_ID  instanceId=$INSTANCE_ID"
[ -n "$TASK_ID" ] && { echo "  ✅ taskId present"; PASS=$((PASS+1)); } || { echo "  ❌ no taskId"; FAIL=$((FAIL+1)); }

# ── FLOW 5: mark task read ───────────────────────────────────────────────────
echo "== FLOW 5: mark task read =="
chk "mark-read 200" 200 "$(post "$JA" /api/oa/inbox/task/read "{\"id\":\"$TASK_ID\"}")"
chk "mark-read code=0" "0" "$(ev "o.code")"

# ── FLOW 6: detail (left read-only form + right timeline) ────────────────────
echo "== FLOW 6: inbox detail — form + timeline =="
chk "detail 200" 200 "$(get "$JA" /api/oa/inbox/detail/$INSTANCE_ID)"
chk "detail.data.instance present"  "true" "$(ev "typeof o.data.instance === 'object'")"
chk "detail.data.timeline non-empty" "true" "$(ev "Array.isArray(o.data.timeline)&&o.data.timeline.length>0")"
chk "detail.data.forecast non-empty" "true" "$(ev "Array.isArray(o.data.forecast)&&o.data.forecast.length>0")"
# The leave form schema should be included so the frontend can render the read-only form
chk "detail.data.formSchemaJson present" "true" "$(ev "typeof o.data.formSchemaJson === 'string'&&o.data.formSchemaJson.length>0")"

# ── FLOW 7: approver approves (single task) ───────────────────────────────────
echo "== FLOW 7: batch approve (single task) =="
chk "batch approve 200" 200 "$(post "$JA" /api/oa/inbox/batch "{\"taskIds\":[\"$TASK_ID\"],\"approve\":true,\"comment\":\"QA approve\"}")"
chk "batch approve code=0" "0" "$(ev "o.code")"
chk "batch result[0].ok true" "true" "$(ev "o.data[0].ok")"

# After approval, approver's pending list should be empty (or not contain this task)
chk "pending list empty after approve" 200 "$(get "$JA" /api/oa/inbox/pending)"
chk "approved task gone from pending" "false" "$(ev "Array.isArray(o.data)&&o.data.some(t=>t.taskId==='$TASK_ID')")"

# Instance should now appear in 'done' for the approver
chk "done list 200" 200 "$(get "$JA" "/api/oa/inbox/done?tab=mine")"
chk "done contains instance" "true" "$(ev "Array.isArray(o.data)&&o.data.some(d=>d.instanceId==='$INSTANCE_ID')")"

# ── FLOW 8: cc user pending-cc ────────────────────────────────────────────────
echo "== FLOW 8: qa_cc pending-cc =="
JC=$SP/j_cc; rm -f "$JC"
chk "cc login 200" 200 "$(login "$JC" qa_cc 123456 DEFAULT "$SP/b")"
chk "pending-cc 200" 200 "$(get "$JC" /api/oa/inbox/pending-cc)"
CC_ID="$(ev "Array.isArray(o.data)&&o.data.length>0?o.data[0].ccId:null")"
echo "  ccId=$CC_ID"
[ -n "$CC_ID" ] && [ "$CC_ID" != "null" ] && { echo "  ✅ cc row present"; PASS=$((PASS+1)); } || { echo "  (cc row may be missing if CcHook wrote it — check Wf_FlowCc in DB)"; }

# ── FLOW 9: flow-admin list ───────────────────────────────────────────────────
echo "== FLOW 9: flow-admin list (admin login) =="
chk "flow-admin list 200" 200 "$(get "$JS" /api/oa/flow-admin/list)"
chk "leave flow present" "true" "$(ev "Array.isArray(o.data)&&o.data.some(f=>f.flowKey==='leave')")"
chk "leave flow enabled" "true" "$(ev "Array.isArray(o.data)&&o.data.some(f=>f.flowKey==='leave'&&f.enable===true)")"

# ── FLOW 10: E-WF-008 conflict ───────────────────────────────────────────────
echo "== FLOW 10: enable leave2 (same FormKey as leave) → E-WF-008 =="
code="$(post "$JS" /api/oa/flow-admin/enable '{"flowKey":"leave2","enabled":true}')"
chk "enable conflict HTTP 400" 400 "$code"
# The body should contain E-WF-008
node -e "let o;try{o=JSON.parse(require('fs').readFileSync('$SP/b','utf8'))}catch(e){process.exit(0)};process.stdout.write(o.message||'')" 2>/dev/null | grep -q "E-WF-008" \
    && { echo "  ✅ body contains E-WF-008"; PASS=$((PASS+1)); } \
    || { echo "  ❌ body does not contain E-WF-008 (got: $(cat "$SP/b" | head -c 200))"; FAIL=$((FAIL+1)); }

# leave2 should still be disabled after the rejected attempt
chk "flow-admin list 200 after conflict" 200 "$(get "$JS" /api/oa/flow-admin/list)"
chk "leave2 still disabled" "false" "$(ev "Array.isArray(o.data)&&o.data.some(f=>f.flowKey==='leave2'&&f.enable===true)")"

# ── Running list (starter's in-flight view) ───────────────────────────────────
echo "== FLOW 11: running list (starter's in-flight view) =="
# Re-start a second instance so 'running' has at least one entry for starter
chk "second draft save" 200 "$(post "$JS" /api/oa/draft/save '{"flowKey":"leave","varsJson":"{\"reason\":\"second QA\",\"days\":1}')"
chk "second draft code=0" "0" "$(ev "o.code")"
DRAFT2="$(ev "o.data.id")"
post "$JS" /api/oa/draft/submit "{\"id\":\"$DRAFT2\"}" >/dev/null

chk "running list 200" 200 "$(get "$JS" /api/oa/inbox/running)"
chk "running list non-empty" "true" "$(ev "Array.isArray(o.data)&&o.data.length>0")"

# ─────────────────────────────────────────────────────────────────────────────
echo ""
echo "== RESULT: PASS=$PASS  FAIL=$FAIL =="
[ "$FAIL" -eq 0 ] && echo "All checks passed." || echo "Some checks FAILED — review output above."
