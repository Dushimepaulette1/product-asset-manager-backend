#!/usr/bin/env bash
#
# Container Smoke Test.
#
# Positive: start the whole stack, wait for /health/ready, then expect
#           GET /api/products/{id} for the seeded demo product to return 200.
# Negative: stop the database container, then expect /health/ready to return
#           503 while /health/live still returns 200.
#
# The stack runs under its own Compose project name and is always removed at
# the end (including its volume), so it never touches your everyday stack.
# Stop your everyday stack first if it uses the same ports.
#
# Usage: scripts/smoke-test.sh
# Optional: DOCKER=docker.exe (WSL without Docker integration), API_PORT=8080,
#           SMOKE_TEST_SKIP_BUILD=true to test the product-asset-manager:local
#           image that is already built instead of building it again (CI).

set -euo pipefail

cd "$(dirname "$0")/.."
source scripts/lib/settings.sh

DOCKER="${DOCKER:-docker}"
PROJECT_NAME="${SMOKE_PROJECT_NAME:-pam-smoke-test}"
API_PORT="${API_PORT:-$(setting API_PORT)}"
BASE_URL="http://localhost:${API_PORT:-8080}"
DEMO_PRODUCT_ID="22222222-2222-2222-2222-222222222222"
STARTUP_TIMEOUT_SECONDS="${STARTUP_TIMEOUT_SECONDS:-600}"

failures=0

compose() {
  "$DOCKER" compose --project-name "$PROJECT_NAME" --env-file "$SETTINGS_FILE" "$@"
}

teardown() {
  echo "Removing the smoke-test stack"
  compose down --volumes --remove-orphans > /dev/null 2>&1 || true
}
trap teardown EXIT

status_of() {
  curl --silent --output /dev/null --max-time 15 --write-out '%{http_code}' "$BASE_URL$1" || true
}

wait_for_status() {
  local path=$1 expected=$2 timeout=$3
  local deadline=$((SECONDS + timeout))
  until [ "$(status_of "$path")" = "$expected" ]; do
    if [ "$SECONDS" -ge "$deadline" ]; then
      return 1
    fi
    sleep 3
  done
}

check() {
  local description=$1 path=$2 expected=$3
  local actual
  actual=$(status_of "$path")
  if [ "$actual" = "$expected" ]; then
    echo "PASS  $description (GET $path -> $actual)"
  else
    echo "FAIL  $description (GET $path -> $actual, expected $expected)"
    failures=$((failures + 1))
  fi
}

BUILD_OPTION="--build"
if [ "${SMOKE_TEST_SKIP_BUILD:-false}" = "true" ]; then
  BUILD_OPTION="--no-build"
fi

echo "Starting the stack with $SETTINGS_FILE (this can take a few minutes the first time)"
compose up --detach "$BUILD_OPTION"

echo "Waiting for $BASE_URL/health/ready"
if ! wait_for_status /health/ready 200 "$STARTUP_TIMEOUT_SECONDS"; then
  echo "FAIL  the API never became ready"
  compose ps --all
  compose logs --tail 50 api migrator
  exit 1
fi

echo
echo "Positive test"
check "the seeded demo product is served" "/api/products/$DEMO_PRODUCT_ID" 200

echo
echo "Negative test: stopping the database container"
compose stop sqlserver > /dev/null
wait_for_status /health/ready 503 60 || true
check "ready reports the database is gone" /health/ready 503
check "live still reports the process is running" /health/live 200

echo
if [ "$failures" -eq 0 ]; then
  echo "Smoke test passed"
else
  echo "Smoke test failed: $failures check(s) failed"
  exit 1
fi
