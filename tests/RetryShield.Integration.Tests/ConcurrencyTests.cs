using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using RetryShield.Application;
using RetryShield.Domain;
using RetryShield.Infrastructure;

namespace RetryShield.Integration.Tests;

public class ConcurrencyTests
{
    [Fact]
    public async Task Fifty_concurrent_claims_have_exactly_one_winner()
    {
        var repository = new InMemoryRepository();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var upstreamForwards = 0;
        var tasks = Enumerable.Range(0, 50).Select(async _ =>
        {
            await gate.Task;
            var record = IdempotencyRecord.Create("t", "/payments", "same-key", "same-fingerprint",
                DateTimeOffset.UtcNow.AddHours(1));
            var claim = await repository.ClaimAsync(record, CancellationToken.None);
            if (claim.Kind == ClaimKind.Claimed) Interlocked.Increment(ref upstreamForwards);
            return claim;
        }).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, x => x.Kind == ClaimKind.Claimed);
        Assert.Equal(49, results.Count(x => x.Kind == ClaimKind.Existing));
        Assert.Single(results.Select(x => x.Record.Id).Distinct());
        Assert.Equal(1, upstreamForwards);
    }

    [Fact]
    public async Task Reused_key_with_different_payload_is_rejected()
    {
        var repository = new InMemoryRepository();
        await repository.ClaimAsync(IdempotencyRecord.Create("t", "/p", "k", "one", DateTimeOffset.UtcNow.AddHours(1)), default);
        var result = await repository.ClaimAsync(
            IdempotencyRecord.Create("t", "/p", "k", "two", DateTimeOffset.UtcNow.AddHours(1)), default);
        Assert.Equal(ClaimKind.FingerprintMismatch, result.Kind);
    }

    [Fact]
    public void Aes_gcm_protection_round_trips_and_randomizes_ciphertext()
    {
        var options = Options.Create(new RetryShieldOptions
        {
            PostgresConnectionString = "unused",
            EncryptionKeyBase64 = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray())
        });
        var protector = new AesGcmPayloadProtector(options);
        var plaintext = new byte[] { 1, 2, 3, 4 };
        var one = protector.Protect(plaintext);
        var two = protector.Protect(plaintext);
        Assert.NotEqual(one, two);
        Assert.Equal(plaintext, protector.Unprotect(one));
    }

    [Fact]
    public async Task Stale_processing_claims_become_indeterminate_after_crash_window()
    {
        var repository = new InMemoryRepository();
        var now = DateTimeOffset.UtcNow;
        var record = IdempotencyRecord.Create("t", "/payments", "crash-key", "fingerprint",
            now.AddHours(1), now.AddMinutes(-10));
        await repository.ClaimAsync(record, default);

        var changed = await repository.MarkStaleProcessingIndeterminateAsync(now.AddMinutes(-5), default);

        Assert.Equal(1, changed);
        Assert.Equal(RecordState.Indeterminate, record.State);
    }
}

sealed class InMemoryRepository : IIdempotencyRepository
{
    private readonly object _sync = new();
    private readonly Dictionary<(string Tenant, string Route, string Key), IdempotencyRecord> _records = [];

    public Task<ClaimResult> ClaimAsync(IdempotencyRecord candidate, CancellationToken ct)
    {
        lock (_sync)
        {
            var identity = (candidate.Tenant, candidate.Route, candidate.Key);
            if (!_records.TryGetValue(identity, out var existing))
            {
                _records[identity] = candidate;
                return Task.FromResult(new ClaimResult(ClaimKind.Claimed, candidate));
            }
            return Task.FromResult(new ClaimResult(existing.Fingerprint == candidate.Fingerprint
                ? ClaimKind.Existing : ClaimKind.FingerprintMismatch, existing));
        }
    }
    public Task<IdempotencyRecord?> GetAsync(string tenant, string route, string key, CancellationToken ct)
    {
        lock (_sync) return Task.FromResult(_records.GetValueOrDefault((tenant, route, key)));
    }
    public Task<IdempotencyRecord?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        lock (_sync) return Task.FromResult(_records.Values.SingleOrDefault(x => x.Id == id));
    }
    public Task SaveAsync(IdempotencyRecord record, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<IdempotencyRecord>> ListAsync(RecordQuery query, CancellationToken ct)
    {
        lock (_sync) return Task.FromResult<IReadOnlyList<IdempotencyRecord>>(_records.Values.ToArray());
    }
    public Task<RecordStats> StatsAsync(string? tenant, CancellationToken ct)
    {
        lock (_sync)
        {
            var values = _records.Values.Where(x => tenant is null || x.Tenant == tenant).ToArray();
            return Task.FromResult(new RecordStats(values.Length,
                values.GroupBy(x => x.State).ToDictionary(x => x.Key, x => (long)x.Count())));
        }
    }
    public Task<int> PurgeExpiredAsync(DateTimeOffset before, CancellationToken ct)
    {
        lock (_sync)
        {
            var keys = _records.Where(x => x.Value.ExpiresAt < before).Select(x => x.Key).ToArray();
            foreach (var key in keys) _records.Remove(key);
            return Task.FromResult(keys.Length);
        }
    }
    public Task<int> MarkStaleProcessingIndeterminateAsync(DateTimeOffset before, CancellationToken ct)
    {
        lock (_sync)
        {
            var records = _records.Values
                .Where(record => record.State == RecordState.Processing && record.UpdatedAt < before)
                .ToArray();
            foreach (var record in records) record.MarkIndeterminate("stale processing claim");
            return Task.FromResult(records.Length);
        }
    }
}
