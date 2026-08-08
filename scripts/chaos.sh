#!/usr/bin/env bash
set -euo pipefail

COMPOSE_FILE="${COMPOSE_FILE:-deploy/compose.yml}"
ENV_FILE="${ENV_FILE:-deploy/.env}"
FAULT="${1:-postgres-restart}"
RECOVERY_SECONDS="${RECOVERY_SECONDS:-10}"

compose() { docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"; }

case "$FAULT" in
  postgres-restart)
    echo "Stopping PostgreSQL for ${RECOVERY_SECONDS}s..."
    compose stop postgres
    trap 'compose start postgres >/dev/null' EXIT INT TERM
    sleep "$RECOVERY_SECONDS"
    compose start postgres
    trap - EXIT INT TERM
    ;;
  redis-restart)
    echo "Stopping Redis for ${RECOVERY_SECONDS}s..."
    compose stop redis
    trap 'compose start redis >/dev/null' EXIT INT TERM
    sleep "$RECOVERY_SECONDS"
    compose start redis
    trap - EXIT INT TERM
    ;;
  upstream-restart)
    echo "Restarting the demo upstream..."
    compose restart demo-upstream
    ;;
  *)
    echo "Usage: $0 {postgres-restart|redis-restart|upstream-restart}" >&2
    exit 2
    ;;
esac

echo "Waiting for services to recover..."
compose up -d --wait
compose ps
