#!/usr/bin/env bash
# Tenant-Compliance (#5) HTTP e2e against live backend (http://localhost:5177, CSRF off in dev).
BASE=http://localhost:5177
SP="$(dirname "$0")"
SQLCMD="/c/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/sqlcmd.exe"
PASS=0; FAIL=0
chk(){ if [ "$2" = "$3" ]; then echo "  ✅ $1 (=$3)"; PASS=$((PASS+1)); else echo "  ❌ $1 expected[$2] got[$3]"; FAIL=$((FAIL+1)); fi; }
login(){ curl -s -o "$5" -w "%{http_code}" -c "$1" -b "$1" -H "Content-Type: application/json" -X POST "$BASE/api/auth/login" -d "{\"userName\":\"$2\",\"password\":\"$3\",\"tenantCode\":\"$4\"}"; }
# decode a claim from the cp6_at cookie in a jar
claim(){ local jar=$1 key=$2; local at=$(grep cp6_at "$jar" 2>/dev/null | tail -1 | awk '{print $NF}'); [ -z "$at" ] && { echo ""; return; }; node -e "let t='$at'.split('.')[1].replace(/-/g,'+').replace(/_/g,'/');let p=JSON.parse(Buffer.from(t,'base64').toString());console.log(p['$key']??'')" 2>/dev/null; }
atval(){ grep cp6_at "$1" 2>/dev/null | tail -1 | awk '{print $NF}'; }
ev(){ node -e "let o;try{o=JSON.parse(require('fs').readFileSync('$SP/b','utf8'))}catch(e){console.log('ERR');process.exit(0)}console.log($1)" 2>/dev/null; }

echo "== FLOW 1: admin login → isPlatformAdmin true =="
J=$SP/jadmin; rm -f "$J"
chk "admin login" 200 "$(login "$J" admin 123456 DEFAULT "$SP/b")"
chk "profile isPlatformAdmin" "true" "$(ev "o.isPlatformAdmin")"
chk "admin token is_platform_admin claim" "true" "$(claim "$J" is_platform_admin)"

echo "== FLOW 2: platform-admin gate (admin 200) =="
chk "GET /api/platform/tenant (admin)" 200 "$(curl -s -o "$SP/b" -w "%{http_code}" -b "$J" "$BASE/api/platform/tenant?page=1&pageSize=5")"

echo "== FLOW 3: create tenant + first admin (atomic provisioning) =="
TS=$("$SQLCMD" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT FORMAT(SYSUTCDATETIME(),'HHmmss');" | tr -d '\r ' | head -1)
TCODE="qatc$TS"; TADMIN="qaadmin$TS"
code=$(curl -s -o "$SP/b" -w "%{http_code}" -b "$J" -H "Content-Type: application/json" -X POST "$BASE/api/platform/tenant" -d "{\"code\":\"$TCODE\",\"name\":\"QA TC Tenant\",\"adminUserName\":\"$TADMIN\"}")
chk "create tenant http" 200 "$code"
TEMPPW=$(ev "o.tempPassword"); TID=$(ev "o.tenantId")
echo "  tenantId=$TID tempPwd=$TEMPPW adminUser=$TADMIN"
[ -n "$TEMPPW" ] && { echo "  ✅ tempPassword returned (one-time)"; PASS=$((PASS+1)); } || { echo "  ❌ no tempPassword"; FAIL=$((FAIL+1)); }
# verify in DB: tenant + admin both exist, admin MustChangePassword + not platform
DBADM=$("$SQLCMD" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -s "|" -Q "SET NOCOUNT ON; SELECT MustChangePassword, IsPlatformAdmin FROM Sys_Users WHERE UserName='$TADMIN';" | tr -d '\r ' | head -1)
chk "new admin MustChangePassword|IsPlatformAdmin" "1|0" "$DBADM"

echo "== FLOW 4: non-platform user → 403 E-SEC-031 (tfaforce, DEFAULT) =="
JL=$SP/jlow; rm -f "$JL"
login "$JL" tfaforce 123456 DEFAULT "$SP/b" >/dev/null
code=$(curl -s -o "$SP/b" -w "%{http_code}" -b "$JL" "$BASE/api/platform/tenant?page=1&pageSize=5")
chk "non-platform GET /api/platform/tenant" 403 "$code"
cat "$SP/b" | grep -q "E-SEC-031" && echo "  ✅ body E-SEC-031" || echo "  (body: $(cat $SP/b | head -c 120))"

echo "== FLOW 5: impersonation START → token swap =="
code=$(curl -s -o "$SP/b" -w "%{http_code}" -b "$J" -c "$J" -H "Content-Type: application/json" -X POST "$BASE/api/platform/impersonation/start" -d "{\"tenantId\":\"$TID\",\"reason\":\"qa\"}")
chk "impersonation start http" 200 "$code"
chk "imp returns menus (non-empty)" "true" "$(ev "Array.isArray(o.menus)&&o.menus.length>0")"
IMP_AT=$(atval "$J")   # save imp access cookie for replay test
chk "imp token tenant_id == target" "$TID" "$(claim "$J" tenant_id)"
chk "imp token has impersonator_id" "true" "$([ -n "$(claim "$J" impersonator_id)" ] && echo true || echo false)"
chk "imp token NO is_platform_admin" "" "$(claim "$J" is_platform_admin)"
chk "imp token mustChange false" "" "$(claim "$J" must_change_password | sed 's/false//')"

echo "== FLOW 6 (R9-i): during imp, access /api/platform/* → 403 E-SEC-034 =="
code=$(curl -s -o "$SP/b" -w "%{http_code}" -b "$J" "$BASE/api/platform/tenant?page=1&pageSize=5")
chk "imp→/api/platform/tenant blocked" 403 "$code"
cat "$SP/b" | grep -q "E-SEC-034" && { echo "  ✅ E-SEC-034 (imp suspends platform power)"; PASS=$((PASS+1)); } || { echo "  ❌ not E-SEC-034 ($(cat $SP/b|head -c100))"; FAIL=$((FAIL+1)); }

echo "== FLOW 7: write during imp → OperLog carries ImpersonatorId =="
# impersonated qa-admin (RoleId=1 in target tenant) creates a role → OperLog
curl -s -o /dev/null -b "$J" -H "Content-Type: application/json" -X POST "$BASE/api/role" -d "{\"roleId\":9101,\"roleName\":\"ImpRole\",\"enable\":true,\"orderNo\":50}"
sleep 1
ADMINID=$("$SQLCMD" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(varchar(36),Id) FROM Sys_Users WHERE UserName='admin';" | tr -d '\r ' | head -1)
IMPLOGS=$("$SQLCMD" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM Sys_OperLogs WHERE ImpersonatorId='$ADMINID';" | tr -d '\r ' | head -1)
chk "OperLog rows with ImpersonatorId=admin >=1" "true" "$([ "${IMPLOGS:-0}" -ge 1 ] && echo true || echo false)"

echo "== FLOW 8: impersonation END → restore platform token =="
code=$(curl -s -o "$SP/b" -w "%{http_code}" -b "$J" -c "$J" -H "Content-Type: application/json" -X POST "$BASE/api/platform/impersonation/end" -d "{}")
chk "impersonation end http" 200 "$code"
chk "restored token is_platform_admin" "true" "$(claim "$J" is_platform_admin)"
chk "restored token NO impersonator_id" "" "$(claim "$J" impersonator_id)"

echo "== FLOW 9 (R9-ii): replay OLD imp cookie after end → 401 (jti blacklisted) =="
JR=$SP/jreplay; printf "" > "$JR"
# craft a jar with the saved imp access cookie
echo -e "# Netscape HTTP Cookie File\nlocalhost\tFALSE\t/\tFALSE\t0\tcp6_at\t$IMP_AT" > "$JR"
code=$(curl -s -o "$SP/b" -w "%{http_code}" -b "$JR" "$BASE/api/role")
chk "replay old imp cookie → 401" 401 "$code"

echo "== FLOW 10: cross-tenant audit shows ImpersonationStarted/Ended =="
curl -s -o "$SP/b" -b "$J" "$BASE/api/platform/audit?tenantCode=$TCODE&page=1&pageSize=20" >/dev/null
chk "audit has ImpersonationStarted(25)" "true" "$(ev "o.rows.some(r=>r.eventType===25)")"
curl -s -o "$SP/b" -b "$J" "$BASE/api/platform/audit?page=1&pageSize=50" >/dev/null
chk "audit has ImpersonationEnded(26)" "true" "$(ev "o.rows.some(r=>r.eventType===26)")"

echo "== FLOW 11: GDPR export tenant → no Password in JSON =="
curl -s -o "$SP/exp.json" -b "$J" "$BASE/api/platform/gdpr/export/tenant/$TID"
grep -qi "\"Password\"" "$SP/exp.json" && { echo "  ❌ Password LEAKED in export"; FAIL=$((FAIL+1)); } || { echo "  ✅ no Password key in tenant export"; PASS=$((PASS+1)); }
grep -q "$TADMIN" "$SP/exp.json" && echo "  ✅ export contains the tenant admin user row" || echo "  (admin row not found in export head)"

echo "== FLOW 12: GDPR subject anonymize → user disabled =="
NUID=$("$SQLCMD" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(varchar(36),Id) FROM Sys_Users WHERE UserName='$TADMIN';" | tr -d '\r ' | head -1)
code=$(curl -s -o "$SP/b" -w "%{http_code}" -b "$J" -X DELETE "$BASE/api/platform/gdpr/erase/subject/$NUID?confirm=true")
chk "erase subject http" 200 "$code"
ANON=$("$SQLCMD" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -s "|" -Q "SET NOCOUNT ON; SELECT Enable, CASE WHEN UserName LIKE 'anon-%' THEN 'anon' ELSE UserName END FROM Sys_Users WHERE Id='$NUID';" | tr -d '\r ' | head -1)
chk "erased subject Enable|UserName" "0|anon" "$ANON"

echo "== RESULT: PASS=$PASS FAIL=$FAIL =="
