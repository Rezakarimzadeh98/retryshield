# FAQ

## Is RetryShield an exactly-once system?

No. It provides durable request idempotency for a configured gateway and PostgreSQL database. It makes duplicates observable and prevents automatic retries when the outcome is unknowable. Read [guarantees](guarantees.md).

## Does it prevent duplicate payments?

It prevents the gateway from forwarding the same logical mutation more than once for a stable `Idempotency-Key`. It cannot protect calls that bypass the gateway, expire after retention, or reuse keys incorrectly. Clients must keep the same key across retries.

## Why PostgreSQL instead of Redis?

PostgreSQL is the authority for claim ownership and stored responses. Redis is optional pub/sub acceleration. A Redis outage must not create a second owner for the same key.

## Can I put it in front of any API?

Only a fixed, trusted upstream origin configured at startup. RetryShield is intentionally not an open proxy. Point `UPSTREAM_BASE_URL` at the service you protect.

## What happens if the upstream succeeds and the response is lost?

The record becomes `indeterminate`. Clients must stop inventing new keys. Operators investigate and resolve through the admin API/dashboard.

## Is this only for .NET teams?

No. The gateway speaks HTTP. Clients in any language can send `Idempotency-Key`. The current implementation is .NET; SDK helpers for other languages are welcome contributions.

## How do I try it in under five minutes?

Use the released Compose images in the README. Send the same payment twice and confirm the upstream counter stays at `1`.

## How do I contribute without being a distributed-systems expert?

Docs, client examples, dashboard accessibility, Grafana panels, and issue reproductions are valuable. Look for `good first issue` labels.
