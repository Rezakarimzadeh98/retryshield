# Sharing RetryShield

Goal: people searching for double charges, unsafe retries, webhook dedupe, or “idempotency done right” should find a **home**—learn, steal a kit, or run the gateway—not only a Docker binary.

Always link [guarantees](guarantees.md). Never claim magical exactly-once.

## Safe material

- architecture and ADRs
- kit patterns and synthetic demos
- screenshots with tenant/key/host labels removed
- benchmark methodology

Never share `.env`, secrets, raw keys, production payloads, or customer logs.

## Show HN

**Title**

`Show HN: RetryShield – stop double charges when the response never comes back`

**Post**

> I kept seeing the same failure: a write succeeds, the response is lost, the client retries, and money or inventory moves twice.
>
> RetryShield is now three layers: plain-language patterns you can steal in five minutes, copy-paste kit snippets (Node/Express/ASP.NET/FastAPI/webhooks), and a self-hosted idempotency gateway that claims keys in PostgreSQL before forwarding, replays responses, and marks uncertain outcomes instead of guessing.
>
> Looking for people who have shipped that incident—and PRs that add language packs to the kit:
> https://github.com/Rezakarimzadeh98/retryshield

## Reddit / LinkedIn / X

Lead with the human failure (“user paid, phone timed out, tapped again”), then offer two next actions: open `kit/` or run Compose. End with a ask for incident-shaped Discussions or a language-pack PR.

## One-liner

`RetryShield — learn, steal, or deploy the contract that stops unsafe retries when the outcome is uncertain.`
