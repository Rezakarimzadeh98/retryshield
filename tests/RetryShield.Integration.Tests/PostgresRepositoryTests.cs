using Microsoft.Extensions.Options;
using Npgsql;
using RetryShield.Application;
using RetryShield.Domain;
using RetryShield.Infrastructure;
using Testcontainers.PostgreSql;

namespace RetryShield.Integration.Tests;

[CollectionDefinition("PostgreSQL integration", DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public NpgsqlDataSource DataSource { get; private set; } = null!;
    public PostgresIdempotencyRepository Repository { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("RETRYSHIELD_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("retryshield_tests")
                .WithUsername("retryshield")
                .WithPassword("retryshield-integration-password")
                .Build();
            await _postgres.StartAsync();
            connectionString = _postgres.GetConnectionString();
        }

        DataSource = NpgsqlDataSource.Create(connectionString);
        await RetryShieldSchemaMigrator.ApplyAsync(DataSource);
        Repository = new PostgresIdempotencyRepository(DataSource, CreateProtector());
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null) await DataSource.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private static AesGcmPayloadProtector CreateProtector() =>
        new(Options.Create(new RetryShieldOptions
        {
            PostgresConnectionString = "provided-by-integration-fixture",
            EncryptionKeyBase64 = Convert.ToBase64String(
                Enumerable.Range(0, 32).Select(value => (byte)value).ToArray())
        }));
}

[Trait("Category", "Docker")]
[Collection(PostgreSqlCollection.Name)]
public sealed class PostgresRepositoryTests(PostgreSqlFixture database)
{
    private NpgsqlDataSource DataSource => database.DataSource;
    private PostgresIdempotencyRepository Repository => database.Repository;

    [Fact]
    public async Task Fifty_concurrent_postgres_claims_have_exactly_one_winner()
    {
        var key = $"concurrent-{Guid.NewGuid():N}";
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 50).Select(async _ =>
        {
            await gate.Task;
            return await Repository.ClaimAsync(NewRecord(key, "same-fingerprint"), default);
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
        var first = await Repository.ClaimAsync(NewRecord(key, "fingerprint-one"), default);
        var second = await Repository.ClaimAsync(NewRecord(key, "fingerprint-two"), default);

        Assert.Equal(ClaimKind.Claimed, first.Kind);
        Assert.Equal(ClaimKind.FingerprintMismatch, second.Kind);
        Assert.Equal(first.Record.Id, second.Record.Id);
    }

    [Fact]
    public async Task Completed_response_is_encrypted_and_rehydrated_for_exact_replay()
    {
        var record = NewRecord($"replay-{Guid.NewGuid():N}", "replay-fingerprint");
        Assert.Equal(ClaimKind.Claimed,
            (await Repository.ClaimAsync(record, default)).Kind);

        var response = new StoredResponse(
            201,
            new Dictionary<string, string[]> { ["Content-Type"] = ["application/json"] },
            """{"id":"pay_000001"}"""u8.ToArray());
        record.Complete(response);
        await Repository.SaveAsync(record, default);

        var persisted = await Repository.GetByIdAsync(record.Id, default);

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
        await Repository.ClaimAsync(record, default);

        var changed = await Repository.MarkStaleProcessingIndeterminateAsync(
            now.AddMinutes(-5), default);
        var persisted = await Repository.GetByIdAsync(record.Id, default);

        Assert.Equal(1, changed);
        Assert.Equal(RecordState.Indeterminate, persisted!.State);
        Assert.Contains("Gateway stopped", persisted.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schema_migrations_are_idempotent_and_record_the_current_version()
    {
        await RetryShieldSchemaMigrator.ApplyAsync(DataSource);
        await RetryShieldSchemaMigrator.ApplyAsync(DataSource);

        await using var command = DataSource.CreateCommand(
            "SELECT version,name,checksum FROM retryshield_schema_migrations");
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(RetryShieldSchemaMigrator.CurrentVersion, reader.GetInt32(0));
        Assert.Equal("v0.1_initial_schema", reader.GetString(1));
        Assert.Equal(64, reader.GetString(2).Length);
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Concurrent_startups_adopt_v01_schema_without_losing_records()
    {
        var record = NewRecord($"pre-migrations-{Guid.NewGuid():N}", "legacy-fingerprint");
        await Repository.ClaimAsync(record, default);
        await using (var dropHistory = DataSource.CreateCommand(
            "DROP TABLE retryshield_schema_migrations"))
        {
            await dropHistory.ExecuteNonQueryAsync();
        }

        await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => RetryShieldSchemaMigrator.ApplyAsync(DataSource)));
        var persisted = await Repository.GetByIdAsync(record.Id, default);
        await using var countHistory = DataSource.CreateCommand(
            "SELECT count(*) FROM retryshield_schema_migrations");
        var migrationCount = Convert.ToInt32(await countHistory.ExecuteScalarAsync());

        Assert.NotNull(persisted);
        Assert.Equal(record.Fingerprint, persisted.Fingerprint);
        Assert.Equal(1, migrationCount);
    }

    [Fact]
    public async Task Global_stats_query_works_without_a_tenant_filter()
    {
        var claimed = await Repository.ClaimAsync(
            NewRecord($"stats-{Guid.NewGuid():N}", "stats-fingerprint"), default);
        Assert.Equal(ClaimKind.Claimed, claimed.Kind);

        var stats = await Repository.StatsAsync(null, default);

        Assert.True(stats.Total >= 1);
        Assert.True(stats.ByState.ContainsKey(RecordState.Processing));
    }

    private static IdempotencyRecord NewRecord(string key, string fingerprint) =>
        IdempotencyRecord.Create(
            "test", "/payments", key, fingerprint, DateTimeOffset.UtcNow.AddHours(1));

}
