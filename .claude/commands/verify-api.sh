#!/usr/bin/env bash
# API smoke test for the running stack (docker compose up). Uses the real Europe/Zurich time API.
# Reads the seed admin from .env (never printed), creates a throw-away employee, walks every endpoint,
# then deactivates that employee. Exit code 1 if any check fails.
set -u
cd "$(dirname "$0")/../.."
set -a; . ./.env; set +a
B=${BASE:-http://localhost:3000/api}
FAIL=0
RUN="verify-$RANDOM$RANDOM"

# req METHOD PATH TOKEN [JSON-body] -> sets $CODE and $BODY
req() {
  local out
  out=$(curl -s -w '\n%{http_code}' -X "$1" "$B$2" ${3:+-H "Authorization: Bearer $3"} ${4:+-H 'Content-Type: application/json' -d "$4"})
  CODE=${out##*$'\n'}; BODY=${out%$'\n'*}
}
check() { # check "label" expected-code
  if [ "$CODE" = "$2" ]; then echo "PASS $1 ($CODE)"; else echo "FAIL $1: got $CODE, expected $2: $BODY"; FAIL=1; fi
}
field() { python3 -c "import sys,json;print(json.loads(sys.argv[1])$2)" "$1"; }

req GET /health; check "health" 200

req POST /auth/login "" "{\"email\":\"$SEED_ADMIN_EMAIL\",\"password\":\"$SEED_ADMIN_PASSWORD\"}"; check "admin login" 200
ADMIN=$(field "$BODY" "['token']")
req POST /auth/login "" '{"email":"nobody@example.com","password":"wrong-password-1"}'; check "bad login is generic 401" 401

req POST /admin/employees "$ADMIN" "{\"email\":\"$RUN@example.com\",\"fullName\":\"Verify Employee\"}"; check "admin creates employee" 201
EMP_ID=$(field "$BODY" "['id']"); INVITE=$(field "$BODY" "['inviteToken']")
req POST /auth/set-password "" "{\"token\":\"$INVITE\",\"password\":\"verify-password-1\"}"; check "set password via invite" 200
req POST /auth/set-password "" "{\"token\":\"$INVITE\",\"password\":\"verify-password-1\"}"; check "invite link is single-use" 400
req POST /auth/login "" "{\"email\":\"$RUN@example.com\",\"password\":\"verify-password-1\"}"; check "employee login" 200
EMP=$(field "$BODY" "['token']")

req GET /auth/me; check "anonymous /auth/me" 401
req GET /admin/employees "$EMP"; check "employee blocked from admin route" 403
req POST /attendance/clock-in "$ADMIN"; check "admin cannot punch" 403

req GET /attendance/status "$EMP"; check "status (not clocked in)" 200
req POST /attendance/clock-out "$EMP"; check "clock-out without clock-in" 409

# Real time API: timeapi.io sometimes answers 502, which the app turns into a legitimate fail-closed 503
# (nothing recorded). Retry the punch a few times before calling it a failure.
punch() { # punch clock-in|clock-out
  for i in 1 2 3 4; do
    req POST "/attendance/$1" "$EMP"
    [ "$CODE" != 503 ] && return
    echo "note: time API unavailable (503, nothing recorded), retry $i"; sleep 3
  done
}
punch clock-in
check "clock-in (time from external API)" 200
echo "     recorded: $(field "$BODY" "['record']['clockInUtc']") provider=$(field "$BODY" "['record']['provider']")  | machine UTC now: $(date -u +%FT%TZ)"
req POST /attendance/clock-in "$EMP"; check "second clock-in is rejected" 409
req GET /attendance/status "$EMP"; check "status (clocked in)" 200
sleep 2
punch clock-out
check "clock-out" 200

req GET /attendance/me/month "$EMP"; check "monthly report (current month)" 200
req GET "/attendance/me/month?year=2026&month=13" "$EMP"; check "monthly report rejects month 13" 400

req GET "/admin/attendance?userId=$EMP_ID" "$ADMIN"; check "admin attendance view" 200
req POST /admin/attendance/corrections "$ADMIN" "{\"kind\":\"Add\",\"userId\":$EMP_ID,\"clockInLocal\":\"2026-01-05T09:00:00\",\"clockOutLocal\":\"2026-01-05T17:00:00\",\"reason\":\"verify script manual entry\"}"; check "admin adds a manual record" 201
req POST /admin/attendance/corrections "$ADMIN" "{\"kind\":\"Add\",\"userId\":$EMP_ID,\"clockInLocal\":\"2026-03-29T02:30:00\",\"clockOutLocal\":\"2026-03-29T04:00:00\",\"reason\":\"nonexistent DST local time\"}"; check "DST gap time rejected" 422
req POST /admin/attendance/corrections "$ADMIN" "{\"kind\":\"Add\",\"userId\":$EMP_ID,\"clockInLocal\":\"2026-01-05T10:00:00\",\"clockOutLocal\":\"2026-01-05T11:00:00\",\"reason\":\"overlaps the first one\"}"; check "overlapping correction rejected" 422
req POST /admin/attendance/corrections "$EMP" "{\"kind\":\"Add\",\"userId\":$EMP_ID,\"clockInLocal\":\"2026-01-06T09:00:00\",\"clockOutLocal\":\"2026-01-06T10:00:00\",\"reason\":\"employee must not do this\"}"; check "employee cannot correct" 403

req GET /admin/settings/shift "$ADMIN"; check "read default shift duration" 200
req PUT /admin/settings/shift "$ADMIN" '{"durationMinutes":0}'; check "invalid shift duration rejected" 400

req POST "/admin/employees/$EMP_ID/deactivate" "$ADMIN"; check "cleanup: deactivate test employee" 200
req GET /attendance/status "$EMP"; check "deactivated user's token stops working" 401

[ $FAIL = 0 ] && echo "ALL CHECKS PASSED" || echo "SOME CHECKS FAILED"
exit $FAIL
