#!/bin/bash
# Runs the automated part of /verify and records proof ONLY if everything passes:
# health check, API smoke test, backend tests, frontend tests + lint + build.
# `.claude/last-verified` is written here (never by hand) and holds the fingerprint of the code that passed.
# Browser checks (/verify steps 3-5, 7) are NOT part of this script and are not recorded in the stamp:
# report them separately in the conversation.
set -eo pipefail
cd "$(dirname "$0")/../.."

echo "== health";   curl -sf http://localhost:3000/api/health; echo
echo "== API smoke test"; bash .claude/commands/verify-api.sh | grep -vE "^PASS"
echo "== backend tests"; docker compose run --rm backend-tests 2>&1 | grep -E "error|Passed!|Failed!"
echo "== frontend tests, lint, build"
(cd frontend && npm test 2>&1 | grep -E "Test Files|Tests " && npx oxlint 2>&1 | tail -1 && npm run build 2>&1 | grep -E "built in|error")

printf '%s %s\n' "$(bash scripts/fingerprint.sh)" "$(date -u +%FT%TZ)" > .claude/last-verified
rm -f .claude/verify-blocks
echo "VERIFIED (automated checks only): all passed and recorded in .claude/last-verified"
echo "BROWSER CHECK: not run by this script. Do /verify steps 3-5 and 7 separately and report that result on its own."
