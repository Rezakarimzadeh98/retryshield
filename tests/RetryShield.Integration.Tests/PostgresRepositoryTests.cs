using Microsoft.Extensions.Options;
using Npgsql;
using RetryShield.Application;
using RetryShield.Domain;
using RetryShield.Infrastructure;
using Testcontainers.PostgreSql;

namespace RetryShield.Integration.Tests;

[Trait("Category", "Docker")]
public sealed class PostgresRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("retryshield_tests")
        .WithUsername("retryshield")
        .WithPassword("retryshield-integration-password")
        .Build();

    private NpgsqlDataSource _dataSource = null!;
    private PostgresIdempotencyRepository _repository = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await RetryShieldSchemaMigrator.ApplyAsync(_dataSource);
        _repository = new PostgresIdempotencyRepository(_dataSource, CreateProtector());
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Fifty_concurrent_postgres_claims_have_exactly_one_winner()
    {
        var key = $"concurrent-{Guid.NewGuid():N}";
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 50).Select(async _ =>
        {
            await gate.Task;
            return await _repository.ClaimAsync(NewRecord(key, "same-fingerprint"), default);
        }).ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Single(results, item => item.Kind == ClaimKind.Claimed);
        Assert.Equal(49, results.Count(item => item.Kind == ClaimKind.Existing));
        Assert.Single(results.Select(item => item.Record.Id).Distinct());
    }

    [Fact]
    public async Task Postgres_rejects_a_reused_key_with_a_different_fingerprint()
    {
        var key = $"mismatch-{Guid.NewGuid():N}";
        var first = await _repository.ClaimAsync(NewRecord(key, "fingerprint-one"), default);
        var second = await _repository.ClaimAsync(NewRecord(key, "fingerprint-two"), default);

        Assert.Equal(ClaimKind.Claimed, first.Kind);
        Assert.Equal(ClaimKind.FingerprintMismatch, second.Kind);
        Assert.Equal(first.Record.Id, second.Record.Id);
    }

    [Fact]
    public async Task Completed_response_is_encrypted_and_rehydrated_for_exact_replay()
    {
        var record = NewRecord($"replay-{Guid.NewGuid():N}", "replay-fingerprint");
        Assert.Equal(ClaimKind.Claimed,
            (await _repository.ClaimAsync(record, default)).Kind);

        var response = new StoredResponse(
            201,
            new Dictionary<string, string[]> { ["Content-Type"] = ["application/json"] },
            """{"id":"pay_000001"}"""u8.ToArray());
        record.Complete(response);
        await _repository.SaveAsync(record, default);

        var persisted = await _repository.GetByIdAsync(record.Id, default);

        Assert.NotNull(persisted);
        Assert.Equal(RecordState.Completed, persisted.State);
        Assert.Equal(response.StatusCode, persisted.Response!.StatusCode);
        Assert.Equal(response.Body, persisted.Response.Body);
        Assert.Equal(response.Headers, persisted.Response.Headers);
    }

    [Fact]
    public async Task Stale_postgres_claim_becomes_indeterminate_after_the_crash_window()
    {
        var now = DateTimeOffset.UtcNow;
        var record = IdempotencyRecord.Create(
            "test", "/payments", $"stale-{Guid.NewGuid():N}", "stale-fingerprint",
            now.AddHours(1), now.AddMinutes(-10));
        await _repository.ClaimAsync(record, default);

        var changed = await _repository.MarkStaleProcessingIndeterminateAsync(
            now.AddMinutes(-5), default);
        var persisted = await _repository.GetByIdAsync(record.Id, default);

        Assert.Equal(1, changed);
        Assert.Equal(RecordState.Indeterminate, persisted!.State);
        Assert.Contains("Gateway stopped", persisted.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_migrations_are_idempotent_and_record_the_current_version()
    {
        await RetryShieldSchemaMigrator.ApplyAsync(_dataSource);
        await RetryShieldSchemaMigrator.ApplyAsync(_dataSource);

        await using var command = _dataSource.CreateCommand(
            "SELECT MAX(version) FROM retryshield_schema_migrations");
        var version = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(RetryShieldSchemaMigrator.CurrentVersion, version);
    }

    [Fact]
    public async Task Global_stats_query_works_without_a_tenant_filter()
    {
        var claimed = await _repository.ClaimAsync(
            NewRecord($"stats-{Guid.NewGuid():N}", "stats-fingerprint"), default);
        Assert.Equal(ClaimKind.Claimed, claimed.Kind);

        var stats = await _repository.StatsAsync(null, default);

        Assert.True(stats.Total >= 1);
        Assert.True(stats.ByState.ContainsKey(RecordState.Processing));
    }

    private static IdempotencyRecord NewRecord(string key, string fingerprint) =>
        IdempotencyRecord.Create(
            "test", "/payments", key, fingerprint, DateTimeOffset.UtcNow.AddHours(1));

    private static AesGcmPayloadProtector CreateProtector() =>
        new(Options.Create(new RetryShieldOptions
        {
            PostgresConnectionString = "provided-by-testcontainer",
            EncryptionKeyBase64 = Convert.ToBase64String(
                Enumerable.Range(0, 32).Select(value => (byte)value).ToArray())
        }));
}
