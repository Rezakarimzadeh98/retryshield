# Vision: the home for safe operations

RetryShield started as a self-hosted idempotency gateway. That product remains the production path.

The larger problem is broader:

> Important work either runs twice, disappears, or finishes in a state nobody can prove.

That shows up as double charges, duplicate orders, repeated webhooks, redelivered queue messages, and background jobs that crash mid-flight. Idempotency is one tool. The project’s job is to be the place developers go when that class of failure matters.

## What this repository is

Three layers on one contract:

| Layer | Who it serves | What you get |
| --- | --- | --- |
| **Learn** | anyone who hit the bug once | plain-language failure stories and rules |
| **Kit** | everyday backend work | copy-paste patterns and small helpers you can take into your app today |
| **Gateway** | teams that need a shared edge | PostgreSQL-backed claim, replay, indeterminate ops, Compose/GHCR |

The gateway is the deep end. The kit is the front door. The docs are the shared language.

## What success looks like

Someone lands here and thinks one of:

- “I do not need to reinvent this poorly.”
- “I can steal a working pattern in five minutes.”
- “I can run the real gateway when we outgrow in-app checks.”
- “I can extend one sharp edge and ship a PR.”

Stars without use are vanity. Use, issue reports shaped like real incidents, and small language packs are the signal.

## Non-goals

- promising magical exactly-once delivery
- replacing Stripe, Kafka, or a full API gateway
- becoming an observability platform
- shipping twenty half-finished SDKs before one excellent five-minute path

## Honest scope

Writing a Map check in one service is fine for a single app. This project exists when you want:

- a contract that survives concurrency and lost responses;
- the same rules across clients, services, and operators;
- a production gateway when “each service rolled its own” becomes the incident.

Read [guarantees](guarantees.md) before production use.
