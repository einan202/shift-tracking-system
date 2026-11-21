#!/bin/bash
# Prints a hash of every file that can affect the build or the verification itself (tracked or untracked,
# not git-ignored): application + tests + Dockerfiles/nginx (backend/, frontend/), compose, env template,
# verification scripts and the hook settings. Markdown is excluded (docs don't change behaviour).
# Any edit after a passing run changes the hash, so the stamp proves the CURRENT code passed.
# `fingerprint.sh --paths` prints the covered paths (used by the Stop hook).
cd "$(dirname "$0")/.." || exit 1
PATHS=(backend frontend docker-compose.yml .env.example scripts .claude/commands .claude/settings.json ':!*.md')

if [ "$1" = "--paths" ]; then printf '%s\n' "${PATHS[@]}"; exit 0; fi

git ls-files -co --exclude-standard -- "${PATHS[@]}" \
  | sort | while read -r f; do [ -f "$f" ] && shasum "$f"; done | shasum | cut -d' ' -f1
