using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RetryShield.Application;
using RetryShield.Domain;
using RetryShield.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var processingTimeout = builder.Configuration.GetValue(
    "RetryShield:ProcessingTimeout", TimeSpan.FromMinutes(5));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddRetryShieldInfrastructure(builder.Configuration);
var otlpEnabled = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddSource("RetryShield.Gateway")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
        if (otlpEnabled) tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("RetryShield.Gateway", "Microsoft.AspNetCore.Hosting", "System.Net.Http");
        if (otlpEnabled) metrics.AddOtlpExporter();
    });
builder.Services.AddOptions<GatewayOptions>().Bind(builder.Configuration.GetSection("Gateway"))
    .Validate(options => Uri.TryCreate(options.UpstreamBaseUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && uri.AbsolutePath == "/" &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment),
        "Gateway upstream must be an absolute HTTP(S) origin without a path, query, or fragment.")
    .Validate(options => options.MaxBodyBytes > 0 && options.MaxResponseBodyBytes > 0,
        "Request and response limits must be positive.")
    .Validate(options => IsValidScope(options.DefaultTenant),
        "Default tenant must be 1-128 letters, digits, dots, dashes, or underscores.")
    .Validate(options => options.UpstreamTimeout < processingTimeout,
        "Gateway upstream timeout must be shorter than the stale-processing timeout.")
    .ValidateOnStart();
builder.Services.AddHttpClient("upstream", (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
    client.BaseAddress = new Uri(options.UpstreamBaseUrl, UriKind.Absolute);
    client.Timeout = options.UpstreamTimeout;
});

var app = builder.Build();
await app.Services.InitializeRetryShieldSchemaAsync();
app.MapOpenApi();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health");
app.MapGet("/health/ready", async (RetryShieldService service, CancellationToken ct) =>
{
    await service.StatsAsync(null, ct);
    return Results.Ok(new { status = "ready" });
});
app.MapGet("/ready", () => Results.Redirect("/health/ready"));
app.MapGet("/metrics", () => Results.Text(
    GatewayTelemetry.RenderPrometheus(), "text/plain; version=0.0.4"));

app.MapMethods("/proxy/{**path}", ["POST", "PUT", "PATCH", "DELETE"], HandleProxy);
app.Run();

static async Task<IResult> HandleProxy(HttpContext context, string? path, RetryShieldService service,
    IPayloadProtector protector, IHttpClientFactory clients, IOptions<GatewayOptions> configured, CancellationToken ct)
{
    using var activity = GatewayTelemetry.Activity.StartActivity("retryshield.proxy", ActivityKind.Server);
    Interlocked.Increment(ref GatewayTelemetry.Requests);
    var options = configured.Value;
    if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues) ||
        keyValues.Count != 1 || string.IsNullOrWhiteSpace(keyValues[0]))
        return Results.BadRequest(new { error = "Exactly one Idempotency-Key is required." });
    var key = keyValues.ToString();
    if (key.Length > 256) return Results.BadRequest(new { error = "Idempotency-Key is too long." });
    if (context.Request.ContentLength > options.MaxBodyBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = options.MaxBodyBytes;

    byte[] body;
    try
    {
        body = await ReadBoundedAsync(context.Request.Body, options.MaxBodyBytes, ct);
    }
    catch (BodyLimitExceededException) { return Results.StatusCode(StatusCodes.Status413PayloadTooLarge); }
    catch (BadHttpRequestException) { return Results.StatusCode(StatusCodes.Status413PayloadTooLarge); }
    var route = "/" + (path ?? "");
    var upstreamPath = route + context.Request.QueryString;
    var fingerprint = Fingerprints.Compute(context.Request.Method, upstreamPath,
        context.Request.ContentType, body);
    var tenant = options.DefaultTenant;
    var record = IdempotencyRecord.Create(tenant, route, key, fingerprint,
        DateTimeOffset.UtcNow.Add(options.RecordTtl));
    activity?.SetTag("retryshield.record_id", record.Id);
    record.ProtectedRequestBody = protector.Protect(body);
    var claim = await service.ClaimAsync(record, ct);

    if (claim.Kind == ClaimKind.FingerprintMismatch)
    {
        activity?.SetTag("retryshield.outcome", "fingerprint_mismatch");
        Interlocked.Increment(ref GatewayTelemetry.Conflicts);
        context.Response.Headers["Idempotency-Status"] = "conflict";
        return Results.UnprocessableEntity(new { error = "Idempotency-Key was already used with a different request." });
    }
    if (claim.Kind == ClaimKind.Existing)
    {
        record = await service.WaitForTerminalAsync(claim.Record, options.DuplicateWait, ct);
        if (record.State == RecordState.Processing)
        {
            activity?.SetTag("retryshield.outcome", "in_flight");
            Interlocked.Increment(ref GatewayTelemetry.Conflicts);
            context.Response.Headers["Idempotency-Status"] = "processing";
            return Results.Conflict(new { error = "An identical request is still processing.", retryAfterMs = (int)options.DuplicateWait.TotalMilliseconds });
        }
        if (record.State == RecordState.Indeterminate)
        {
            activity?.SetTag("retryshield.outcome", "indeterminate");
            context.Response.Headers["Idempotency-Status"] = "indeterminate";
            return Results.Conflict(new { error = "The upstream outcome is indeterminate; operator resolution is required." });
        }
        if (record.Response is not null)
        {
            activity?.SetTag("retryshield.outcome", "replayed");
            Interlocked.Increment(ref GatewayTelemetry.Replays);
            context.Response.Headers["Idempotency-Status"] = "replayed";
            return Replay(record.Response);
        }
        return Results.StatusCode(StatusCodes.Status410Gone);
    }

    var client = clients.CreateClient("upstream");
    if (client.BaseAddress is null || !TryBuildDestination(client.BaseAddress, upstreamPath, out var destination))
        return Results.Problem("The configured upstream destination is invalid.",
            statusCode: StatusCodes.Status500InternalServerError);
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), destination);
    if (body.Length > 0)
    {
        request.Content = new ByteArrayContent(body);
        if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
            request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
    }
    foreach (var header in options.ForwardRequestHeaders)
        if (context.Request.Headers.TryGetValue(header, out var value))
            request.Headers.TryAddWithoutValidation(header, value.ToArray());
    request.Headers.TryAddWithoutValidation("X-RetryShield-Request-Id", record.Id.ToString());

    try
    {
        var started = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref GatewayTelemetry.Forwards);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.Content.Headers.ContentLength > options.MaxResponseBodyBytes)
            throw new BodyLimitExceededException();
        var responseBody = await ReadBoundedAsync(
            await response.Content.ReadAsStreamAsync(ct), options.MaxResponseBodyBytes, ct);
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in options.ResponseHeaderAllowlist)
        {
            if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
                headers[name] = values.ToArray();
        }
        if (response.Content.Headers.ContentType is not null)
            headers["Content-Type"] = [response.Content.Headers.ContentType.ToString()];
        var stored = new StoredResponse((int)response.StatusCode, headers, responseBody);
        if (response.IsSuccessStatusCode) await service.CompleteAsync(record, stored, ct);
        else await service.FailAsync(record, $"Upstream returned {(int)response.StatusCode}.", stored, ct);
        GatewayTelemetry.RecordLatency(Stopwatch.GetElapsedTime(started));
        activity?.SetTag("retryshield.outcome", response.IsSuccessStatusCode ? "completed" : "failed");
        context.Response.Headers["Idempotency-Status"] = "created";
        return Replay(stored);
    }
    catch (BodyLimitExceededException)
    {
        activity?.SetTag("retryshield.outcome", "indeterminate");
        Interlocked.Increment(ref GatewayTelemetry.Indeterminate);
        await service.IndeterminateAsync(record, "Upstream response exceeded the configured replay limit.",
            CancellationToken.None);
        context.Response.Headers["Idempotency-Status"] = "indeterminate";
        return Results.Json(new { error = "Upstream completed but its response was too large to store safely." },
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
    {
        activity?.SetTag("retryshield.outcome", "indeterminate");
        Interlocked.Increment(ref GatewayTelemetry.Indeterminate);
        await service.IndeterminateAsync(record, ex.GetType().Name, CancellationToken.None);
        context.Response.Headers["Idempotency-Status"] = "indeterminate";
        return Results.Json(new { error = "Upstream outcome is indeterminate." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

static IResult Replay(StoredResponse response) => new StoredResponseResult(response);

static async Task<byte[]> ReadBoundedAsync(Stream stream, long limit, CancellationToken ct)
{
    await using var buffer = new MemoryStream((int)Math.Min(limit, 81_920));
    var chunk = new byte[81_920];
    while (true)
    {
        var read = await stream.ReadAsync(chunk, ct);
        if (read == 0) return buffer.ToArray();
        if (buffer.Length + read > limit) throw new BodyLimitExceededException();
        await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
    }
}

static bool IsValidScope(string value) =>
    value.Length is > 0 and <= 128 && value.All(character =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

static bool TryBuildDestination(Uri upstream, string pathAndQuery, out Uri destination)
{
    if (!Uri.TryCreate(upstream, pathAndQuery, out destination!)) return false;
    return destination.Scheme == upstream.Scheme &&
        destination.Host.Equals(upstream.Host, StringComparison.OrdinalIgnoreCase) &&
        destination.Port == upstream.Port;
}

sealed class StoredResponseResult(StoredResponse response) : IResult
{
    public async Task ExecuteAsync(HttpContext context)
    {
        context.Response.StatusCode = response.StatusCode;
        foreach (var (name, values) in response.Headers) context.Response.Headers[name] = values;
        context.Response.ContentLength = response.Body.Length;
        await context.Response.Body.WriteAsync(response.Body, context.RequestAborted);
    }
}

sealed class GatewayOptions
{
    public string UpstreamBaseUrl { get; set; } = "http://localhost:5090/";
    public string DefaultTenant { get; set; } = "default";
    public long MaxBodyBytes { get; set; } = 1_048_576;
    public long MaxResponseBodyBytes { get; set; } = 2_097_152;
    public TimeSpan RecordTtl { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan DuplicateWait { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan UpstreamTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public string[] ForwardRequestHeaders { get; set; } = ["Accept", "User-Agent"];
    public string[] ResponseHeaderAllowlist { get; set; } = ["Content-Type", "Location", "ETag", "Cache-Control"];
}

static class GatewayTelemetry
{
    public static readonly ActivitySource Activity = new("RetryShield.Gateway");
    public static readonly Meter Meter = new("RetryShield.Gateway");
    public static long Requests;
    public static long Forwards;
    public static long Replays;
    public static long Conflicts;
    public static long Indeterminate;
    private static readonly Histogram<double> Latency = Meter.CreateHistogram<double>(
        "retryshield.forward.duration", "s");
    private static readonly double[] DurationBounds = [.01, .025, .05, .1, .25, .5, 1, 2.5, 5, 10, 30];
    private static readonly long[] DurationBuckets = new long[DurationBounds.Length];
    private static long _durationCount;
    private static long _durationTicks;
    public static void RecordLatency(TimeSpan elapsed)
    {
        Latency.Record(elapsed.TotalSeconds);
        Interlocked.Increment(ref _durationCount);
        Interlocked.Add(ref _durationTicks, elapsed.Ticks);
        for (var index = 0; index < DurationBounds.Length; index++)
            if (elapsed.TotalSeconds <= DurationBounds[index])
                Interlocked.Increment(ref DurationBuckets[index]);
    }
    public static string RenderPrometheus()
    {
        var text = new StringBuilder()
            .AppendLine("# TYPE retryshield_requests_total counter")
            .Append("retryshield_requests_total ").AppendLine(Volatile.Read(ref Requests).ToString(CultureInfo.InvariantCulture))
            .AppendLine("# TYPE retryshield_claims_total counter")
            .Append("retryshield_claims_total ").AppendLine(Volatile.Read(ref Forwards).ToString(CultureInfo.InvariantCulture))
            .Append("retryshield_forwards_total ").AppendLine(Volatile.Read(ref Forwards).ToString(CultureInfo.InvariantCulture))
            .Append("retryshield_replays_total ").AppendLine(Volatile.Read(ref Replays).ToString(CultureInfo.InvariantCulture))
            .Append("retryshield_conflicts_total ").AppendLine(Volatile.Read(ref Conflicts).ToString(CultureInfo.InvariantCulture))
            .Append("retryshield_indeterminate_total ").AppendLine(Volatile.Read(ref Indeterminate).ToString(CultureInfo.InvariantCulture))
            .AppendLine("# TYPE retryshield_request_duration_seconds histogram");
        for (var index = 0; index < DurationBounds.Length; index++)
            text.Append("retryshield_request_duration_seconds_bucket{le=\"")
                .Append(DurationBounds[index].ToString(CultureInfo.InvariantCulture))
                .Append("\"} ").AppendLine(Volatile.Read(ref DurationBuckets[index]).ToString(CultureInfo.InvariantCulture));
        var count = Volatile.Read(ref _durationCount);
        text.Append("retryshield_request_duration_seconds_bucket{le=\"+Inf\"} ")
            .AppendLine(count.ToString(CultureInfo.InvariantCulture))
            .Append("retryshield_request_duration_seconds_sum ")
            .AppendLine(TimeSpan.FromTicks(Volatile.Read(ref _durationTicks)).TotalSeconds.ToString(CultureInfo.InvariantCulture))
            .Append("retryshield_request_duration_seconds_count ")
            .AppendLine(count.ToString(CultureInfo.InvariantCulture));
        return text.ToString();
    }
    static GatewayTelemetry()
    {
        Meter.CreateObservableCounter("retryshield.requests", () => Requests);
        Meter.CreateObservableCounter("retryshield.forwards", () => Forwards);
        Meter.CreateObservableCounter("retryshield.replays", () => Replays);
        Meter.CreateObservableCounter("retryshield.conflicts", () => Conflicts);
        Meter.CreateObservableCounter("retryshield.indeterminate", () => Indeterminate);
    }
}

sealed class BodyLimitExceededException : Exception;

public partial class Program;
