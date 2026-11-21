#!/bin/sh
# Creates .env with unique random secrets. Refuses to run if .env already exists.
set -eu
cd "$(dirname "$0")/.."

if [ -e .env ]; then
  echo ".env already exists, nothing was changed."
  echo "To create new values: delete .env, run 'docker compose down -v' (this deletes the database), then run this script again."
  exit 1
fi
command -v openssl >/dev/null 2>&1 || { echo "openssl is required but was not found."; exit 1; }

# Letters, digits and '-' only: safe in .env, in the SQL connection string and in every shell.
sa_password="$(openssl rand -hex 16)Aa1-"
app_password="$(openssl rand -hex 16)Bb2-"
admin_password="$(openssl rand -hex 12)Cc3-"
jwt_secret="$(openssl rand -hex 32)"

umask 077
cat > .env <<ENV
MSSQL_SA_PASSWORD=$sa_password
SEED_ADMIN_EMAIL=admin@example.com
SEED_ADMIN_PASSWORD=$admin_password
JWT_SECRET=$jwt_secret
APP_DB_PASSWORD=$app_password
ENV

echo "Created .env with new random secrets."
echo "Admin sign-in:  admin@example.com  /  $admin_password"
echo "(the same values are in .env; the password is not shown again)"
echo "Next: docker compose up --build"
