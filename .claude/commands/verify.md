---
description: Verify changes work end-to-end after any code change. Use this aggressively any time you touch relevant code.
---

1. Start the app if it isn't running: check with `curl -s http://localhost:3000/api/health`; if it fails, run `docker compose up -d --build` and wait for it to be ready (rebuild with `docker compose up -d --build backend` or `frontend` after changing only that service).

2. Wait for the health-check endpoint to return 200: `curl -s http://localhost:3000/api/health` should return `{"status":"ok"}`.

3. Open localhost:3000 in the browser using the Claude Code Chrome extension.

4. Test the features touched this session and $ARGUMENTS.

5. Check the browser console for errors.

6. If the backend changed, curl the affected endpoints and confirm expected responses. Log in first (admin credentials are in `.env`, never print them): `set -a; . ./.env; set +a; curl -s -X POST http://localhost:3000/api/auth/login -H "Content-Type: application/json" -d "{\"email\":\"$SEED_ADMIN_EMAIL\",\"password\":\"$SEED_ADMIN_PASSWORD\"}"` — 200 with `{token, role, ...}`; send the token as `-H "Authorization: Bearer <token>"`. Clock endpoints need an employee token (admin creates the employee with `POST /api/admin/employees`, then the employee sets a password with `POST /api/auth/set-password` using the returned `inviteToken`, then logs in).

   - List employees (admin): `curl -s http://localhost:3000/api/admin/employees -H "Authorization: Bearer <admin token>"` — array of `{id, email, fullName, isActive, ...}` objects

   - Clock in (employee): `curl -s -X POST http://localhost:3000/api/attendance/clock-in -H "Authorization: Bearer <employee token>"` — 200 with `{clockedIn: true, record}`; a second call returns 409; 503 means the external time API is down and nothing was recorded

   - Clock out (employee): `curl -s -X POST http://localhost:3000/api/attendance/clock-out -H "Authorization: Bearer <employee token>"` — 200 with `clockOutUtc` set; without a clock-in it returns 409

   - Status (employee): `curl -s http://localhost:3000/api/attendance/status -H "Authorization: Bearer <employee token>"` — `{clockedIn, record}`

   - Monthly report (employee): `curl -s "http://localhost:3000/api/attendance/me/month" -H "Authorization: Bearer <employee token>"` — `{year, month, defaultShiftMinutes, days, totals}`

   - Correction (admin): `curl -s -X POST http://localhost:3000/api/admin/attendance/corrections -H "Authorization: Bearer <admin token>" -H "Content-Type: application/json" -d '{"kind":"Add","userId":<id>,"clockInLocal":"2026-01-05T09:00:00","clockOutLocal":"2026-01-05T17:00:00","reason":"smoke test"}'` — 201; overlapping or nonexistent (DST gap) times return 422

7. Screenshot the result.

If any step fails: read the error, fix it, restart from step 1.

If you hit a blocker: update the user and update this file.

Update this file as you use it to accurately reflect any changes, delete old information and add new information that may be needed.

When done: `bash .claude/commands/verify-all.sh` (AUTOMATED checks: health, smoke test, backend and frontend suites). It writes `.claude/last-verified` itself, only if everything passes; never create or touch that file by hand. The Stop hook compares it with the code fingerprint (app, tests, Dockerfiles, compose, env template, verification scripts) and keeps blocking you from finishing until it passes (it gives up with an UNVERIFIED warning only after 3 blocks in a row).

Browser checks (steps 3-5 and 7) are separate and are not recorded in the stamp: report their result in the conversation on its own, labelled "Browser:", next to the "Automated:" result.
