# Backlog

Known limitations, technical debt and future features that are **not** implemented. Source: PR #1 review items 6-9 and 11. Priority and scope are not decided; entries describe the current state only. Architectural decisions are recorded separately in `DECISIONS.md`.

| # | Type | Item | Current state |
|---|---|---|---|
| 6 | Technical debt | Browser-clock guard is a source scan, not a lint rule | `frontend/src/features/attendance/noBrowserClock.test.ts` fails on `Date.now()`, `new Date()` and `performance.now()` in app sources. oxlint has no matching rule, and the regex would miss forms such as `Date['now']()`. |
| 7 | Technical debt | No component tests for the admin screens | `Employees.tsx` and `AttendanceAdmin.tsx` have no tests. Covered today: the clock panel, the correction dialog, the punch logic, the invite link, and the backend endpoints they call. |
| 8 | Limitation | time.now attribution not added | time.now's page asks for attribution when using its free API. Nothing is shown in the app or README. It also publishes no numeric rate limit (a punch makes 1 request, at most 4). |
| 9 | Limitation | Corrections are minute-granular | The admin form takes minutes only, so a shift cannot be closed within the minute it started (the server answers "Clock-out must be after clock-in"). |
| 11 | Future feature | Idempotency keys for clock requests | Not built. A retry after a lost response gets a 409; the UI explains it using `/status`. |
| 11 | Future feature | Change-password endpoint | Not built. A forgotten or replaced password needs a new invite link from an admin. |
| 11 | Future feature | Anomaly flagging for forgotten clock-outs | Not built. An open record stays open until an admin corrects it; nothing flags it. |
| 11 | Future feature | Month picker | Not built. The monthly report and the admin attendance view have previous/next month buttons only. |
