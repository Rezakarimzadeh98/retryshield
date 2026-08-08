# Operations

## Production baseline

Run PostgreSQL with automated backups, point-in-time recovery, TLS, and tested restores. Keep the database and Redis on private networks. Terminate TLS and authenticate clients at a trusted edge. Place the admin API/dashboard behind SSO and authorization; the Compose dashboard binds to loopback intentionally.

Set unique values for `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `RETRYSHIELD_ADMIN_API_KEY`, `GRAFANA_ADMIN_PASSWORD`, and a base64 32-byte `RETRYSHIELD_ENCRYPTION_KEY`. Store them in a secret manager, not Compose files or source control. Rotate using a documented dual-key process.

## Health and telemetry

- Liveness should prove the process can serve HTTP; readiness should include required durable dependencies.
- Alert on indeterminate outcomes, conflict-rate changes, claim failures, PostgreSQL saturation, and p95/p99 latency.
- Claims and replays should move together under retry traffic. A sudden replay drop can indicate bypass traffic or lost client keys.
- Scrape `/metrics` privately. Avoid request bodies, credentials, raw keys, and tenant identifiers in metric labels.

## Backup and recovery

Back up PostgreSQL before upgrades. Restore into an isolated environment, verify claim/result row counts and decryptability, then rehearse application cutover. Redis can be rebuilt and must never be used as the recovery authority.

## Upgrades and shutdown

Use rolling deployment only when schema versions are backward compatible. Drain new requests, allow in-flight claims to settle, then stop instances. Apply additive migrations before application rollout and destructive migrations only after all old versions are gone.

## Incident response

Prefer fail-closed behavior when authority is unavailable. Preserve database and gateway logs, note key rotation state, and classify affected requests as complete, conflicting, or indeterminate. Do not replay indeterminate writes automatically. Follow `SECURITY.md` for suspected compromise.
