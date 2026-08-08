# RetryShield guarantees

RetryShield provides request idempotency when clients send a stable, sufficiently random idempotency key and the protected operation is routed through the gateway.

## Contract

- The first valid request claims a key and records a request fingerprint.
- A concurrent request with the same key and fingerprint waits for, or replays, the authoritative result.
- Reusing a key with a different fingerprint is a conflict and must not invoke the upstream.
- Completed responses are replayed for the configured retention period.
- An uncertain upstream outcome is recorded as **indeterminate**; RetryShield does not silently retry it.
- PostgreSQL is the authority. Redis is an optimization and cannot decide ownership or completion.

The guarantee is scoped to one configured RetryShield data plane and its PostgreSQL database. It does not cover calls that bypass the gateway, upstream side effects performed outside the protected request, expired records, or clients that reuse keys incorrectly.

## Client obligations

Use at least 128 bits of entropy, scope keys to the intended tenant/operation, retain a key until a terminal response is known, and treat conflict or indeterminate responses as explicit states. Never derive keys from secrets or personal data.

## Delivery language

RetryShield prevents duplicate execution after it has durably claimed a key. This is not universal “exactly once” delivery: a network failure can make an upstream result unknowable. See [failure semantics](adr/0002-failure-semantics.md).
