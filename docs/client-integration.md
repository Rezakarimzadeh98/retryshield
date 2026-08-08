# Client integration contract

RetryShield prevents duplicate forwarding only when clients reuse a stable
`Idempotency-Key` for every retry of the same logical operation.

## Generate keys at the operation boundary

Generate a cryptographically random key before the first attempt, persist it
with the local operation, and reuse it until the operation reaches a terminal
state. Never derive keys from secrets or reuse one key for unrelated writes.

Good keys include UUIDv4/UUIDv7 values or an opaque operation ID:

```text
Idempotency-Key: 01939d5a-7d8e-7a41-b4cc-1c669c92e530
```

## Response contract

| HTTP result | `Idempotency-Status` | Client action |
| --- | --- | --- |
| Upstream result | `created` | Treat the stored upstream response as authoritative. |
| Stored upstream result | `replayed` | Treat it exactly like the original response. |
| `409 Conflict` | `processing` | Retry later with the same key and identical request. |
| `409 Conflict` | `indeterminate` | Stop automatic retries and escalate for reconciliation. |
| `422 Unprocessable Entity` | `conflict` | Do not retry; the key was reused with different input. |
| `503 Service Unavailable` | `indeterminate` | Stop automatic retries and reconcile the business outcome. |

A transport timeout is not proof that the operation failed. Retry with the same
key; never generate a replacement key after an uncertain attempt.

## Recommended retry policy

1. Keep method, route, content type, query, and body byte-for-byte stable.
2. Retry transport failures and `processing` responses with bounded exponential
   backoff and jitter.
3. Stop after a documented deadline and surface the operation as pending.
4. Never automatically retry an `indeterminate` response.
5. Keep the key longer than the gateway record TTL so delayed retries cannot
   accidentally become a new operation.

## Example

```csharp
using var request = new HttpRequestMessage(HttpMethod.Post, "/proxy/payments");
request.Headers.Add("Idempotency-Key", operation.IdempotencyKey);
request.Content = JsonContent.Create(new { amount = 4200, currency = "USD" });

using var response = await client.SendAsync(request, cancellationToken);
var status = response.Headers.TryGetValues("Idempotency-Status", out var values)
    ? values.Single()
    : null;

if (status == "indeterminate")
{
    // Stop retries. Query the payment provider or send this operation to review.
    throw new InvalidOperationException("Payment outcome requires reconciliation.");
}
```

Do not log raw idempotency keys or request bodies in production. Correlate
operations with a separate non-secret trace or business identifier.
