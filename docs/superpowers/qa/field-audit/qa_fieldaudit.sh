#!/usr/bin/env bash
# Field Audit HTTP-layer e2e against live backend (http://localhost:5177, CSRF off in dev).
# ASCII-only payloads (Windows bash curl -d does not send Chinese as UTF-8).
BASE=http://localhost:5177
SP="$(dirname "$0")"
SQLCMD="/c/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/sqlcmd.exe"
PASS=0; FAIL=0
chk(){ if [ "$2" = "$3" ]; then echo "  ✅ $1 (=$3)"; PASS=$((PASS+1)); else echo "  ❌ $1 expected[$2] got[$3]"; FAIL=$((FAIL+1)); fi; }
csrf(){ grep cp6_csrf "$1" 2>/dev/null | tail -1 | awk '{print $NF}'; }
login(){ curl -s -o "$5" -w "%{http_code}" -c "$1" -b "$1" -H "Content-Type: application/json" -X POST "$BASE/api/auth/login" -d "{\"userName\":\"$2\",\"password\":\"$3\",\"tenantCode\":\"$4\"}"; }
req(){ local m=$1 jar=$2 path=$3 json=$4; curl -s -o "$SP/b" -w "%{http_code}" -b "$jar" -H "Content-Type: application/json" -H "X-CSRF-Token: $(csrf "$jar")" -X "$m" "$BASE$path" ${json:+-d "$json"}; }
get(){ curl -s -b "$2" "$BASE$1" -o "$SP/b" -w "%{http_code}"; }
ev(){ node -e "let o;try{o=JSON.parse(require('fs').readFileSync('$SP/b','utf8'))}catch(e){console.log('ERR');process.exit(0)}console.log($1)" 2>/dev/null; }

echo "== Setup: login admin (DEFAULT) =="
J=$SP/jfa; rm -f "$J"
chk "admin login" 200 "$(login "$J" admin 123456 DEFAULT "$SP/b")"

RID=9001
echo "== FLOW 1: create role $RID -> Added audit row =="
chk "create role http" 200 "$(req POST "$J" /api/role "{\"roleId\":$RID,\"roleName\":\"QARoleOne\",\"description\":\"qa\",\"enable\":true,\"orderNo\":99}")"
sleep 1
get "/api/sys/field-audit?entityName=Sys_Role&entityKey=$RID" "$J" >/dev/null
chk "Sys_Role Added row present" "1" "$(ev "o.rows.filter(r=>r.operation===1).length")"
chk "Added changeCount>=1 (PK excluded)" "true" "$(ev "((o.rows.find(r=>r.operation===1)||{}).changeCount||0)>=1")"

echo "== FLOW 2: update RoleName -> Modified accurate diff (R2/T4 先查后改) =="
chk "update role http" 200 "$(req PUT "$J" /api/role "{\"roleId\":$RID,\"roleName\":\"QARoleTwo\",\"description\":\"qa\",\"enable\":true,\"orderNo\":99}")"
sleep 1
get "/api/sys/field-audit/record?entityName=Sys_Role&entityKey=$RID" "$J" >/dev/null
chk "Modified row has RoleName QARoleOne->QARoleTwo (proves 先查后改)" "yes" \
  "$(ev "o.rows.filter(r=>r.operation===2).some(r=>{try{return JSON.parse(r.changes).some(x=>x.Field==='RoleName'&&x.Old==='QARoleOne'&&x.New==='QARoleTwo')}catch{return false}})?'yes':'no'")"

echo "== FLOW 3: timeline ascending (first op = Added) =="
echo "  ops asc: $(ev "JSON.stringify(o.rows.map(r=>r.operation))")"
chk "timeline first op = Added(1)" "1" "$(ev "o.rows[0]?.operation")"

echo "== FLOW 4: update user field -> Modified audit row =="
FUID=$("$SQLCMD" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(varchar(36),Id) FROM Sys_Users WHERE UserName='tfaforce';" | tr -d '\r ' | head -1)
echo "  tfaforce id=$FUID"
req PUT "$J" /api/user "{\"id\":\"$FUID\",\"userName\":\"tfaforce\",\"nickName\":\"AuditRenamed\",\"email\":\"tfaforce@default.test\",\"roleId\":1,\"enable\":true}" >/dev/null
sleep 1
get "/api/sys/field-audit?entityName=Sys_User&entityKey=$FUID" "$J" >/dev/null
chk "Sys_User Modified audit row present" "true" "$(ev "o.rows.filter(r=>r.operation===2).length>=1")"
chk "user diff has NickName change" "yes" \
  "$(ev "o.rows.some(r=>{try{return JSON.parse(r.changes||'[]').some(x=>x.Field==='NickName')}catch{return false}})?'yes':'no'")"

echo "== FLOW 5: delete role $RID -> Deleted audit row =="
chk "delete role http" 200 "$(req DELETE "$J" /api/role "[$RID]")"
sleep 1
get "/api/sys/field-audit?entityName=Sys_Role&entityKey=$RID" "$J" >/dev/null
chk "Sys_Role Deleted audit row present" "true" "$(ev "o.rows.filter(r=>r.operation===3).length>=1")"

echo "== FLOW 6: KEY GUARD - no Password/Secret/Hash in Changes (DB-wide) =="
chk "zero rows leak secret material" "0" "$("$SQLCMD" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM Sys_FieldAuditLogs WHERE Changes LIKE '%assword%' OR Changes LIKE '%ecret%' OR Changes LIKE '%okenHash%' OR Changes LIKE '%Salt%';" | tr -d '\r ' | head -1)"

echo "== FLOW 7: tenant scoping =="
chk "role $RID rows all DEFAULT tenant" "0" "$("$SQLCMD" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM Sys_FieldAuditLogs WHERE TenantId<>'00000000-0000-0000-0000-0000000000A1' AND EntityKey='$RID';" | tr -d '\r ' | head -1)"
echo "  total audit rows: $("$SQLCMD" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM Sys_FieldAuditLogs;" | tr -d '\r ' | head -1)"

echo "== FLOW 8: 403 - user without sys-field-audit:query + tenant isolation =="
req POST "$J" /api/role "{\"roleId\":9002,\"roleName\":\"LowPriv\",\"description\":\"noperm\",\"enable\":true,\"orderNo\":98}" >/dev/null
req POST "$J" /api/user "{\"userName\":\"fauditlow\",\"password\":\"123456\",\"nickName\":\"LowUser\",\"roleId\":9002,\"enable\":true}" >/dev/null
sleep 1
JL=$SP/jlow; rm -f "$JL"
echo "  fauditlow login http=$(login "$JL" fauditlow 123456 DEFAULT "$SP/b")"
chk "no-permission user -> 403" "403" "$(get "/api/sys/field-audit?page=1&pageSize=10" "$JL")"
chk "admin -> 200" "200" "$(get "/api/sys/field-audit?page=1&pageSize=10" "$J")"

echo "== RESULT: PASS=$PASS FAIL=$FAIL =="
