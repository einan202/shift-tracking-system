#!/bin/bash
# Stop hook: Claude may not finish while code changed after the last SUCCESSFUL automated verification.
# `.claude/last-verified` holds the fingerprint written only by .claude/commands/verify-all.sh.
# Blocks (exit 2) every time, including after the first block, until verification passes. To avoid an
# endless loop when verification genuinely cannot pass, it gives up after MAX_BLOCKS consecutive blocks and
# lets Claude stop with a visible UNVERIFIED warning. The counter resets when verification passes.
MAX_BLOCKS=3

# Only enforce while the app is running.
if ! curl -s --max-time 1 http://localhost:3000/api/health > /dev/null 2>&1; then
  exit 0
fi

cd "$(dirname "$0")/.." || exit 0
cat > /dev/null  # hook input (JSON) is not needed

current=$(bash scripts/fingerprint.sh)
stamped=$(cut -d' ' -f1 .claude/last-verified 2>/dev/null)
counter=.claude/verify-blocks

if [ "$current" = "$stamped" ]; then
  rm -f "$counter"
  exit 0
fi

# Nothing ever verified and nothing changed in the working tree: nothing to prove.
if [ -z "$stamped" ] && [ -z "$(git status --porcelain -- $(bash scripts/fingerprint.sh --paths | tr '\n' ' '))" ]; then
  exit 0
fi

blocks=$(( $(cat "$counter" 2>/dev/null || echo 0) + 1 ))
if [ "$blocks" -gt "$MAX_BLOCKS" ]; then
  rm -f "$counter"
  printf '{"systemMessage":"UNVERIFIED: code changed after the last passing verification and verify-all.sh did not pass after %s attempts. Stopping anyway; see the conversation for the failure."}\n' "$MAX_BLOCKS"
  exit 0
fi
echo "$blocks" > "$counter"
printf "Code changed since the last successful automated verification (block %s of %s). Run: bash .claude/commands/verify-all.sh; fix and re-run until it prints VERIFIED, then finish. Do not stop before it passes.\n" "$blocks" "$MAX_BLOCKS" >&2
exit 2
