using Npgsql;

namespace RetryShield.Infrastructure;

public static class RetryShieldSchemaMigrator
{
    private const long AdvisoryLockId = 7_624_911_804_122_026_001;

    private static readonly (int Version, string Sql)[] Migrations =
    [
        (1, """
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
              applied_at timestamptz NOT NULL DEFAULT now()
            )
            """, connection, transaction))
        {
            await historyCommand.ExecuteNonQueryAsync(ct);
        }

        int databaseVersion;
        await using (var versionCommand = new NpgsqlCommand(
            "SELECT COALESCE(MAX(version), 0) FROM retryshield_schema_migrations",
            connection, transaction))
        {
            databaseVersion = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(ct));
        }

        if (databaseVersion > CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {databaseVersion} is newer than supported version {CurrentVersion}.");
        }

        foreach (var migration in Migrations.Where(item => item.Version > databaseVersion))
        {
            await using var migrationCommand = new NpgsqlCommand(migration.Sql, connection, transaction);
            await migrationCommand.ExecuteNonQueryAsync(ct);

            await using var recordCommand = new NpgsqlCommand(
                "INSERT INTO retryshield_schema_migrations(version) VALUES (@version)",
                connection, transaction);
            recordCommand.Parameters.AddWithValue("version", migration.Version);
            await recordCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }
}
