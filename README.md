# Shift tracking system — running instructions

Prerequisites: Docker.

```bash
cp .env.example .env   # set the SQL Server password and secrets
docker compose up --build
```

- Frontend: http://localhost:3000
- Backend API: proxied through the frontend at `/api`
- SQL Server: host port 1434

Run backend tests:

```bash
docker compose run --rm backend-tests
```

Run frontend tests:

```bash
cd frontend && npm install && npm test
```

(These commands become valid as the corresponding parts are added.)
