# Why RetryShield exists

Search for “idempotency key”, “duplicate payment retry”, or “exactly once API” and you will find the same production failure again and again:

1. a client sends a mutation;
2. the upstream commits the side effect;
3. the response is lost;
4. the client retries;
5. the system charges, books, provisions, or writes twice.

Most teams solve this inside one service. That works until the second service appears, until a webhook consumer appears, or until an operator needs to investigate an uncertain outcome without reading application logs by hand.

## What people usually search for

RetryShield is built for people searching for:

- idempotency gateway / idempotency proxy
- `Idempotency-Key` middleware for APIs
- duplicate payment prevention
- safe API retries after timeout
- PostgreSQL-backed idempotency store
- self-hosted Stripe-style idempotency outside one language or framework

## What RetryShield is

RetryShield is a **self-hosted idempotency gateway**.

It sits in front of mutation endpoints, claims the key in PostgreSQL before forwarding, replays completed responses, rejects conflicting payloads, and stops automatic retries when the upstream outcome is unknowable.

It is not a hosted SaaS billing product and it does not claim universal exactly-once delivery. It makes the hard case observable and controllable.

## Why not only Redis?

Redis is excellent for fast coordination. It is a weak authority for money, inventory, and irreversible writes if the process crashes after a network call.

RetryShield keeps PostgreSQL as the source of truth. Redis is optional. If Redis disappears, efficiency can drop, but ownership of a key cannot silently fork.

## Why not only application code?

Application-level idempotency is correct when one team owns one service. It becomes expensive when:

- multiple services need the same contract;
- operators need a shared timeline and reconciliation queue;
- you want one place to enforce body limits, header allowlists, encryption, and retention;
- reviewers want architecture, tests, and runbooks instead of another ad-hoc table.

## Who should use it

- backend and platform engineers protecting payment, order, booking, invoice, or provisioning APIs
- SRE/DevOps teams that need dashboards, alerts, health probes, and Compose/GHCR packaging
- open-source contributors interested in distributed systems correctness, React ops UI, observability, or docs

## Who should contribute

The highest-value contributions are the ones users ask for after they try the demo:

- client SDKs and copy-paste examples
- Kubernetes/Helm packaging
- reconciliation hooks for indeterminate records
- adversarial concurrency tests
- dashboard accessibility and investigation workflows
- docs that turn a production incident into a reproducible guide

Start with [`good first issue`](https://github.com/Rezakarimzadeh98/retryshield/issues?q=is%3Aissue+is%3Aopen+label%3A%22good+first+issue%22) or open a Discussion with the failure mode you actually hit.
