#!/usr/bin/env bash
#
# Observed Load Test: the Stage 3 load buffering scenario against the running
# Docker Compose stack, so its effect can be watched in Grafana.
#
# Sets the demo chair's stock to 6, fires 20 purchases at the same moment,
# waits for every order to resolve, and expects exactly 6 Confirmed and
# 14 Rejected. Start the stack first with "docker compose up -d".
#
# Usage: scripts/observed-load-test.sh

set -euo pipefail

cd "$(dirname "$0")/.."

ENV_FILE=".env"
[ -f "$ENV_FILE" ] || ENV_FILE=".env.example"
setting() { grep -E "^$1=" "$ENV_FILE" | cut -d= -f2-; }

BASE_URL="http://localhost:$(setting API_PORT)"
ADMIN_EMAIL="$(setting SEED_ADMIN_EMAIL)"
ADMIN_PASSWORD="$(setting SEED_ADMIN_PASSWORD)"
SKU="DEMO-CHAIR-OAK"
VARIANT_ID="33333333-3333-3333-3333-333333333333"
STOCK=6
PURCHASES=20
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

token_for() {
  curl --silent --fail -H "Content-Type: application/json" \
    -d "{\"email\":\"$1\",\"password\":\"$2\"}" "$BASE_URL/api/auth/login" \
    | sed -E 's/.*"token":"([^"]+)".*/\1/'
}

echo "Setting $SKU stock to $STOCK"
admin_token=$(token_for "$ADMIN_EMAIL" "$ADMIN_PASSWORD")
curl --silent --fail --output /dev/null -X PATCH \
  -H "Authorization: Bearer $admin_token" -H "Content-Type: application/json" \
  -d "{\"quantity\":$STOCK}" "$BASE_URL/api/variants/$SKU/stock"

shopper_email="load-test-$(date +%s)@example.com"
curl --silent --fail --output /dev/null -H "Content-Type: application/json" \
  -d "{\"name\":\"Load Test\",\"email\":\"$shopper_email\",\"password\":\"LoadTest_123!\"}" \
  "$BASE_URL/api/auth/register"
shopper_token=$(token_for "$shopper_email" "LoadTest_123!")

echo "Firing $PURCHASES purchases at the same time"
for i in $(seq 1 "$PURCHASES"); do
  curl --silent -H "Authorization: Bearer $shopper_token" -H "Content-Type: application/json" \
    -d "{\"variantId\":\"$VARIANT_ID\",\"quantity\":1}" \
    "$BASE_URL/api/orders" > "$WORK_DIR/order-$i.json" &
done
wait

order_ids=$(cat "$WORK_DIR"/order-*.json | grep -oE '[0-9a-f-]{36}')
echo "Accepted: $(echo "$order_ids" | wc -l) orders"

echo "Waiting for the consumer to resolve them"
deadline=$((SECONDS + 120))
while :; do
  confirmed=0; rejected=0; pending=0
  for id in $order_ids; do
    status=$(curl --silent -H "Authorization: Bearer $shopper_token" "$BASE_URL/api/orders/$id" \
      | grep -oE '"status":"[A-Za-z]+"' | cut -d'"' -f4)
    case "$status" in
      Confirmed) confirmed=$((confirmed + 1)) ;;
      Rejected) rejected=$((rejected + 1)) ;;
      *) pending=$((pending + 1)) ;;
    esac
  done
  if [ "$pending" -eq 0 ] || [ "$SECONDS" -ge "$deadline" ]; then
    break
  fi
  sleep 2
done

echo
echo "Confirmed: $confirmed  Rejected: $rejected  Still pending: $pending"

if [ "$confirmed" -eq "$STOCK" ] && [ "$rejected" -eq $((PURCHASES - STOCK)) ]; then
  echo "As expected: exactly $STOCK confirmed and $((PURCHASES - STOCK)) rejected."
  echo "Open Grafana, dashboard 'Product Asset Manager', with a time range covering the last few minutes."
else
  echo "Unexpected result."
  exit 1
fi
