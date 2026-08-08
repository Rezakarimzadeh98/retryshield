using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<DemoState>();
var app = builder.Build();

app.MapPost("/payments", async (HttpContext context, PaymentRequest payment, DemoState state, CancellationToken ct) =>
{
    var controls = state.Controls;
    if (controls.DelayMs > 0) await Task.Delay(controls.DelayMs, ct);
    if (controls.ConsumeFailure())
        return Results.Json(new { error = "simulated failure" }, statusCode: 500);

    var number = Interlocked.Increment(ref state.PaymentCount);
    var id = $"pay_{number:D6}";
    state.Payments[id] = payment;
    if (controls.ConsumeLostResponse())
    {
        context.Abort();
        return Results.Empty;
    }
    return Results.Created($"/payments/{id}", new { id, payment.Amount, payment.Currency, sequence = number });
});
app.MapGet("/payments/count", (DemoState state) => new { count = Volatile.Read(ref state.PaymentCount) });
app.MapGet("/controls", (DemoState state) => state.Controls);
app.MapPut("/controls", (DemoControls controls, DemoState state) =>
{
    state.Controls = controls;
    return Results.Ok(state.Controls);
});
app.MapDelete("/controls", (DemoState state) =>
{
    state.Controls = new();
    return Results.NoContent();
});
app.Run();

sealed record PaymentRequest(decimal Amount, string Currency);
sealed class DemoControls
{
    public int DelayMs { get; set; }
    public int FailNext { get; set; }
    public int LoseResponseNext { get; set; }
    public bool ConsumeFailure()
    {
        lock (this) { if (FailNext <= 0) return false; FailNext--; return true; }
    }
    public bool ConsumeLostResponse()
    {
        lock (this) { if (LoseResponseNext <= 0) return false; LoseResponseNext--; return true; }
    }
}
sealed class DemoState
{
    public int PaymentCount;
    public ConcurrentDictionary<string, PaymentRequest> Payments { get; } = new();
    public DemoControls Controls { get; set; } = new();
}

public partial class Program;
