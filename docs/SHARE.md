# Sharing RetryShield

When presenting or publishing RetryShield, describe it as an idempotency gateway with durable PostgreSQL authority—not as a universal exactly-once system.

Safe material to share:

- architecture and ADRs;
- synthetic demo traffic and aggregate metrics;
- screenshots with tenant, key, host, and account labels removed;
- benchmark methodology, hardware, versions, and configuration.

Never share `.env` files, connection strings, API keys, encryption keys, raw idempotency keys, request/response bodies, production dashboards, database dumps, or logs containing customer data.

Before publishing a benchmark, run multiple trials, report error rates and latency percentiles, distinguish fresh claims from replays, and state whether PostgreSQL and the upstream were local. Link to `docs/guarantees.md` so limitations travel with performance claims.

Use the project name and logo only in ways that do not imply endorsement. Follow the repository license and attribute third-party components.

## Show HN

**Title**

`Show HN: RetryShield – a self-hosted idempotency gateway for uncertain API retries`

**Post**

> I built RetryShield after focusing on a specific distributed-systems failure: an upstream commits a mutation, its response is lost, and the client retries.
>
> RetryShield claims an idempotency key in PostgreSQL before forwarding. Identical completed requests replay the stored response; a changed payload returns 422; concurrent duplicates wait briefly; ambiguous outcomes become `indeterminate` instead of being retried automatically.
>
> It includes a .NET gateway, React operations console, PostgreSQL/optional Redis, Prometheus/Grafana, a payment demo, and a 50-client k6 exercise. The README is explicit about why this is not universal exactly-once delivery.
>
> I would especially value feedback on the failure semantics, PostgreSQL claim transaction, and reconciliation workflow:
> https://github.com/Rezakarimzadeh98/retryshield

## Reddit

**Title**

`I built an open-source idempotency gateway that stops retries when the outcome is unknowable`

**Post**

> RetryShield is a self-hosted reverse proxy for mutation endpoints. PostgreSQL is the authority; Redis is optional and cannot decide ownership. It handles exact response replay, payload conflicts, concurrent duplicates, encrypted retained bodies, and explicit operator resolution for indeterminate outcomes.
>
> The repository includes Docker Compose, a demo payment API, OpenTelemetry/Prometheus instrumentation, Grafana, chaos scripts, and concurrency tests. I am looking for technical critique and focused contributions—not claiming universal exactly-once behavior.
>
> Repo and architecture: https://github.com/Rezakarimzadeh98/retryshield

## LinkedIn

> API retries are easy until a write succeeds and the response disappears.
>
> I built **RetryShield**, an open-source, self-hosted idempotency gateway that claims mutation keys in PostgreSQL before forwarding, replays completed responses, rejects conflicting payloads, and surfaces uncertain outcomes for operator reconciliation.
>
> The project combines a .NET clean architecture, React operations console, optional Redis, AES-GCM storage protection, OpenTelemetry/Prometheus/Grafana, Docker Compose, and concurrency/chaos exercises.
>
> The important design choice: it does not hide uncertainty behind an “exactly once” claim.
>
> https://github.com/Rezakarimzadeh98/retryshield
