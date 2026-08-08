using System.Security.Cryptography;
using System.Text;
using RetryShield.Domain;

namespace RetryShield.Application;

public enum ClaimKind { Claimed, Existing, FingerprintMismatch }
public sealed record ClaimResult(ClaimKind Kind, IdempotencyRecord Record);
public sealed record RecordQuery(string? Tenant = null, RecordState? State = null, string? Search = null,
    int Offset = 0, int Limit = 100);
public sealed record RecordStats(long Total, IReadOnlyDictionary<RecordState, long> ByState);

public interface IIdempotencyRepository
{
    Task<ClaimResult> ClaimAsync(IdempotencyRecord candidate, CancellationToken ct);
    Task<IdempotencyRecord?> GetAsync(string tenant, string route, string key, CancellationToken ct);
    Task<IdempotencyRecord?> GetByIdAsync(Guid id, CancellationToken ct);
    Task SaveAsync(IdempotencyRecord record, CancellationToken ct);
    Task<IReadOnlyList<IdempotencyRecord>> ListAsync(RecordQuery query, CancellationToken ct);
    Task<RecordStats> StatsAsync(string? tenant, CancellationToken ct);
    Task<int> MarkStaleProcessingIndeterminateAsync(DateTimeOffset before, CancellationToken ct);
    Task<int> PurgeExpiredAsync(DateTimeOffset before, CancellationToken ct);
}

public interface IPayloadProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(ReadOnlySpan<byte> ciphertext);
}

public interface IRecordNotifier
{
    Task PublishAsync(IdempotencyRecord record, CancellationToken ct);
    Task<bool> WaitAsync(Guid id, TimeSpan timeout, CancellationToken ct);
}

public static class Fingerprints
{
    public static string Compute(string method, string pathAndQuery, string? contentType, ReadOnlySpan<byte> body)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(bytes);
            hash.AppendData([0]);
        }
        Add(method.ToUpperInvariant());
        Add(CanonicalizePathAndQuery(pathAndQuery));
        Add((contentType ?? "").Split(';', 2)[0].Trim().ToLowerInvariant());
        hash.AppendData(body);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string CanonicalizePathAndQuery(string value)
    {
        var separator = value.IndexOf('?');
        if (separator < 0) return value;
        var path = value[..separator];
        var query = value[(separator + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal);
        return $"{path}?{string.Join('&', query)}";
    }
}

public sealed class RetryShieldService(IIdempotencyRepository repository, IRecordNotifier notifier)
{
    public Task<ClaimResult> ClaimAsync(IdempotencyRecord candidate, CancellationToken ct) =>
        repository.ClaimAsync(candidate, ct);

    public async Task CompleteAsync(IdempotencyRecord record, StoredResponse response, CancellationToken ct)
    {
        record.Complete(response); await repository.SaveAsync(record, ct); await notifier.PublishAsync(record, ct);
    }

    public async Task FailAsync(IdempotencyRecord record, string error, StoredResponse? response, CancellationToken ct)
    {
        record.Fail(error, response); await repository.SaveAsync(record, ct); await notifier.PublishAsync(record, ct);
    }

    public async Task IndeterminateAsync(IdempotencyRecord record, string error, CancellationToken ct)
    {
        record.MarkIndeterminate(error); await repository.SaveAsync(record, ct); await notifier.PublishAsync(record, ct);
    }

    public async Task<IdempotencyRecord> WaitForTerminalAsync(IdempotencyRecord record, TimeSpan timeout, CancellationToken ct)
    {
        if (record.State != RecordState.Processing) return record;
        await notifier.WaitAsync(record.Id, timeout, ct);
        return await repository.GetByIdAsync(record.Id, ct) ?? record;
    }

    public Task<IReadOnlyList<IdempotencyRecord>> ListAsync(RecordQuery query, CancellationToken ct) =>
        repository.ListAsync(query, ct);
    public Task<IdempotencyRecord?> DetailAsync(Guid id, CancellationToken ct) => repository.GetByIdAsync(id, ct);
    public Task<RecordStats> StatsAsync(string? tenant, CancellationToken ct) => repository.StatsAsync(tenant, ct);
    public Task<int> PurgeAsync(DateTimeOffset before, CancellationToken ct) => repository.PurgeExpiredAsync(before, ct);

    public async Task<IdempotencyRecord?> ResolveAsync(Guid id, RecordState outcome, StoredResponse response,
        CancellationToken ct)
    {
        var record = await repository.GetByIdAsync(id, ct);
        if (record is null || record.State != RecordState.Indeterminate) return null;
        if (outcome == RecordState.Completed) record.ResolveCompleted(response);
        else if (outcome == RecordState.Failed) record.ResolveFailed(response);
        else throw new ArgumentOutOfRangeException(nameof(outcome), "Resolution must be Completed or Failed.");
        await repository.SaveAsync(record, ct);
        await notifier.PublishAsync(record, ct);
        return record;
    }
}
