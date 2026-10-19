#!/usr/bin/env bash
#
# Creates a .env for CI from .env.example, with random passwords and keys
# generated for this run only. In GitHub Actions every generated value is
# masked, so it shows as *** if anything ever prints it, and the test
# database connection string is exported for the following steps.

set -euo pipefail

cd "$(dirname "$0")/.."

sql_password="Ci_$(openssl rand -hex 16)_Aa1!"
admin_password="Ci_$(openssl rand -hex 12)_Aa1!"
jwt_signing_key="$(openssl rand -hex 32)"
grafana_password="Ci_$(openssl rand -hex 12)_Aa1!"

if [ -n "${GITHUB_ACTIONS:-}" ]; then
  echo "::add-mask::$sql_password"
  echo "::add-mask::$admin_password"
  echo "::add-mask::$jwt_signing_key"
  echo "::add-mask::$grafana_password"
fi

sed \
  -e "s/^SQL_SA_PASSWORD=.*/SQL_SA_PASSWORD=$sql_password/" \
  -e "s/^SEED_ADMIN_PASSWORD=.*/SEED_ADMIN_PASSWORD=$admin_password/" \
  -e "s/^JWT_SIGNING_KEY=.*/JWT_SIGNING_KEY=$jwt_signing_key/" \
  -e "s/^GRAFANA_ADMIN_PASSWORD=.*/GRAFANA_ADMIN_PASSWORD=$grafana_password/" \
  .env.example > .env

if [ -n "${GITHUB_ENV:-}" ]; then
  sql_port=$(grep -E '^SQL_PORT=' .env | cut -d= -f2)
  echo "TEST_SQL_CONNECTION_STRING=Server=localhost,$sql_port;User Id=sa;Password=$sql_password;TrustServerCertificate=True;MultipleActiveResultSets=true" >> "$GITHUB_ENV"
fi

echo "Created .env with freshly generated passwords"
