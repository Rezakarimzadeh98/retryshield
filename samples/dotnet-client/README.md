# Retry-safe .NET client example

This sample shows the minimum contract a paying client must follow:

1. create one idempotency key per logical operation;
2. reuse it on every retry;
3. never invent a replacement key after an indeterminate outcome.

```csharp
using System.Net.Http.Json;

public sealed class RetrySafePaymentClient(HttpClient http)
{
    public async Task<HttpResponseMessage> ChargeAsync(
        Guid operationId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/proxy/payments");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", operationId.ToString("D"));
        request.Content = JsonContent.Create(new { amount, currency });

        var response = await http.SendAsync(request, cancellationToken);
        var status = response.Headers.TryGetValues("Idempotency-Status", out var values)
            ? values.SingleOrDefault()
            : null;

        if (status is "indeterminate")
        {
            throw new InvalidOperationException(
                "Payment outcome is indeterminate. Stop automatic retries and reconcile.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict &&
            status == "processing")
        {
            // Caller should backoff and retry with the same operationId.
            return response;
        }

        return response;
    }
}
```

## Rules that prevent double charges

- Persist `operationId` with the business operation before the first attempt.
- Keep method, path, query, content type, and body stable across retries.
- Treat `replayed` as success with the original body.
- Treat `conflict` (`422`) as a programming error: key reuse with different input.
- Treat `indeterminate` as an incident, not a transient retry signal.

See [client-integration.md](../docs/client-integration.md) for the full contract.
