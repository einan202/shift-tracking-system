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
- Do not push, and do not touch other projects' containers or databases.
- Write tests with each backend change; run them before declaring an increment done.
- Reference: the approved plan is in `/Users/sagit/.claude/plans/suggest-a-plan-for-wobbly-pearl.md`.
