# Local demo

## Start

Create `deploy/.env` (never commit it):

```dotenv
POSTGRES_PASSWORD=replace-with-a-long-random-value
REDIS_PASSWORD=replace-with-a-different-long-random-value
UPSTREAM_BASE_URL=http://demo-upstream:8080/
RETRYSHIELD_ADMIN_API_KEY=replace-with-a-random-api-key
RETRYSHIELD_ENCRYPTION_KEY=replace-with-32-bytes-base64
GRAFANA_ADMIN_PASSWORD=replace-with-a-random-password
```

From the repository root:

```sh
docker compose --env-file deploy/.env \
  --profile demo -f deploy/compose.yml -f deploy/compose.release.yml up -d --wait
```

This pulls the published `amd64`/`arm64` images. The `demo` profile is deliberately
opt-in. To compile the stack locally, run
`docker compose --env-file deploy/.env --profile demo -f deploy/compose.yml up --build --wait`.

Gateway, admin API, dashboard, demo upstream, Prometheus, and Grafana bind only to localhost on ports 8080, 8081, 3000, 8082, 9090, and 3001 respectively.

## Exercise concurrency

Install k6, then run:

```sh
k6 run -e BASE_URL=http://localhost:8080 -e RUN_ID="$(date +%s)" scripts/load/50-concurrency.js
```

All 50 virtual users share one idempotency key. The threshold requires exactly one `created` response; the other successful requests are durable replays. Confirm the upstream count separately:

```sh
curl http://localhost:8082/payments/count
```

## Stop

`docker compose --env-file deploy/.env --profile demo -f deploy/compose.yml down` preserves volumes. Add `--volumes` only when you intentionally want to erase local state.
