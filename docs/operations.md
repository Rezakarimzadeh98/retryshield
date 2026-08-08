# Operations

## Production baseline

Run PostgreSQL with automated backups, point-in-time recovery, TLS, and tested restores. Keep the database and Redis on private networks. Terminate TLS and authenticate clients at a trusted edge. Place the admin API/dashboard behind SSO and authorization; the Compose dashboard binds to loopback intentionally.

Set `UPSTREAM_BASE_URL` to the absolute HTTP(S) base URL of the protected production service. Set unique values for `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `RETRYSHIELD_ADMIN_API_KEY`, `GRAFANA_ADMIN_PASSWORD`, and a base64 32-byte `RETRYSHIELD_ENCRYPTION_KEY`. Store secrets in a secret manager, not Compose files or source control. Rotate encryption keys using a documented dual-key process.

The production overlay keeps the gateway, admin API/dashboard, PostgreSQL, Redis, Prometheus, and Grafana, while excluding the demo upstream. From the repository root:

```sh
docker compose --env-file deploy/.env \
  -f deploy/compose.yml \
  -f deploy/compose.production.yml \
  -f deploy/compose.release.yml \
  up -d --wait
```

Omit `compose.release.yml` and add `--build` to build application images locally. Do not enable the `demo` profile in production. Validate the fully merged configuration before rollout with the same file order and `config -q`.

## Health and telemetry

- Liveness should prove the process can serve HTTP; readiness should include required durable dependencies.
- Alert on indeterminate outcomes, conflict-rate changes, claim failures, PostgreSQL saturation, and p95/p99 latency.
- Claims and replays should move together under retry traffic. A sudden replay drop can indicate bypass traffic or lost client keys.
- Scrape `/metrics` privately. Avoid request bodies, credentials, raw keys, and tenant identifiers in metric labels.

Prometheus loads `deploy/prometheus/alerts.yml`. The bundled rules alert when the gateway scrape target is down for two minutes, any indeterminate outcome occurs, conflicts exceed 5% with a minimum traffic volume, p95 latency exceeds one second, or replay counters exceed request counters. The Compose stack evaluates and displays these alerts but does not include Alertmanager; configure Prometheus with your external Alertmanager before relying on notifications. Route `critical` alerts to paging and `warning` alerts to the owning service team. Tune thresholds and durations against the service-level objectives and normal traffic before rollout.

The available gateway metrics cannot determine whether a low replay count is anomalous because they do not expose expected retries or bypass traffic. Monitor replay-to-claim trends on the dashboard and correlate unexpected changes with edge and client telemetry. PostgreSQL saturation and claim-failure alerts likewise require database/exporter metrics not included in this Compose stack.

## Backup and recovery

Back up PostgreSQL before upgrades. Restore into an isolated environment, verify claim/result row counts and decryptability, then rehearse application cutover. Redis can be rebuilt and must never be used as the recovery authority.

## Upgrades and shutdown

RetryShield records applied versions in `retryshield_schema_migrations`. Gateway and Admin API startup serialize migrations with a PostgreSQL advisory transaction lock, so concurrent replicas cannot apply the same migration twice. Startup fails closed when the database schema is newer than the running application.

Use rolling deployment only when schema versions are backward compatible. Back up PostgreSQL, deploy one new instance, confirm the migration version and readiness, then continue the rollout. Drain new requests, allow in-flight claims to settle, and stop old instances only after the new version is healthy. Future destructive migrations must be split into expand/migrate/contract releases and applied only after all old versions are gone.

## Incident response

Prefer fail-closed behavior when authority is unavailable. Preserve database and gateway logs, note key rotation state, and classify affected requests as complete, conflicting, or indeterminate. Do not replay indeterminate writes automatically. Follow `SECURITY.md` for suspected compromise.
