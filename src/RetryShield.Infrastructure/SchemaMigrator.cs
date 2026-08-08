using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace RetryShield.Infrastructure;

public static class RetryShieldSchemaMigrator
{
    private const long AdvisoryLockId = 7_624_911_804_122_026_001;

    private static readonly Migration[] Migrations =
    [
        new(1, "v0.1_initial_schema", """
            CREATE TABLE IF NOT EXISTS retryshield_records (
              id uuid PRIMARY KEY, tenant text NOT NULL, route text NOT NULL, key text NOT NULL,
              fingerprint text NOT NULL, state text NOT NULL, status_code integer,
              response_headers jsonb, response_body bytea, request_body bytea, error text,
              created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
              timeline jsonb NOT NULL,
              CONSTRAINT ck_retryshield_state CHECK (state IN ('processing','completed','failed','indeterminate','expired')),
              CONSTRAINT ck_retryshield_key_length CHECK (char_length(key) BETWEEN 1 AND 256),
              UNIQUE (tenant,route,key));
            CREATE INDEX IF NOT EXISTS ix_retryshield_records_expiry
              ON retryshield_records(expires_at);
            CREATE INDEX IF NOT EXISTS ix_retryshield_records_tenant_state
              ON retryshield_records(tenant,state);
            """)
    ];

    public static int CurrentVersion => Migrations[^1].Version;

    public static async Task ApplyAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@lock_id)", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("lock_id", AdvisoryLockId);
            await lockCommand.ExecuteNonQueryAsync(ct);
        }

        await using (var historyCommand = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS retryshield_schema_migrations (
              version integer PRIMARY KEY,
              name text NOT NULL,
              checksum text NOT NULL,
              applied_at timestamptz NOT NULL DEFAULT now()
            )
            """, connection, transaction))
        {
            await historyCommand.ExecuteNonQueryAsync(ct);
        }

        var applied = new Dictionary<int, AppliedMigration>();
        await using (var versionCommand = new NpgsqlCommand(
            "SELECT version,name,checksum FROM retryshield_schema_migrations ORDER BY version",
            connection, transaction))
        {
            await using var reader = await versionCommand.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                applied.Add(reader.GetInt32(0), new(reader.GetString(1), reader.GetString(2)));
        }

        var unsupportedVersion = applied.Keys.FirstOrDefault(version => version > CurrentVersion);
        if (unsupportedVersion > 0)
        {
            throw new InvalidOperationException(
                $"Database schema version {unsupportedVersion} is newer than supported version {CurrentVersion}.");
        }

        foreach (var migration in Migrations)
        {
            var checksum = Checksum(migration.Sql);
            if (applied.TryGetValue(migration.Version, out var existing))
            {
                if (!StringComparer.Ordinal.Equals(existing.Name, migration.Name) ||
                    !StringComparer.Ordinal.Equals(existing.Checksum, checksum))
                {
                    throw new InvalidOperationException(
                        $"Schema migration {migration.Version} differs from the version recorded in the database.");
                }
                continue;
            }

            await using var migrationCommand = new NpgsqlCommand(migration.Sql, connection, transaction);
            await migrationCommand.ExecuteNonQueryAsync(ct);

            await using var recordCommand = new NpgsqlCommand("""
                INSERT INTO retryshield_schema_migrations(version,name,checksum)
                VALUES (@version,@name,@checksum)
                """, connection, transaction);
            recordCommand.Parameters.AddWithValue("version", migration.Version);
            recordCommand.Parameters.AddWithValue("name", migration.Name);
            recordCommand.Parameters.AddWithValue("checksum", checksum);
            await recordCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    private static string Checksum(string sql) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();

    private sealed record Migration(int Version, string Name, string Sql);
    private sealed record AppliedMigration(string Name, string Checksum);
}
