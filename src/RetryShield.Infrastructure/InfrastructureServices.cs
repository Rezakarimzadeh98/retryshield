using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using RetryShield.Application;
using RetryShield.Domain;
using StackExchange.Redis;

namespace RetryShield.Infrastructure;

public sealed class RetryShieldOptions
{
    public required string PostgresConnectionString { get; set; }
    public string? RedisConnectionString { get; set; }
    public required string EncryptionKeyBase64 { get; set; }
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(5);
}

public sealed class AesGcmPayloadProtector : IPayloadProtector
{
    private readonly byte[] _key;
    public AesGcmPayloadProtector(IOptions<RetryShieldOptions> options)
    {
        _key = Convert.FromBase64String(options.Value.EncryptionKeyBase64);
        if (_key.Length is not (16 or 24 or 32)) throw new InvalidOperationException("Encryption key must be 128, 192, or 256 bits.");
    }
    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var output = new byte[12 + 16 + plaintext.Length];
        nonce.CopyTo(output, 0);
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintext, output.AsSpan(28), tag);
        tag.CopyTo(output, 12);
        return output;
    }
    public byte[] Unprotect(ReadOnlySpan<byte> ciphertext)
    {
        if (ciphertext.Length < 28) throw new CryptographicException("Invalid protected payload.");
        var output = new byte[ciphertext.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(ciphertext[..12], ciphertext[28..], ciphertext.Slice(12, 16), output);
        return output;
    }
}

public sealed class PostgresIdempotencyRepository(NpgsqlDataSource dataSource, IPayloadProtector protector)
    : IIdempotencyRepository
{
    private const string Columns = "id,tenant,route,key,fingerprint,state,status_code,response_headers,response_body,request_body,error,created_at,updated_at,expires_at,timeline";

    public async Task<ClaimResult> ClaimAsync(IdempotencyRecord candidate, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO retryshield_records
            (id,tenant,route,key,fingerprint,state,request_body,created_at,updated_at,expires_at,timeline)
            VALUES (@id,@tenant,@route,@key,@fingerprint,'processing',@request,@created,@updated,@expires,@timeline::jsonb)
            ON CONFLICT (tenant,route,key) DO NOTHING
            """, conn, tx);
        AddIdentity(insert, candidate);
        insert.Parameters.AddWithValue("request", (object?)candidate.ProtectedRequestBody ?? DBNull.Value);
        insert.Parameters.AddWithValue("created", candidate.CreatedAt);
        insert.Parameters.AddWithValue("updated", candidate.UpdatedAt);
        insert.Parameters.AddWithValue("expires", candidate.ExpiresAt);
        insert.Parameters.AddWithValue("timeline", JsonSerializer.Serialize(candidate.Timeline));
        var inserted = await insert.ExecuteNonQueryAsync(ct) == 1;
        IdempotencyRecord record = candidate;
        if (!inserted)
        {
            await using var select = new NpgsqlCommand($"SELECT {Columns} FROM retryshield_records WHERE tenant=@tenant AND route=@route AND key=@key FOR UPDATE", conn, tx);
            select.Parameters.AddWithValue("tenant", candidate.Tenant);
            select.Parameters.AddWithValue("route", candidate.Route);
            select.Parameters.AddWithValue("key", candidate.Key);
            await using var reader = await select.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            record = Read(reader);
        }
        await tx.CommitAsync(ct);
        return new(inserted ? ClaimKind.Claimed :
            CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(record.Fingerprint),
                System.Text.Encoding.UTF8.GetBytes(candidate.Fingerprint)) ? ClaimKind.Existing : ClaimKind.FingerprintMismatch, record);
    }

    public Task<IdempotencyRecord?> GetAsync(string tenant, string route, string key, CancellationToken ct) =>
        QueryOneAsync("tenant=@tenant AND route=@route AND key=@key", [("tenant", tenant), ("route", route), ("key", key)], ct);
    public Task<IdempotencyRecord?> GetByIdAsync(Guid id, CancellationToken ct) =>
        QueryOneAsync("id=@id", [("id", id)], ct);

    private async Task<IdempotencyRecord?> QueryOneAsync(string where, (string, object)[] args, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand($"SELECT {Columns} FROM retryshield_records WHERE {where}", conn);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task SaveAsync(IdempotencyRecord record, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE retryshield_records SET state=@state,status_code=@status,response_headers=@headers::jsonb,
            response_body=@body,error=@error,updated_at=@updated,timeline=@timeline::jsonb WHERE id=@id
            """, conn);
        cmd.Parameters.AddWithValue("id", record.Id);
        cmd.Parameters.AddWithValue("state", record.State.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue("status", (object?)record.Response?.StatusCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("headers", record.Response is null ? DBNull.Value : JsonSerializer.Serialize(record.Response.Headers));
        cmd.Parameters.AddWithValue("body", record.Response is null ? DBNull.Value : protector.Protect(record.Response.Body));
        cmd.Parameters.AddWithValue("error", (object?)record.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("updated", record.UpdatedAt);
        cmd.Parameters.AddWithValue("timeline", JsonSerializer.Serialize(record.Timeline));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<IdempotencyRecord>> ListAsync(RecordQuery query, CancellationToken ct)
    {
        var where = new List<string>();
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand { Connection = conn };
        if (query.Tenant is not null) { where.Add("tenant=@tenant"); cmd.Parameters.AddWithValue("tenant", query.Tenant); }
        if (query.State is not null) { where.Add("state=@state"); cmd.Parameters.AddWithValue("state", query.State.Value.ToString().ToLowerInvariant()); }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            where.Add("(key ILIKE @search OR tenant ILIKE @search OR route ILIKE @search)");
            cmd.Parameters.AddWithValue("search", $"%{EscapeLike(query.Search.Trim())}%");
        }
        cmd.CommandText = $"SELECT {Columns} FROM retryshield_records {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")} ORDER BY created_at DESC OFFSET @offset LIMIT @limit";
        cmd.Parameters.AddWithValue("offset", Math.Max(0, query.Offset));
        cmd.Parameters.AddWithValue("limit", Math.Clamp(query.Limit, 1, 500));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<IdempotencyRecord>();
        while (await reader.ReadAsync(ct)) result.Add(Read(reader));
        return result;
    }

    public async Task<RecordStats> StatsAsync(string? tenant, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT state,count(*) FROM retryshield_records WHERE (@tenant IS NULL OR tenant=@tenant) GROUP BY state", conn);
        cmd.Parameters.AddWithValue("tenant", (object?)tenant ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var states = new Dictionary<RecordState, long>();
        while (await reader.ReadAsync(ct)) states[ParseState(reader.GetString(0))] = reader.GetInt64(1);
        return new(states.Values.Sum(), states);
    }

    public async Task<int> PurgeExpiredAsync(DateTimeOffset before, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM retryshield_records WHERE expires_at < @before AND state IN ('completed','failed','indeterminate','expired')",
            conn);
        cmd.Parameters.AddWithValue("before", before);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> MarkStaleProcessingIndeterminateAsync(DateTimeOffset before, CancellationToken ct)
    {
        const string reason = "Gateway stopped before the upstream outcome was durably recorded.";
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE retryshield_records
            SET state='indeterminate', error=@reason, updated_at=now(),
                timeline=timeline || jsonb_build_array(jsonb_build_object(
                    'At', now(), 'State', 3, 'Note', @reason))
            WHERE state='processing' AND updated_at < @before
            """, conn);
        cmd.Parameters.AddWithValue("reason", reason);
        cmd.Parameters.AddWithValue("before", before);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private IdempotencyRecord Read(NpgsqlDataReader r)
    {
        var response = r.IsDBNull(6) ? null : new StoredResponse(r.GetInt32(6),
            JsonSerializer.Deserialize<Dictionary<string, string[]>>(r.GetString(7))!,
            protector.Unprotect((byte[])r[8]));
        var record = new IdempotencyRecord
        {
            Id = r.GetGuid(0),
            Tenant = r.GetString(1),
            Route = r.GetString(2),
            Key = r.GetString(3),
            Fingerprint = r.GetString(4),
            ExpiresAt = r.GetFieldValue<DateTimeOffset>(13),
            ProtectedRequestBody = r.IsDBNull(9) ? null : (byte[])r[9]
        };
        var timeline = JsonSerializer.Deserialize<List<RecordEvent>>(r.GetString(14)) ?? [];
        record.Rehydrate(ParseState(r.GetString(5)), response, r.IsDBNull(10) ? null : r.GetString(10),
            r.GetFieldValue<DateTimeOffset>(12), timeline);
        return record;
    }
    private static RecordState ParseState(string state) => Enum.Parse<RecordState>(state, true);
    private static string EscapeLike(string value) => value.Replace(@"\", @"\\", StringComparison.Ordinal)
        .Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);
    private static void AddIdentity(NpgsqlCommand cmd, IdempotencyRecord r)
    {
        cmd.Parameters.AddWithValue("id", r.Id); cmd.Parameters.AddWithValue("tenant", r.Tenant);
        cmd.Parameters.AddWithValue("route", r.Route); cmd.Parameters.AddWithValue("key", r.Key);
        cmd.Parameters.AddWithValue("fingerprint", r.Fingerprint);
    }
}

public sealed class RecordNotifier : IRecordNotifier, IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _waiters = new();
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<RecordNotifier> _logger;
    public RecordNotifier(IOptions<RetryShieldOptions> options, ILogger<RecordNotifier> logger)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(options.Value.RedisConnectionString))
        {
            try
            {
                var redisOptions = ConfigurationOptions.Parse(options.Value.RedisConnectionString);
                redisOptions.AbortOnConnectFail = false;
                redisOptions.ConnectRetry = 1;
                redisOptions.ConnectTimeout = 2_000;
                _redis = ConnectionMultiplexer.Connect(redisOptions);
                _redis.GetSubscriber().Subscribe(RedisChannel.Literal("retryshield:records"), (_, value) =>
                {
                    if (Guid.TryParse(value.ToString(), out var id) &&
                        _waiters.TryRemove(id, out var waiter)) waiter.TrySetResult();
                });
            }
            catch (RedisException exception)
            {
                logger.LogWarning(exception, "Redis is unavailable; continuing with PostgreSQL as the authority");
            }
        }
    }
    public async Task PublishAsync(IdempotencyRecord record, CancellationToken ct)
    {
        if (_waiters.TryRemove(record.Id, out var waiter)) waiter.TrySetResult();
        if (_redis is null) return;
        try
        {
            await _redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal("retryshield:records"), record.Id.ToString());
        }
        catch (RedisException exception)
        {
            _logger.LogWarning(exception, "Redis publication failed; durable record {RecordId} remains authoritative",
                record.Id);
        }
    }
    public async Task<bool> WaitAsync(Guid id, TimeSpan timeout, CancellationToken ct)
    {
        var waiter = _waiters.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        try { await waiter.Task.WaitAsync(timeout, ct); return true; }
        catch (TimeoutException) { return false; }
        finally { _waiters.TryRemove(new KeyValuePair<Guid, TaskCompletionSource>(id, waiter)); }
    }
    public async ValueTask DisposeAsync() { if (_redis is not null) await _redis.DisposeAsync(); }
}

public sealed class CleanupWorker(IServiceScopeFactory scopeFactory, IOptions<RetryShieldOptions> options,
    ILogger<CleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CleanupInterval);
        do
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IIdempotencyRepository>();
            var indeterminate = await repository.MarkStaleProcessingIndeterminateAsync(
                DateTimeOffset.UtcNow.Subtract(options.Value.ProcessingTimeout), stoppingToken);
            var count = await repository.PurgeExpiredAsync(DateTimeOffset.UtcNow, stoppingToken);
            if (indeterminate > 0)
                logger.LogWarning("Marked {Count} stale processing records as indeterminate", indeterminate);
            if (count > 0) logger.LogInformation("Purged {Count} expired idempotency records", count);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRetryShieldInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<RetryShieldOptions>().Bind(config.GetSection("RetryShield"))
            .Validate(x => !string.IsNullOrWhiteSpace(x.PostgresConnectionString), "PostgreSQL connection string is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.EncryptionKeyBase64), "Encryption key is required.")
            .Validate(x => x.CleanupInterval > TimeSpan.Zero && x.ProcessingTimeout > TimeSpan.Zero,
                "Cleanup and processing timeouts must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp =>
        {
            var value = sp.GetRequiredService<IOptions<RetryShieldOptions>>().Value;
            return NpgsqlDataSource.Create(value.PostgresConnectionString);
        });
        services.AddSingleton<IPayloadProtector, AesGcmPayloadProtector>();
        services.AddSingleton<IRecordNotifier, RecordNotifier>();
        services.AddScoped<IIdempotencyRepository, PostgresIdempotencyRepository>();
        services.AddScoped<RetryShieldService>();
        services.AddHostedService<CleanupWorker>();
        return services;
    }

    public static async Task InitializeRetryShieldSchemaAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await RetryShieldSchemaMigrator.ApplyAsync(dataSource, ct);
    }
}
