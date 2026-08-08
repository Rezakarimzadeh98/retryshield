# ADR 0001: PostgreSQL is the correctness authority

- Status: Accepted
- Date: 2026-08-08

## Context

Claim ownership, request fingerprints, terminal results, and indeterminate outcomes must survive process and cache loss. Two gateway instances can race for the same key.

## Decision

PostgreSQL constraints and transactions are authoritative for every state transition. A unique scoped idempotency key elects one owner. Redis may cache completed results or coordinate wake-ups, but a cache hit is validated as required and a cache miss never grants ownership.

## Consequences

Correctness survives Redis loss and gateway restarts. Claim latency and capacity depend on PostgreSQL, so indexes, connection limits, vacuum, backups, and failover require operational attention. During loss of database authority, writes fail closed.
