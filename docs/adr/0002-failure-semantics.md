# ADR 0002: Explicit failure and indeterminate semantics

- Status: Accepted
- Date: 2026-08-08

## Context

Timeouts and connection loss do not prove whether an upstream write occurred. Blind retries can duplicate side effects, while treating every timeout as failure can mislead clients.

## Decision

RetryShield distinguishes rejected-before-dispatch, completed, conflict, and indeterminate outcomes. It retries only when non-execution is known. Once dispatch may have occurred, an unknown result is durably marked indeterminate and returned as an explicit non-success state. Operators reconcile it using upstream evidence; clients do not automatically submit a new key.

Loss of PostgreSQL authority is fail-closed. Loss of Redis degrades performance, not correctness.

If a gateway process stops after claiming a key but before persisting the outcome, the cleanup worker changes the stale `processing` record to `indeterminate` after the configured processing timeout. It never releases the key for an automatic retry. The timeout must remain longer than the maximum upstream request duration.

## Consequences

The API exposes uncertainty instead of claiming exactly-once behavior. Integrations need reconciliation procedures. This may reduce apparent availability but prevents availability mechanisms from weakening duplicate-suppression guarantees.
