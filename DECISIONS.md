# Decisions

Decisions and deferred items, recorded only after the owner has chosen them. Items not listed here are still open.

## Planning and process

### 1. Two plans instead of one

- **Status:** accepted (2026-11-21).
- **Decision:** I made two plans: one for the skeleton, and another for the upgraded logic of time editing together with the UI/UX improvements.

### 2. Process: research skill to compare approaches

- **Status:** accepted (2026-11-21).
- **Decision:** I used the deep research skill to compare different approaches to clock-in and clock-out systems, to make the product plan better.

### 3. Docker Compose with separate containers and health checks

- **Status:** accepted (2026-11-21).
- **Decision:** the application runs with Docker Compose, with the backend, frontend and database as separate containers. Each has a health check, so that all of them are checked before the application is considered ready.
- **Reason (owner):** each part can be tracked separately.

### 4. Process: run the verify skill after every change

- **Status:** accepted (2026-11-21).
- **Decision:** I use the verify skill to run checks after every backend or frontend change. These checks are separate from the written tests.

### 5. Process: independent review with a subagent

- **Status:** accepted (2026-11-21).
- **Decision:** I run the /review skill with a subagent, so that mistakes and wrong behavior from Claude are found, and the notes are fixed.

## Product rules and roles

### 6. Two roles: admin and employee

- **Status:** accepted (2026-11-21).
- **Decision:** the system has two actors, admin and employee. This separates the authority to correct mistakes and manage users (admin) from the main features that employees need.

### 7. Employees are responsible for fixing their own times

- **Status:** accepted (2026-11-21).
- **Decision:** the employee is responsible for fixing their own times. The admin sees the history of fixes and of every clock-in and clock-out, and can fix times if needed. A planned future feature is to lock the monthly report so that the admin can approve it.

### 8. Employees see only the current clock-in and clock-out times

- **Status:** accepted (2026-11-21).
- **Decision:** the employee sees only the current (updated) clock-in and clock-out times of each day, not every record behind them.
- **Reason (owner):** this makes the timesheet simpler to follow.

### 9. The first clock-in and the last clock-out define the day

- **Status:** accepted (2026-11-21).
- **Decision:** the first clock-in and the last clock-out of a day are the ones that count. A later clock-out is useful and can update the end of the day. A later clock-in is not important and must not replace the original first clock-in.

### 10. A clock-out on a later date is not counted automatically

- **Status:** accepted (2026-11-22).
- **Decision:** if an employee forgets to clock out and clocks out on a later day, that time is not counted for the earlier day. The day shows "needs correction" until the employee sets the real end time with a reason.

### 11. Working days and expected hours are set by the admin for each employee

- **Status:** accepted (2026-11-21).
- **Decision:** when registering an employee, the admin defines their working days and the expected hours of each day. The times can be pre-configured for all employees when needed. For now the pay model is monthly. A future upgrade may allow more than one shift per day.

### 12. No clock-in on non-working days

- **Status:** accepted (2026-11-21).
- **Decision:** an employee cannot clock in on a day that is not one of their working days.
- **Reason (owner):** the total work hours must stay legal.

### 13. Employees can delete or change a shift with a reason; the admin sees the full history

- **Status:** accepted (2026-11-21).
- **Decision:** an employee can delete a shift or change it manually, and must give a reason. The admin sees the full history, so that they can later decide whether to approve the monthly report (a planned future feature).

### 14. Manual times are always clearly marked

- **Status:** accepted (2026-11-22).
- **Decision:** a manual time is never treated as a real clock-in or clock-out. It is saved as a separate record with a reason, and it is shown in orange with a "Manual" tag. Only admins can see the full history of changes.

### 15. Confirmation message after clock-in and clock-out

- **Status:** accepted (2026-11-21).
- **Decision:** a confirmation message is shown after each clock-in and clock-out.
- **Reason (owner):** better user experience.

### 16. Password setup and reset through a personal link

- **Status:** accepted (2026-11-21).
- **Decision:** each employee receives a personal link with a secure token and uses it to choose their own password. If the employee forgets the password, the admin can create a new link for them.

### 17. The admin can deactivate and reactivate employees

- **Status:** accepted (2026-11-21).
- **Decision:** the admin can deactivate an employee, who then can no longer sign in, and can activate them again when needed.

## Time source and reliability

### 18. Time source: an external API for Europe/Zurich time

- **Status:** accepted (2026-11-21).
- **Decision:** the time of every clock-in and clock-out comes from an external API for Europe/Zurich time. I chose it after Claude investigated the options and their trade-offs (see decisions 21 and 22 for the provider order).

### 19. The time rule is enforced by the code

- **Status:** accepted (2026-11-22).
- **Decision:** clock-in and clock-out times never come from the browser or the server clock. This is checked automatically: a banned-API check in the attendance project, a database test (no default values on time columns), tests with a fake time service, and a frontend test that scans the code. If the time API fails, the request fails with a 503 and nothing is saved.

### 20. Retries and fallback for failed clock-in and clock-out

- **Status:** accepted (2026-11-21).
- **Decision:** I tested the system end to end and wrote down the critical bugs. Clock-in and clock-out attempts were failing, so I had Claude investigate different retry and fallback strategies. The goal is that the user does not see the failure and does not have to clock in or out again. This makes the most important feature more reliable (see decision 21 for the result).

### 21. Time provider order: time.now first, timeapi.io as fallback

- **Status:** accepted (2026-11-21).
- **Plan said:** timeapi.io as the only provider (TimeZoneDB as a later fallback).
- **Decision:** `ResilientTimeSource` asks time.now first (2 attempts), then timeapi.io (2 attempts). If both fail, the punch is a 503 and nothing is written.
- **Reason (measured, details in `docs/bugfixes.md`):**
  - timeapi.io alone answered HTTP 502 for 33% of 90 direct requests, and many 200 answers took longer than the 3 s timeout. With 2 attempts, 8 of 40 punches (20%) were rejected; 5 and 8 attempts still rejected about 5%.
  - time.now answered 60 of 60 direct requests (median 0.2 s). With time.now first and timeapi.io as fallback, 60 of 60 punches succeeded (median 0.2 s).
- **Unchanged:** attendance time still comes only from external Europe/Zurich time APIs, never from the browser or server clock; the provider used is stored in `AttendanceRecords.TimeProvider`.
- **Open points:** time.now publishes no numeric rate limit and asks for attribution (not added).

### 22. One backup time provider instead of two (TimeZoneDB not added)

- **Status:** accepted (2026-11-21).
- **Decision:** time.now is the primary provider and timeapi.io the single backup. A second backup (for example TimeZoneDB) is not added.
- **Reason (owner):** TimeZoneDB requires an API key. A second backup could lead to fewer failures, but it means another integration to handle, another API key and another API to look after. One backup is enough for this MVP.

## Scope phasing and security

### 23. Corrections are only added, never changed

- **Status:** accepted (2026-11-22).
- **Decision:** a fix never changes the original clock-in or clock-out. Every fix is a new row, and the day that is shown and counted is calculated from the original and its fixes. The app's database user cannot update or delete correction rows, and cannot delete clock records.

### 24. Attendance corrections: phased implementation

- **Status:** accepted (2026-11-21).
- **Decision:** the first version supports **Add** and **Adjust** corrections. **Void** and whole-day corrections are planned for the next development phase.
- **Reason (owner):** these features are implemented separately because of the added complexity of overlapping records, audit history and data integrity.

### 25. `sa` credentials in the backend container: not changed for the MVP

- **Status:** accepted for the MVP (2026-11-21); optional production upgrade.
- **Current state:** the app runs as the least-privilege user `clockapp_app`. The `sa` connection string (`ConnectionStrings:Migration`) is still present in the backend container's environment and is used only at startup for migrations, provisioning and admin seeding.
- **Decision (owner):** not implemented for the MVP. A separate migration step that keeps the `sa` password out of the running app container is an optional future upgrade for production.

### 26. Monthly report is planned, but left for the future

- **Status:** deferred (2026-11-22).
- **Decision:** the monthly report (employee submits the month, admin approves it, and the month is then locked) is planned but not built. It is kept for the future.
- **Reason (owner):** because of the limited time, checking that the existing system works correctly is more important than adding this feature.
