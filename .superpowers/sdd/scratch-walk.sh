#!/bin/bash
B="/c/Users/Administrator/.claude/skills/gstack/browse/dist/browse"
SHOTS="/c/CP6/.superpowers/sdd/shots"
OUT="/c/CP6/.superpowers/sdd/walk-results.txt"
mkdir -p "$SHOTS"
> "$OUT"
# noise regex: known pre-existing noise
NOISE='intlify|next\(\)|No match found for location|CSRF|negotiate|notification|Ep small|Ep label|deprecat|SignalR|hub|status of 403|status of 401|__vue-devtools|Feature flag'
while IFS='|' read -r name route label; do
  [ -z "$name" ] && continue
  $B js "1" >/dev/null 2>&1
  $B console --clear >/dev/null 2>&1
  status=$($B goto "http://localhost:5173$route" 2>&1 | head -1)
  $B wait --networkidle >/dev/null 2>&1
  sleep 0.4
  # rendered check: main text length + not the not-found marker
  txt=$($B js "(document.querySelector('.el-main,main,#app')?.innerText||'').replace(/\s+/g,' ').trim()" 2>/dev/null)
  len=${#txt}
  # console errors (errors+warnings), strip noise
  errs=$($B console --errors 2>/dev/null | grep -viE "$NOISE" | grep -iE 'error|warn|fail|exception|cannot|undefined is not|typeerror' | grep -viE 'BEGIN|END UNTRUSTED' | head -8)
  # screenshot
  $B screenshot "$SHOTS/regression-$name.png" >/dev/null 2>&1
  echo "=== $name | $route | $label ===" >> "$OUT"
  echo "STATUS: $status" >> "$OUT"
  echo "TEXTLEN: $len" >> "$OUT"
  echo "SNIPPET: ${txt:0:100}" >> "$OUT"
  if [ -n "$errs" ]; then echo "NEWERR:" >> "$OUT"; echo "$errs" >> "$OUT"; else echo "NEWERR: none" >> "$OUT"; fi
  echo "" >> "$OUT"
  echo "done: $name (len=$len)"
done < /c/CP6/.superpowers/sdd/scratch-routes.txt
echo "ALL DONE"
