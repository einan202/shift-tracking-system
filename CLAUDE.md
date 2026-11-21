# CLAUDE.md — Shift tracking system (clock in / clock out)

## Project
Web application where employees clock in/out and admins manage employees, the default shift duration and attendance corrections.

## Technologies (mandatory — do not substitute)
- Backend: **ASP.NET Core** (current LTS, .NET 10)
- Database: **Microsoft SQL Server** (container)
- Frontend: **React** (Vite + TypeScript)
- Containers: frontend, backend, database (docker compose)
- **All development is carried out with Claude Code** throughout the whole process.

## Hard constraint: attendance time source
- Every Clock In / Clock Out record **must use the time obtained from an external API service, based on the Europe/Zurich time zone**.
- The **browser clock and the server's local clock must never be used for recording attendance**.
- No fallback to a local clock: if the time API is unavailable, the request fails (HTTP 503) and nothing is written.
- Store instants as UTC (`datetimeoffset`); handle DST explicitly (ambiguous / nonexistent local times).
- Clients never send timestamps for clock in/out. No database `DEFAULT` expressions (`GETDATE()`, `SYSUTCDATETIME()`) on attendance time columns.
- Enforce with architecture and tests, not only convention (banned-API analyzer in the attendance project, schema test, stub time-API tests, frontend lint rule).

## Scope and quality
- A smaller number of thoroughly implemented, well-justified features is preferred over many superficial ones.
- Anything not completed or deliberately deferred must be written to `DECISIONS.md` (decision log) with the reason.
- Keep the architecture simple; avoid unnecessary complexity.

## Working rules for Claude Code
- Work in **small increments**: skeleton first, then backend (each piece with tests), then frontend.
- **Do not commit** until the user has confirmed the changes. After each increment, stop, summarize, and ask the user to review/commit before continuing.
- **Commits must be small — one logical change per commit, never everything at once.** Example split for the backend skeleton: (1) `.gitignore`; (2) solution + projects; (3) health endpoint + its test; (4) banned-clock analyzer + `BannedSymbols.txt`. Each commit should be reviewable in a minute and leave the build and tests green.
- Before committing, list the proposed commits (message + files) and wait for the user's confirmation; stage explicit file paths (no `git add .` / whole directories that mix concerns); create the commits one at a time.
- **Never commit generated build artifacts** (`*.dll`, .NET `bin/` and `obj/` directories, `node_modules/`, `dist/`); keep them excluded in `.gitignore`.
- Do not push, and do not touch other projects' containers or databases.
- Write tests with each backend change; run them before declaring an increment done.
- **New database tables:** the app runs as a least-privilege SQL user. Whenever a table is introduced, update its permissions in `AppUserProvisioner` (`backend/ClockApp.Api/Data/AppUserProvisioner.cs`) and add or update a test in `AppUserPermissionTests` for what the app may and may not do with it. Without a grant the app cannot touch the table.
- Reference: the approved plan is in `.claude/plan.md`.

## Verification (automatic, no `/verify` needed)
Follow `.claude/commands/verify.md` on your own; do not wait to be asked.
- After every **backend** change: run the relevant tests, rebuild only the backend, and check the affected endpoints against the running app. No screenshot.
- After every **frontend** change: run the affected tests, rebuild only the frontend, check the affected UI flow in the browser and the console for errors.
- If a check fails, fix it and re-verify without asking; stop and ask only for a real blocker.
- Run the full `verify-api.sh` and the complete backend and frontend suites after a major milestone and before declaring work done; do not repeat them after every small edit.
- Skip checks and rebuilds the change cannot affect.
- Proof of verification is `.claude/last-verified`, written only by `.claude/commands/verify-all.sh` after all automated checks pass (never touch it by hand). A Stop hook blocks finishing if code changed after the last pass.
