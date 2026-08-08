using System.Collections.ObjectModel;

namespace RetryShield.Domain;

public enum RecordState { Processing, Completed, Failed, Indeterminate, Expired }

public sealed record StoredResponse(int StatusCode, IReadOnlyDictionary<string, string[]> Headers, byte[] Body);
public sealed record RecordEvent(DateTimeOffset At, RecordState State, string? Note);

public sealed class IdempotencyRecord
{
    private readonly List<RecordEvent> _timeline = [];

    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Tenant { get; init; }
    public required string Route { get; init; }
    public required string Key { get; init; }
    public required string Fingerprint { get; init; }
    public RecordState State { get; private set; } = RecordState.Processing;
    public StoredResponse? Response { get; private set; }
    public byte[]? ProtectedRequestBody { get; set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; }
    public IReadOnlyList<RecordEvent> Timeline => new ReadOnlyCollection<RecordEvent>(_timeline);

    public static IdempotencyRecord Create(string tenant, string route, string key, string fingerprint,
        DateTimeOffset expiresAt, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(route) ||
            string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(fingerprint))
            throw new ArgumentException("Record identity and fingerprint are required.");
        if (expiresAt <= timestamp) throw new ArgumentOutOfRangeException(nameof(expiresAt), "Expiry must be in the future.");
        var record = new IdempotencyRecord
        {
            Tenant = tenant.Trim(),
            Route = route,
            Key = key.Trim(),
            Fingerprint = fingerprint,
            ExpiresAt = expiresAt,
            CreatedAt = timestamp
        };
        record.UpdatedAt = timestamp;
        record._timeline.Add(new(record.UpdatedAt, RecordState.Processing, "claimed"));
        return record;
    }

    public void Complete(StoredResponse response, DateTimeOffset? now = null) =>
        Transition(RecordState.Completed, now, response: response);
    public void Fail(string error, StoredResponse? response = null, DateTimeOffset? now = null) =>
        Transition(RecordState.Failed, now, error, response);
    public void MarkIndeterminate(string error, DateTimeOffset? now = null) =>
        Transition(RecordState.Indeterminate, now, error);
    public void ResolveCompleted(StoredResponse response, DateTimeOffset? now = null) =>
        Transition(RecordState.Completed, now, "administratively resolved", response);
    public void ResolveFailed(StoredResponse response, DateTimeOffset? now = null) =>
        Transition(RecordState.Failed, now, "administratively resolved as failed", response);
    public void Expire(DateTimeOffset? now = null) => Transition(RecordState.Expired, now, "expired");

    private void Transition(RecordState next, DateTimeOffset? now, string? error = null, StoredResponse? response = null)
    {
        var valid = State switch
        {
            RecordState.Processing => next is RecordState.Completed or RecordState.Failed or RecordState.Indeterminate or RecordState.Expired,
            RecordState.Indeterminate => next is RecordState.Completed or RecordState.Failed or RecordState.Expired,
            RecordState.Completed or RecordState.Failed => next is RecordState.Expired,
            _ => false
        };
        if (!valid) throw new InvalidOperationException($"Invalid transition {State} -> {next}.");
        State = next;
        Response = response;
        Error = error;
        UpdatedAt = now ?? DateTimeOffset.UtcNow;
        _timeline.Add(new(UpdatedAt, State, error));
    }

    public void Rehydrate(RecordState state, StoredResponse? response, string? error,
        DateTimeOffset updatedAt, IEnumerable<RecordEvent> timeline)
    {
        State = state; Response = response; Error = error; UpdatedAt = updatedAt;
        _timeline.Clear(); _timeline.AddRange(timeline);
    }
}
