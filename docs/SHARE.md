# Sharing RetryShield

Goal: help people discover the repo when they search for duplicate payments, idempotency keys, safe API retries, or self-hosted gateways—and give them one clear next action: try the demo, star, open a Discussion, or take a `good first issue`.

Describe RetryShield as an idempotency gateway with durable PostgreSQL authority—not as a universal exactly-once system. Always link [guarantees](guarantees.md).

## Safe material

- architecture and ADRs
- synthetic demo traffic and aggregate metrics
- screenshots with tenant, key, host, and account labels removed
- benchmark methodology, hardware, versions, and configuration

Never share `.env` files, connection strings, API keys, encryption keys, raw idempotency keys, request/response bodies, production dashboards, database dumps, or logs containing customer data.

## Show HN

**Title**

`Show HN: RetryShield – self-hosted idempotency gateway for unsafe API retries`

**Post**

> I kept seeing the same failure: an API write succeeds, the response is lost, the client retries, and money/inventory moves twice.
>
> RetryShield is a self-hosted idempotency gateway. It claims an `Idempotency-Key` in PostgreSQL before forwarding, replays completed responses, rejects conflicting payloads, and marks uncertain outcomes as `indeterminate` instead of guessing.
>
> Docker Compose + GHCR images, React ops dashboard, Prometheus/Grafana, PostgreSQL concurrency tests, and a full-stack smoke test that proves one upstream forward and one replay.
>
> Looking for critique from people who have shipped payments/bookings, plus contributors for SDKs, Helm, and reconciliation hooks:
> https://github.com/Rezakarimzadeh98/retryshield

## Reddit

Useful subs: `r/programming`, `r/dotnet`, `r/devops`, `r/selfhosted`, `r/kubernetes`, fintech/engineering communities. Adapt tone; keep the failure concrete.

**Title**

`Open-source idempotency gateway that stops retries when the upstream outcome is unknowable`

**Post**

> If you have ever had a timed-out payment client create a second charge, this is the failure mode.
>
> RetryShield sits in front of mutation APIs, claims keys in PostgreSQL before forwarding, replays exact responses, and surfaces indeterminate outcomes to operators.
>
> Stack: .NET gateway, React dashboard, Postgres/optional Redis, Compose, GHCR, Prometheus alerts, Grafana, concurrency + smoke tests.
>
> Honest about limits: not magical exactly-once. Feedback and focused PRs welcome.
> https://github.com/Rezakarimzadeh98/retryshield

## LinkedIn / X

> API retries are easy until a write succeeds and the response disappears.
>
> I open-sourced RetryShield: a self-hosted idempotency gateway that claims mutation keys in PostgreSQL before forwarding, replays completed responses, and stops automatic retries when the outcome is uncertain.
>
> Try the Docker demo, then tell me which client SDK or Helm chart would unblock your team.
> https://github.com/Rezakarimzadeh98/retryshield

## One-liner for bios and lists

`RetryShield — self-hosted idempotency gateway that makes API retries safe when the outcome is uncertain.`
