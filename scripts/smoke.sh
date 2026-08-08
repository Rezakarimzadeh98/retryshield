#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

export POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-smoke-postgres-password}"
export REDIS_PASSWORD="${REDIS_PASSWORD:-smoke-redis-password}"
export RETRYSHIELD_ADMIN_API_KEY="${RETRYSHIELD_ADMIN_API_KEY:-smoke-admin-token-change-me}"
export RETRYSHIELD_ENCRYPTION_KEY="${RETRYSHIELD_ENCRYPTION_KEY:-bG9jYWwtZGV2ZWxvcG1lbnQta2V5LTMyLWJ5dGVzISE=}"
export GRAFANA_ADMIN_PASSWORD="${GRAFANA_ADMIN_PASSWORD:-smoke-grafana-password}"
export UPSTREAM_BASE_URL="http://demo-upstream:8080/"
export COMPOSE_PROJECT_NAME="${COMPOSE_PROJECT_NAME:-retryshield-smoke-${GITHUB_RUN_ID:-local}}"

compose=(docker compose --profile demo -f deploy/compose.yml)
work="$(mktemp -d)"

cleanup() {
  status=$?
  if (( status != 0 )); then
    "${compose[@]}" ps || true
    "${compose[@]}" logs --no-color || true
  fi
  "${compose[@]}" down --volumes --remove-orphans || true
  rm -rf "$work"
  exit "$status"
}
trap cleanup EXIT

"${compose[@]}" up --build --detach --wait --wait-timeout 240 \
  gateway demo-upstream admin-api admin-dashboard

key="smoke-${GITHUB_RUN_ID:-local}-${RANDOM}"
payload='{"amount":4200,"currency":"USD"}'

curl --fail-with-body --silent --show-error \
  --dump-header "$work/first.headers" --output "$work/first.body" \
  -X POST http://127.0.0.1:8080/proxy/payments \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $key" \
  --data "$payload"

curl --fail-with-body --silent --show-error \
  --dump-header "$work/second.headers" --output "$work/second.body" \
  -X POST http://127.0.0.1:8080/proxy/payments \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $key" \
  --data "$payload"

first_status="$(tr -d '\r' < "$work/first.headers" | awk -F': ' 'tolower($1) == "idempotency-status" { print tolower($2) }')"
second_status="$(tr -d '\r' < "$work/second.headers" | awk -F': ' 'tolower($1) == "idempotency-status" { print tolower($2) }')"

if [[ "$first_status" != "created" ]]; then
  echo "Expected first response to be created, got '$first_status'." >&2
  exit 1
fi
if [[ "$second_status" != "replayed" ]]; then
  echo "Expected second response to be replayed, got '$second_status'." >&2
  exit 1
fi
if ! cmp --silent "$work/first.body" "$work/second.body"; then
  echo "Replayed response body differs from the original." >&2
  exit 1
fi

count="$(
  curl --fail-with-body --silent --show-error http://127.0.0.1:8082/payments/count |
    python -c 'import json,sys; print(json.load(sys.stdin)["count"])'
)"
if [[ "$count" != "1" ]]; then
  echo "Expected exactly one upstream payment, got '$count'." >&2
  exit 1
fi

curl --fail --silent --show-error http://127.0.0.1:8080/health/ready >/dev/null
curl --fail --silent --show-error http://127.0.0.1:8081/health/ready >/dev/null
curl --fail --silent --show-error http://127.0.0.1:3000/healthz >/dev/null

echo "RetryShield smoke test passed: one forward and one exact replay."
