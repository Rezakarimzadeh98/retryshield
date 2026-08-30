# Queue / outbox redelivery sketch

HTTP retries are not the only double-execution path. Message brokers deliver **at least once**. After a crash, the same message comes back.

## Rules

1. Derive a stable idempotency key from the message (`message_id`, `event_id`, or a hash of business identity).  
2. **Claim** that key in your database before side effects.  
3. If the claim already completed → ack and exit.  
4. If processing → do not start a second side effect (nack/retry only if your policy requires it).  
5. Commit “completed” in the **same transaction** as the business write when possible (transactional outbox / inbox).

## Inbox claim (SQL)

```sql
INSERT INTO processed_messages (message_id, status, claimed_at)
VALUES ($1, 'processing', now())
ON CONFLICT (message_id) DO NOTHING
RETURNING message_id;

-- no row → already seen: SELECT status; if completed, ACK; if processing, decide retry policy
-- row returned → run handler, then:
UPDATE processed_messages SET status = 'completed', completed_at = now()
WHERE message_id = $1;
```

## Outbox (publish after commit)

```text
1. BEGIN
2. Write business row (order paid, …)
3. INSERT into outbox (id, payload, status='pending')
4. COMMIT
5. Publisher reads pending rows, sends to broker, marks 'sent'
6. Consumer uses inbox claim above so redelivery is safe
```

## How this relates to RetryShield

| Surface | Tool |
| --- | --- |
| Browser/mobile retry of your HTTP API | [RetryShield gateway](../../README.md#quick-start) or HTTP kit snippets |
| Webhook from Stripe/etc. | [`webhook-dedupe.md`](webhook-dedupe.md) |
| Queue / outbox | this sketch (durable claim in **your** DB) |

Same contract everywhere: **claim → side effect → store outcome → replay or stop**.
