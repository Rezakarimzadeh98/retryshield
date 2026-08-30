// ASP.NET filter sketch — single-process / learning only.
// Prefer RetryShield gateway when multiple instances share the API.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

public sealed class IdempotencyFilter : IAsyncActionFilter
{
    private static readonly ConcurrentDictionary<string, Entry> Store = new();

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues))
        {
            context.Result = new BadRequestObjectResult(new { error = "Idempotency-Key required" });
            return;
        }

        var key = keyValues.ToString();
        context.HttpContext.Request.EnableBuffering();
        using var reader = new StreamReader(context.HttpContext.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.HttpContext.Request.Body.Position = 0;

        var fingerprint = Sha256($"{context.HttpContext.Request.Method}:{context.HttpContext.Request.Path}:{body}");

        if (Store.TryGetValue(key, out var existing))
        {
            if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                context.HttpContext.Response.Headers["Idempotency-Status"] = "conflict";
                context.Result = new UnprocessableEntityObjectResult(new { error = "conflict" });
                return;
            }

            if (existing.Status == "completed")
            {
                context.HttpContext.Response.Headers["Idempotency-Status"] = "replayed";
                context.Result = new ContentResult
                {
                    StatusCode = existing.StatusCode,
                    Content = existing.Body,
                    ContentType = "application/json",
                };
                return;
            }

            context.HttpContext.Response.Headers["Idempotency-Status"] = "processing";
            context.Result = new ConflictObjectResult(new { error = "processing" });
            return;
        }

        Store[key] = new Entry(fingerprint, "processing", 0, null);

        var executed = await next();
        if (executed.Result is ObjectResult objectResult)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(objectResult.Value);
            Store[key] = new Entry(fingerprint, "completed", objectResult.StatusCode ?? 200, json);
            context.HttpContext.Response.Headers["Idempotency-Status"] = "created";
        }
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private sealed record Entry(string Fingerprint, string Status, int StatusCode, string? Body);
}
