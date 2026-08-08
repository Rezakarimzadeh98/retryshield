# Chaos testing

Run chaos tests only in an isolated, disposable environment. Keep a second terminal open with the k6 script or representative traffic:

```sh
RECOVERY_SECONDS=15 scripts/chaos.sh postgres-restart
RECOVERY_SECONDS=15 scripts/chaos.sh redis-restart
scripts/chaos.sh upstream-restart
```

The script restores stopped dependencies on normal exit and signals, then waits for Compose health checks. Host termination or Docker failure can still prevent cleanup; run `docker compose -f deploy/compose.yml up -d --wait` to recover.

## Expected observations

- **PostgreSQL unavailable:** new claims fail closed; no upstream call occurs without a durable claim.
- **Redis unavailable:** correctness remains intact, with increased database load/latency.
- **Upstream restart:** requests known not to have reached upstream may fail safely; ambiguous requests become indeterminate and are not automatically retried.
- **Recovery:** existing completed keys replay their prior result and conflicting payloads remain conflicts.

Capture Prometheus/Grafana data and gateway, PostgreSQL, and upstream logs. Verify upstream side-effect counts directly; HTTP success counts alone do not prove duplicate suppression.
