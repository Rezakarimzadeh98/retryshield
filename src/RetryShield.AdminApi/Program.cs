using System.Security.Cryptography;
using System.Text;
using RetryShield.Application;
using RetryShield.Domain;
using RetryShield.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddRetryShieldInfrastructure(builder.Configuration);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration.GetSection("Admin:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader().AllowAnyMethod()));
var adminToken = builder.Configuration["Admin:BearerToken"];
if (string.IsNullOrWhiteSpace(adminToken) || adminToken.Length < 16)
    throw new InvalidOperationException("Admin:BearerToken must contain at least 16 characters.");

var app = builder.Build();
await app.Services.InitializeRetryShieldSchemaAsync();
app.UseCors();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/openapi") ||
        context.Request.Path.StartsWithSegments("/health")) { await next(); return; }
    var supplied = context.Request.Headers.Authorization.ToString();
    var expected = "Bearer " + adminToken;
    var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
    var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    var valid = CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    if (!valid) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
    await next();
});

app.MapOpenApi();
app.MapHealthChecks("/health/live");
app.MapGet("/health/ready", async (RetryShieldService service, CancellationToken ct) =>
{
    await service.StatsAsync(null, ct);
    return Results.Ok(new { status = "ready" });
});
var records = app.MapGroup("/api/admin/records");
records.MapGet("/", async (string? tenant, RecordState? status, string? search, int offset, int limit,
    RetryShieldService service, CancellationToken ct) =>
{
    var result = await service.ListAsync(
        new(tenant, status, search, offset, limit == 0 ? 100 : limit), ct);
    return Results.Ok(result.Select(ToSummary));
});
records.MapGet("/{id:guid}", async (Guid id, RetryShieldService service, CancellationToken ct) =>
    await service.DetailAsync(id, ct) is { } record ? Results.Ok(ToDetail(record)) : Results.NotFound());
records.MapGet("/{id:guid}/timeline", async (Guid id, RetryShieldService service, CancellationToken ct) =>
    await service.DetailAsync(id, ct) is { } record
        ? Results.Ok(record.Timeline.Select(ToEvent))
        : Results.NotFound());
records.MapPost("/{id:guid}/resolve", async (Guid id, ResolveRequest request,
    RetryShieldService service, CancellationToken ct) =>
{
    if (!Enum.TryParse<RecordState>(request.State, true, out var state) ||
        state is not (RecordState.Completed or RecordState.Failed))
        return Results.BadRequest(new { error = "State must be Completed or Failed." });
    byte[] body;
    try
    {
        body = string.IsNullOrWhiteSpace(request.BodyBase64)
            ? []
            : Convert.FromBase64String(request.BodyBase64);
    }
    catch (FormatException)
    {
        return Results.BadRequest(new { error = "BodyBase64 is not valid Base64." });
    }
    var defaultStatus = state == RecordState.Completed
        ? StatusCodes.Status204NoContent
        : StatusCodes.Status409Conflict;
    var response = new StoredResponse(request.StatusCode ?? defaultStatus,
        request.Headers ?? new Dictionary<string, string[]>(), body);
    var resolved = await service.ResolveAsync(id, state, response, ct);
    return resolved is null
        ? Results.Conflict(new { error = "Only indeterminate records can be resolved." })
        : Results.Ok(ToDetail(resolved));
});
records.MapDelete("/expired", async (DateTimeOffset? before, RetryShieldService service, CancellationToken ct) =>
    Results.Ok(new { purged = await service.PurgeAsync(before ?? DateTimeOffset.UtcNow, ct) }));
app.MapGet("/api/admin/stats", async (string? tenant, RetryShieldService service, CancellationToken ct) =>
{
    var stats = await service.StatsAsync(tenant, ct);
    stats.ByState.TryGetValue(RecordState.Completed, out var completed);
    stats.ByState.TryGetValue(RecordState.Processing, out var processing);
    stats.ByState.TryGetValue(RecordState.Indeterminate, out var indeterminate);
    return Results.Ok(new
    {
        total = stats.Total,
        processing,
        indeterminate,
        completedRate = stats.Total == 0 ? 0 : completed * 100d / stats.Total
    });
});
app.Run();

static object ToSummary(IdempotencyRecord record) => new
{
    id = record.Id,
    record.Tenant,
    record.Route,
    record.Key,
    record.Fingerprint,
    state = record.State.ToString(),
    record.Error,
    record.CreatedAt,
    record.UpdatedAt,
    record.ExpiresAt,
    latencyMs = Math.Max(0, (record.UpdatedAt - record.CreatedAt).TotalMilliseconds),
    timeline = Array.Empty<object>(),
    response = (object?)null
};

static object ToDetail(IdempotencyRecord record) => new
{
    id = record.Id,
    record.Tenant,
    record.Route,
    record.Key,
    record.Fingerprint,
    state = record.State.ToString(),
    record.Error,
    record.CreatedAt,
    record.UpdatedAt,
    record.ExpiresAt,
    latencyMs = Math.Max(0, (record.UpdatedAt - record.CreatedAt).TotalMilliseconds),
    timeline = record.Timeline.Select(ToEvent),
    response = record.Response is null ? null : new
    {
        record.Response.StatusCode,
        record.Response.Headers,
        body = EncodeBody(record.Response.Body)
    }
};

static object ToEvent(RecordEvent item) => new
{
    at = item.At,
    state = item.State.ToString(),
    item.Note
};

static string EncodeBody(byte[] body)
{
    try { return new UTF8Encoding(false, true).GetString(body); }
    catch (DecoderFallbackException) { return $"base64:{Convert.ToBase64String(body)}"; }
}

sealed record ResolveRequest(string State, int? StatusCode, Dictionary<string, string[]>? Headers, string? BodyBase64);
public partial class Program;
