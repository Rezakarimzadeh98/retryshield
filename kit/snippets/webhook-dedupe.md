# Webhook dedupe pattern

Payment providers and SaaS webhooks often deliver the **same event more than once**. That is normal. Your handler must treat delivery as at-least-once.

## Rules

1. Prefer the provider’s unique event id (`event.id`, `delivery_id`, etc.) as the idempotency key.  
2. Claim that id in your database **before** applying side effects.  
3. If the claim already exists and completed, return `200` quickly (acknowledge, do not redo work).  
4. If processing is in flight, return a retryable status only if your provider expects it—or wait and ack when safe.  
5. Verify signatures **before** claim when the provider requires it.

## Minimal SQL sketch

```sql
-- claim
INSERT INTO webhook_receipts (event_id, status, received_at)
VALUES ($1, 'processing', now())
ON CONFLICT (event_id) DO NOTHING
RETURNING event_id;

-- if no row returned: already seen → load status and exit without side effects
-- if row returned: run handler, then UPDATE status = 'completed'
```

## How this relates to RetryShield

- Webhook handlers are often **inside** your app: use a durable claim table (or the kit contract).  
- Browser/mobile **retries of your own mutation APIs** are where the [RetryShield gateway](../../README.md#quick-start) shines.  
- Same mental model: **claim → side effect → store outcome → replay or stop**.
