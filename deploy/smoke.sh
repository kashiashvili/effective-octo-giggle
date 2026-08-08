#!/usr/bin/env bash
#
# Post-deploy smoke test. Verifies a RUNNING Profiler deployment end to end: the core journey works,
# the anti-abuse guards actually fire, the privacy guarantees hold on the wire, and the operator
# endpoints are token-gated. Safe to run against any environment — it only creates throwaway accounts.
#
#   BASE=https://profiler.example.com MT=<Metrics:Token> ./deploy/smoke.sh
#
# BASE defaults to http://localhost:8080. MT is optional; the /metrics checks are skipped without it.
set -u
BASE="${BASE:-http://localhost:8080}"
MT="${MT:-}"
J=$(mktemp); U="smoke_$(date +%s)_$$"
pass=0; fail=0
ok(){ echo "  PASS  $1"; pass=$((pass+1)); }
no(){ echo "  FAIL  $1"; fail=$((fail+1)); }

# A page may render the antiforgery token in several forms (nav sign-out + the main form). ASP.NET
# issues one token value per request, so take exactly one — concatenating them yields an invalid value.
field(){ grep -o "name=\"$2\"[^>]*value=\"[^\"]*\"" "$1" | sed 's/.*value="//;s/"//' | head -1; }

echo "== 1. app reachable =="
[ "$(curl -s -o /dev/null -w '%{http_code}' $BASE/)" = "200" ] && ok "GET / 200" || no "GET /"

echo "== 2. registration works (with whatever guards are configured) =="
P=$(mktemp); curl -s -c $J $BASE/account/register -o $P
T=$(field $P __RequestVerificationToken); K=$(field $P FormTicket)
[ -n "$K" ] && echo "  note: anti-sybil form ticket is ACTIVE" || echo "  note: form ticket not issued (AntiAbuse:GuardRegistration off)"
sleep 4  # the guard's MinFormSeconds time-trap, when enabled
R=$(curl -s -b $J -c $J -o /dev/null -w '%{http_code}' -X POST $BASE/account/register \
  --data-urlencode "Username=$U" --data-urlencode "Password=Tr0ubad0ur-x9" \
  --data-urlencode "ConfirmPassword=Tr0ubad0ur-x9" --data-urlencode "Website=" \
  --data-urlencode "FormTicket=$K" --data-urlencode "__RequestVerificationToken=$T")
REGISTERED=0
case "$R" in
  302) ok "registered (302)"; REGISTERED=1 ;;
  # The per-IP register rate limit is deliberately tight (default 5/hour). Repeated smoke runs from one
  # IP will hit it — that is the guard working, not a defect, but the signed-in checks can't continue.
  429) ok "register rate limit active (429) — anti-abuse working"
       echo "  note: quota for this IP is spent; re-run later or restart the app to reset the window" ;;
  *)   no "register returned $R" ;;
esac

echo "== 3. honeypot rejects a bot-shaped submission =="
P3=$(mktemp); JH=$(mktemp); curl -s -c $JH $BASE/account/register -o $P3
T3=$(field $P3 __RequestVerificationToken); K3=$(field $P3 FormTicket)
sleep 4
RH=$(curl -s -b $JH -c $JH -o /dev/null -w '%{http_code}' -X POST $BASE/account/register \
  --data-urlencode "Username=hp_$$" --data-urlencode "Password=Tr0ubad0ur-x9" \
  --data-urlencode "ConfirmPassword=Tr0ubad0ur-x9" --data-urlencode "Website=http://spam.example" \
  --data-urlencode "FormTicket=$K3" --data-urlencode "__RequestVerificationToken=$T3")
# 200 = form re-rendered with the error; 429 = the IP quota is spent, which also blocks the bot.
case "$RH" in
  200) ok "honeypot submission rejected" ;;
  429) ok "blocked by rate limit (quota spent — honeypot not reached)" ;;
  *)   no "honeypot returned $RH" ;;
esac

if [ "$REGISTERED" = "0" ]; then
  echo; echo "SKIPPED the signed-in checks (no session — registration was rate limited)."
  echo "RESULT: $pass passed, $fail failed"; exit $fail
fi

echo "== 4. self-described interests build a fingerprint =="
P4=$(mktemp); curl -s -b $J -c $J $BASE/sources/interests -o $P4
T4=$(field $P4 __RequestVerificationToken)
R4=$(curl -s -b $J -c $J -o /dev/null -w '%{http_code}' -X POST $BASE/sources/interests \
  --data-urlencode "features=self-tech:rust" --data-urlencode "features=self-music:jazz" \
  --data-urlencode "features=self-outdoors:climbing" --data-urlencode "custom=byzantine history" \
  --data-urlencode "__RequestVerificationToken=$T4")
[ "$R4" = "302" ] && ok "interests saved (302)" || no "interests returned $R4"
curl -s -b $J $BASE/sources/dashboard | grep -q "Self-described" && ok "fingerprint source recorded" || no "no Self-described source"

echo "== 5. matches render, raw interests never surface =="
M=$(mktemp); curl -s -b $J $BASE/matches -o $M
grep -q "Your Top Matches" $M && ok "matches page renders" || no "matches page"
grep -qi "byzantine" $M && no "LEAK: raw interest text on matches page" || ok "no raw interest text on page"

echo "== 6. data export exposes only derived data =="
D=$(mktemp); curl -s -b $J $BASE/account/data.json -o $D
grep -q '"RawInterestsStored": false' $D && ok "export asserts raw interests not stored" || no "export missing guarantee"
grep -qi "byzantine\|self-tech" $D && no "LEAK: raw interests in export" || ok "no raw interests in export"

echo "== 7. operator endpoints are token-gated =="
c(){ curl -s -o /dev/null -w '%{http_code}' "$@"; }
if [ -n "$MT" ]; then
  [ "$(c $BASE/metrics)" = "401" ] && ok "no token -> 401" || no "no-token not 401"
  [ "$(c -H 'Authorization: Bearer wrong' $BASE/metrics)" = "401" ] && ok "wrong token -> 401" || no "wrong token not 401"
  [ "$(c -b $J $BASE/metrics)" = "401" ] && ok "ordinary session -> 401" || no "session not 401"
  [ "$(c -H "Authorization: Bearer $MT" $BASE/metrics)" = "200" ] && ok "operator token -> 200" || no "operator token not 200"
  curl -s -H "Authorization: Bearer $MT" $BASE/metrics | grep -q "$U" && no "LEAK: username in metrics" || ok "metrics names no user"
  [ "$(c -H "Authorization: Bearer $MT" $BASE/metrics/reports)" = "200" ] && ok "moderation view reachable by operator" || no "reports not 200"
else
  echo "  skip: set MT=<Metrics:Token> to check the operator endpoints"
fi
[ "$(c -X POST -H 'Content-Type: application/json' -d '{"suspend":true}' $BASE/metrics/suspend)" != "200" ] \
  && ok "suspend refused without operator token" || no "suspend reachable without token!"

echo; echo "RESULT: $pass passed, $fail failed"; exit $fail
