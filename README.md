# Shift tracking system — running instructions

Prerequisites: Docker.

```bash
cp .env.example .env   # set the SQL Server password, the admin password and JWT_SECRET
docker compose up --build
```

- Frontend: http://localhost:3000 (sign in as the admin from `.env`: `SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD`)
- Backend API: proxied through the frontend at `/api`
- SQL Server: host port 1434

Typical flow: the admin adds an employee and sends them the one-time link; the employee chooses a password
through that link, then clocks in and out. Clock In/Out times always come from the external Europe/Zurich
time APIs (https://time.now first, https://timeapi.io as fallback); if neither answers the punch is rejected and nothing is recorded.

Run backend tests:

```bash
docker compose run --rm backend-tests
```

Opt-in live check against the real time API:

```bash
docker run --rm -e RUN_LIVE_TIME_API=1 -v "$PWD/backend":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test --filter LiveTimeApiSmokeTests
```

Run frontend tests and checks:

```bash
cd frontend && npm install && npm test && npm run lint && npm run build
```
