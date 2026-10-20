#!/usr/bin/env bash
#
# Creates a .env for CI from .env.example, with random passwords and keys
# generated for this run only. In GitHub Actions every generated value is
# masked, so it shows as *** if anything ever prints it, and the settings
# the tests need (database and Service Bus emulator) are exported for the
# following steps.

set -euo pipefail

cd "$(dirname "$0")/.."

sql_password="Ci_$(openssl rand -hex 16)_Aa1!"
admin_password="Ci_$(openssl rand -hex 12)_Aa1!"
jwt_signing_key="$(openssl rand -hex 32)"

if [ -n "${GITHUB_ACTIONS:-}" ]; then
  echo "::add-mask::$sql_password"
  echo "::add-mask::$admin_password"
  echo "::add-mask::$jwt_signing_key"
fi

sed \
  -e "s/^SQL_SA_PASSWORD=.*/SQL_SA_PASSWORD=$sql_password/" \
  -e "s/^SEED_ADMIN_PASSWORD=.*/SEED_ADMIN_PASSWORD=$admin_password/" \
  -e "s/^JWT_SIGNING_KEY=.*/JWT_SIGNING_KEY=$jwt_signing_key/" \
  .env.example > .env

source scripts/lib/settings.sh

if [ -n "${GITHUB_ENV:-}" ]; then
  servicebus_connection="Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;"
  {
    echo "TEST_SQL_CONNECTION_STRING=Server=localhost,$(setting SQL_PORT);User Id=sa;Password=$sql_password;TrustServerCertificate=True;MultipleActiveResultSets=true"
    echo "ServiceBus__ConnectionString=$servicebus_connection"
    echo "ServiceBus__StockEventsConnectionString=$servicebus_connection"
    echo "ServiceBus__QueueName=orders"
    echo "ServiceBus__StockEventsQueueName=stock-events"
  } >> "$GITHUB_ENV"
fi

echo "Created .env with freshly generated passwords"
