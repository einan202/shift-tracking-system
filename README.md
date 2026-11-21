# Shift tracking system — running instructions

Prerequisites: Docker.

## Run it

Run the commands one at a time, from the project folder. They contain no comments, so they work in zsh and bash.

1. Create the `.env` file with new random secrets (needs `openssl`, which macOS and Linux include):

```bash
sh scripts/setup-env.sh
```

It prints the admin sign-in once (the same values are saved in `.env`). It refuses to run if `.env` already exists, so it never overwrites your settings.

2. Start the app:

```bash
docker compose up --build
```

3. Wait until the backend logs `Application started`, then open http://localhost:3000 and sign in with the admin email and password from step 1 (`SEED_ADMIN_EMAIL` and `SEED_ADMIN_PASSWORD` in `.env`).

### Or create `.env` by hand

```bash
cp .env.example .env
```

Then open `.env` and replace every `<set-...>` value. The placeholders are not valid passwords, so the app will not start until you do.

| Variable | What it is | Rules |
|---|---|---|
| `SEED_ADMIN_EMAIL` | email of the first admin | any email |
| `SEED_ADMIN_PASSWORD` | password of the first admin | at least 10 characters |
| `MSSQL_SA_PASSWORD` | SQL Server administrator password | at least 8 characters with upper case, lower case, a digit and a symbol |
| `APP_DB_PASSWORD` | password of the database user the app runs as | same rules, different from the `sa` password, no `;` or `=` |
| `JWT_SECRET` | key that signs login tokens | at least 32 random characters |

To generate strong values (one command at a time):

```bash
openssl rand -base64 48
```

```bash
printf "%sAa1-\n" "$(openssl rand -hex 16)"
```

Use the first for `JWT_SECRET` and the second for the two database passwords (run it twice). Never commit `.env`; it is git-ignored.

The admin account is created only when the database has no admin yet. The database passwords are set when the database is first created. If you change them later, start from a fresh database (see below).

## Stop, restart, reset

```bash
docker compose down
```

stops the app and keeps the data. `docker compose up -d` starts it again.

```bash
docker compose down -v
```

also deletes the database, so the next start is empty and uses the current `.env` values.

Run only one copy of the project at a time: a second clone with the same folder name shares the first one's containers and database volume, and uses the same ports (3000 and 1434).

## Where things run

- Frontend: http://localhost:3000
- Backend API: proxied through the frontend at `/api` (it is not published on its own port).
- SQL Server: reachable only from your own computer, on `localhost:1434`. The app runs as a least-privilege user (`clockapp_app`, created at startup, no deletes, append-only corrections); `sa` is used only for migrations and provisioning.

Typical flow: the admin adds an employee and sends them the one-time link; the employee chooses a password
through that link, then clocks in and out. Clock In/Out times always come from the external Europe/Zurich
time APIs (https://time.now first, https://timeapi.io as fallback); if neither answers the punch is rejected and nothing is recorded.

## Troubleshooting

- `required variable ... is missing a value`: `.env` does not exist or is missing a value. Run `sh scripts/setup-env.sh`, or create it by hand as described above.
- `.env already exists` from the script: nothing was changed. To get new values, delete `.env`, run `docker compose down -v` (this deletes the database) and run the script again.
- `Login failed for user 'sa'` in the database log: the database volume was created with different passwords. Use the original `.env`, or run `docker compose down -v` to start from an empty database.
- `port is already allocated`: another program uses port 3000 or 1434. Stop it, or stop the other copy of this project.

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
