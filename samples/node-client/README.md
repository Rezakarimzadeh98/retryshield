# Retry-safe Node.js client example

This sample shows the minimum contract a client must follow:

1. create one idempotency key per logical operation;
2. persist it before the first attempt;
3. reuse it on every retry;
4. never invent a replacement key after an indeterminate outcome.

```js
const { randomUUID } = require("node:crypto");

const BASE_URL = "http://localhost:8080";

// In a real application, replace this Map with durable persistence.
const operations = new Map();

function createPaymentOperation(amount, currency) {
    const operation = {
        id: randomUUID(),
        idempotencyKey: randomUUID(),
        amount,
        currency,
    };

    // Persist the operation before the first attempt.
    operations.set(operation.id, operation);
    return operation;
}

async function chargePayment(operationId) {
    const operation = operations.get(operationId);
    if (!operation) {
        throw new Error(`Unknown operation: ${operationId}`);
    }

    let attempt = 0;
    const deadline = Date.now() + 30_000;

    while (Date.now() < deadline) {
        attempt += 1;

        let response;

        try {
            response = await fetch(`${BASE_URL}/proxy/payments`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Idempotency-Key": operation.idempotencyKey,
                },
                body: JSON.stringify({
                    amount: operation.amount,
                    currency: operation.currency,
                }),
            });
        } catch (error) {
            // The request may have reached the server before the transport failed.
            // Retry with the same key, never a replacement key.
            await backoff(attempt);
            continue;
        }

        const status = response.headers.get("Idempotency-Status");

        if (status === "created" || status === "replayed") {
            return response;
        }

        if (response.status === 409 && status === "processing") {
            await backoff(attempt);
            continue;
        }

        if (status === "indeterminate") {
            throw new Error(
                "Payment outcome is indeterminate. Stop automatic retries and reconcile."
            );
        }

        if (response.status === 422 && status === "conflict") {
            throw new Error(
                "Idempotency key was reused with different input. Do not retry."
            );
        }

        return response;
    }

    throw new Error("Payment is still pending after the retry deadline.");
}

async function backoff(attempt) {
    const exponential = Math.min(1000 * 2 ** (attempt - 1), 5000);
    const jitter = Math.random() * 250;

    await new Promise((resolve) =>
        setTimeout(resolve, exponential + jitter)
    );
}

const operation = createPaymentOperation(4200, "USD");
const response = await chargePayment(operation.id);

console.log(response.status);
console.log(await response.text());
```
## Rules that prevent double charges

- Persist the operation and its `idempotencyKey` before the first attempt.
- Keep method, path, query, content type, and body stable across retries.
- Treat `created` and `replayed` as successful outcomes.
- Retry `processing` responses with the same key.
- Retry transport failures with the same key because a timeout does not prove failure.
- Treat `indeterminate` as an incident requiring reconciliation, not as a retry signal.
- Treat `422` with `conflict` as a programming error: the key was reused with different input.
- Never generate a replacement key after an uncertain attempt.

The `Map` above represents the application's operation store. In production, use durable persistence so the key survives process restarts.

See [client-integration.md](../../docs/client-integration.md) for the full contract.
