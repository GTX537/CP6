#!/usr/bin/env bash
# 2FA HTTP-layer end-to-end QA against live backend (http://localhost:5177).
BASE=http://localhost:5177
SP="$(dirname "$0")"
TOTP() { node "$SP/totp.mjs" "$1"; }
PASS=0; FAIL=0
chk() { # chk "desc" expected actual
  if [ "$2" = "$3" ]; then echo "  ✅ $1 (=$3)"; PASS=$((PASS+1)); else echo "  ❌ $1  expected[$2] got[$3]"; FAIL=$((FAIL+1)); fi
}
csrf() { grep cp6_csrf "$1" | tail -1 | awk '{print $NF}'; }
# returns HTTP status; writes body to $2
post() { # post jar bodyfile path json [extraheader]
  local jar=$1 out=$2 path=$3 json=$4
  local tok; tok=$(csrf "$jar")
  curl -s -o "$out" -w "%{http_code}" -c "$jar" -b "$jar" \
    -H "Content-Type: application/json" -H "X-CSRF-Token: $tok" \
    -X POST "$BASE$path" -d "$json"
}
login() { # login jar user pass tenant -> body in $5
  curl -s -o "$5" -w "%{http_code}" -c "$1" -b "$1" -H "Content-Type: application/json" \
    -X POST "$BASE/api/auth/login" -d "{\"userName\":\"$2\",\"password\":\"$3\",\"tenantCode\":\"$4\"}"
}
jqget() { node -e "let d=require('fs').readFileSync(0,'utf8');try{let o=JSON.parse(d);console.log(o['$1']??'')}catch{console.log('')}" < "$2" 2>/dev/null; }

echo "=========================================================="
echo "FLOW 1: optional self-enroll + challenge + verify + replay (admin/DEFAULT mode0)"
echo "=========================================================="
J=$SP/j_admin; rm -f "$J"
st=$(login "$J" admin 123456 DEFAULT "$SP/b"); chk "admin login http" 200 "$st"
chk "no twoFactorRequired on mode0 unenrolled" "" "$(jqget twoFactorRequired "$SP/b")"
chk "auth cookie set after plain login" "yes" "$(grep -q cp6_access "$J" && echo yes || echo no | sed 's/no//')yes" 2>/dev/null
grep -q cp6_access "$J" && echo "  ✅ cp6_access present" || echo "  ❌ cp6_access missing"
# setup-self
st=$(post "$J" "$SP/b" /api/auth/2fa/setup-self '{}'); chk "setup-self http" 200 "$st"
SECRET=$(jqget secret "$SP/b"); echo "  secret=$SECRET"
[ -n "$SECRET" ] && echo "  ✅ secret returned" || echo "  ❌ no secret"
# enroll-self
CODE=$(TOTP "$SECRET")
st=$(post "$J" "$SP/b" /api/auth/2fa/enroll-self "{\"code\":\"$CODE\"}"); chk "enroll-self http" 200 "$st"
# status
post "$J" "$SP/b" /api/auth/2fa/status '{}' >/dev/null
curl -s -b "$J" "$BASE/api/auth/2fa/status" -o "$SP/b"
chk "status enabled" "True" "$(jqget enabled "$SP/b" | sed 's/true/True/')"
chk "status canDisable (mode0)" "True" "$(jqget canDisable "$SP/b" | sed 's/true/True/')"
# logout
post "$J" "$SP/b" /api/auth/logout '{}' >/dev/null
# re-login -> challenge
J2=$SP/j_admin2; rm -f "$J2"
login "$J2" admin 123456 DEFAULT "$SP/b" >/dev/null
chk "re-login twoFactorRequired" "true" "$(jqget twoFactorRequired "$SP/b")"
chk "re-login mustEnroll=false" "false" "$(jqget mustEnroll "$SP/b")"
grep -q cp6_2fa "$J2" && echo "  ✅ pending cp6_2fa cookie set" || echo "  ❌ no pending cookie"
grep -q cp6_access "$J2" && echo "  ❌ auth cookie LEAKED pre-2fa" || echo "  ✅ no auth cookie pre-2fa"
# verify with TOTP
CODE=$(TOTP "$SECRET")
st=$(post "$J2" "$SP/b" /api/auth/2fa/verify "{\"code\":\"$CODE\",\"method\":\"totp\"}"); chk "verify TOTP http" 200 "$st"
grep -q cp6_access "$J2" && echo "  ✅ auth cookie set after verify" || echo "  ❌ auth cookie missing after verify"
# replay same pending -> E-SEC-013
st=$(post "$J2" "$SP/b" /api/auth/2fa/verify "{\"code\":\"$CODE\",\"method\":\"totp\"}")
EC=$(jqget code "$SP/b"); MSG=$(cat "$SP/b")
echo "  replay http=$st body=$MSG"
echo "$MSG" | grep -q "E-SEC-013" && echo "  ✅ replay rejected E-SEC-013" || echo "  ❌ replay not E-SEC-013"

echo "=========================================================="
echo "FLOW 2: email OTP fallback (admin enabled, re-login)"
echo "=========================================================="
J3=$SP/j_admin3; rm -f "$J3"
login "$J3" admin 123456 DEFAULT "$SP/b" >/dev/null
st=$(post "$J3" "$SP/b" /api/auth/2fa/email-otp '{}'); chk "email-otp send http (verify state)" 200 "$st"
sleep 1
OTP=$(grep -aoE "DEV-EMAIL.*" "$BLOG" 2>/dev/null | tail -1 | grep -oE "[0-9]{6}" | tail -1)
echo "  OTP from dev log: $OTP"
if [ -n "$OTP" ]; then
  st=$(post "$J3" "$SP/b" /api/auth/2fa/verify "{\"code\":\"$OTP\",\"method\":\"email\"}"); chk "verify email OTP http" 200 "$st"
  grep -q cp6_access "$J3" && echo "  ✅ auth cookie set after email verify" || echo "  ❌ no auth cookie after email verify"
else echo "  ⚠️ could not read OTP from backend log ($BLOG) — skipping email verify"; fi

echo "=========================================================="
echo "FLOW 3: forced tenant enroll (set DEFAULT mode=2 via policy API; tfaforce)"
echo "=========================================================="
# admin sets policy mode=2 (admin still logged in via J2)
st=$(curl -s -o "$SP/b" -w "%{http_code}" -b "$J2" -H "Content-Type: application/json" -H "X-CSRF-Token: $(csrf "$J2")" -X PUT "$BASE/api/sys/two-factor-policy" -d '{"mode":2}'); chk "PUT policy mode=2 http" 200 "$st"
# invalid mode -> E-SEC-012
curl -s -o "$SP/b" -b "$J2" -H "Content-Type: application/json" -H "X-CSRF-Token: $(csrf "$J2")" -X PUT "$BASE/api/sys/two-factor-policy" -d '{"mode":5}' >/dev/null
cat "$SP/b" | grep -q "E-SEC-012" && echo "  ✅ invalid mode -> E-SEC-012" || echo "  ❌ invalid mode not E-SEC-012 ($(cat $SP/b))"
# tfaforce login -> mustEnroll
JF=$SP/j_force; rm -f "$JF"
login "$JF" tfaforce 123456 DEFAULT "$SP/b" >/dev/null
chk "tfaforce twoFactorRequired" "true" "$(jqget twoFactorRequired "$SP/b")"
chk "tfaforce mustEnroll" "true" "$(jqget mustEnroll "$SP/b")"
# email-otp during enroll -> E-SEC-014
post "$JF" "$SP/b" /api/auth/2fa/email-otp '{}' >/dev/null
cat "$SP/b" | grep -q "E-SEC-014" && echo "  ✅ email-otp in enroll -> E-SEC-014" || echo "  ❌ not E-SEC-014 ($(cat $SP/b))"
# setup -> enroll
post "$JF" "$SP/b" /api/auth/2fa/setup '{}' >/dev/null
FSECRET=$(jqget secret "$SP/b")
CODE=$(TOTP "$FSECRET")
st=$(post "$JF" "$SP/b" /api/auth/2fa/enroll "{\"code\":\"$CODE\"}"); chk "forced enroll http" 200 "$st"
grep -q cp6_access "$JF" && echo "  ✅ auth cookie after forced enroll" || echo "  ❌ no auth cookie after forced enroll"

echo "=========================================================="
echo "FLOW 4: disable-self forced -> E-SEC-019 ; FLOW 5: admin reset"
echo "=========================================================="
# tfaforce disable-self under mode2 -> E-SEC-019
CODE=$(TOTP "$FSECRET")
curl -s -o "$SP/b" -b "$JF" -H "Content-Type: application/json" -H "X-CSRF-Token: $(csrf "$JF")" -X POST "$BASE/api/auth/2fa/disable-self" -d "{\"currentPassword\":\"123456\",\"code\":\"$CODE\",\"method\":\"totp\"}" >/dev/null
cat "$SP/b" | grep -q "E-SEC-019" && echo "  ✅ disable-self forced tenant -> E-SEC-019" || echo "  ❌ not E-SEC-019 ($(cat $SP/b))"
# admin reset tfaforce: need tfaforce userId
FUID=$("/c/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/sqlcmd.exe" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT CONVERT(varchar(36),Id) FROM Sys_Users WHERE UserName='tfaforce';" | tr -d '\r' | head -1)
echo "  tfaforce uid=$FUID"
st=$(curl -s -o "$SP/b" -w "%{http_code}" -b "$J2" -H "Content-Type: application/json" -H "X-CSRF-Token: $(csrf "$J2")" -X POST "$BASE/api/user/reset-2fa" -d "\"$FUID\""); chk "admin reset-2fa http" 200 "$st"

echo "=========================================================="
echo "FLOW 6: CSRF + pending-without-cookie boundaries"
echo "=========================================================="
# setup-self WITHOUT csrf header -> 403
st=$(curl -s -o "$SP/b" -w "%{http_code}" -b "$J3" -H "Content-Type: application/json" -X POST "$BASE/api/auth/2fa/setup-self" -d '{}'); chk "setup-self no CSRF -> 403" 403 "$st"
# 2fa verify with no pending cookie -> E-SEC-013 (fresh jar, but need a csrf... use a plain login csrf is not pending). Hit without cookie:
st=$(curl -s -o "$SP/b" -w "%{http_code}" -X POST "$BASE/api/auth/2fa/verify" -H "Content-Type: application/json" -d '{"code":"123456","method":"totp"}')
echo "  verify no-cookie http=$st body=$(cat $SP/b)"

echo "=========================================================="
echo "FLOW 7: secret not leaked into SecurityLog"
echo "=========================================================="
LEAK=$("/c/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/sqlcmd.exe" -S "localhost\\KOUSQLSERVER" -d CP6DB -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM Sys_SecurityLogs WHERE Reason LIKE '%$SECRET%' OR Reason LIKE '%otpauth%';" | tr -d '\r ' | head -1)
chk "no secret/otpauth in SecurityLog.Reason" "0" "$LEAK"

# restore DEFAULT mode=0
curl -s -o /dev/null -b "$J2" -H "Content-Type: application/json" -H "X-CSRF-Token: $(csrf "$J2")" -X PUT "$BASE/api/sys/two-factor-policy" -d '{"mode":0}'
echo "=========================================================="
echo "RESULT: PASS=$PASS FAIL=$FAIL"
echo "=========================================================="
