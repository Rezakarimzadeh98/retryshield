# Pattern kit

Steal these first. Graduate to the [gateway quick start](../README.md#quick-start) when you need a shared edge in front of many services.

## Start here

1. [The failure](patterns/01-the-failure.md) — what goes wrong in plain language  
2. [The contract](patterns/02-idempotency-contract.md) — rules every implementation must obey  
3. Snippets below — drop into an app today  

When in-app storage is not enough (multi-instance races, shared APIs, operator workflows), run RetryShield as the gateway.

## Snippets

| File | Use when |
| --- | --- |
| [`snippets/node-operation-store.js`](snippets/node-operation-store.js) | Node client: one key per logical payment/order |
| [`snippets/express-middleware.js`](snippets/express-middleware.js) | Express: in-process claim + replay (single instance / learning) |
| [`snippets/aspnet-filter.cs`](snippets/aspnet-filter.cs) | ASP.NET: same contract as a filter sketch |
| [`snippets/fastapi-dependency.py`](snippets/fastapi-dependency.py) | FastAPI: dependency-style claim sketch |
| [`snippets/go-middleware.go`](snippets/go-middleware.go) | Go `net/http`: claim + replay sketch |
| [`snippets/webhook-dedupe.md`](snippets/webhook-dedupe.md) | Provider webhooks that can arrive twice |
| [`snippets/outbox-or-queue.md`](snippets/outbox-or-queue.md) | Queue redelivery / transactional outbox inbox |

## Hard limits of the kit

In-memory and single-process stores **do not** survive multi-instance deploys or process crashes the way PostgreSQL authority does. The kit teaches the contract. The gateway enforces it under concurrency.

See [guarantees](../docs/guarantees.md) and [client integration](../docs/client-integration.md).
