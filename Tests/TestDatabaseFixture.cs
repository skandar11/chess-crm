using Npgsql;

namespace Tests;

/// <summary>
/// Shared fixture: creates chess_crm_test database once per test run,
/// seeds reference data (groups), provides connection string.
/// </summary>
public class TestDatabaseFixture : IAsyncLifetime
{
    private const string AdminConnStr = "Host=localhost;Port=5432;Database=postgres";
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // ── Create test database if not exists ──────────────────────────
        await using var adminConn = new NpgsqlConnection(AdminConnStr);
        await adminConn.OpenAsync();

        await using (var check = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = 'chess_crm_test'", adminConn))
        {
            var exists = await check.ExecuteScalarAsync();
            if (exists == null)
            {
                await using var create = new NpgsqlCommand("CREATE DATABASE chess_crm_test", adminConn);
                await create.ExecuteNonQueryAsync();
            }
        }

        ConnectionString = "Host=localhost;Port=5432;Database=chess_crm_test";

        // ── Create tables ───────────────────────────────────────────────
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS clients (
                id                  serial          PRIMARY KEY,
                full_name           text            NOT NULL,
                birth_date          date,
                parent_name         text,
                parent_phone        text,
                parent_name_2       text,
                parent_phone_2      text,
                parent_tg_id        bigint,
                parent_tg_username  text,
                invite_token        text,
                invite_expires_at   timestamptz,
                invite_activated_at timestamptz,
                is_active           bool            NOT NULL DEFAULT true,
                level               text,
                subscriptions_count int             NOT NULL DEFAULT 0,
                notes               text,
                created_at          timestamptz     NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS groups (
                id            serial          PRIMARY KEY,
                name          text            NOT NULL,
                day_of_week   text[]          NOT NULL,
                time_start    time            NOT NULL,
                coach         text,
                level         text            NOT NULL,
                max_students  int             NOT NULL DEFAULT 8,
                is_active     bool            NOT NULL DEFAULT true,
                created_at    timestamptz     NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS group_students (
                id          serial          PRIMARY KEY,
                client_id   int             NOT NULL REFERENCES clients(id),
                group_id    int             NOT NULL REFERENCES groups(id),
                joined_at   date            NOT NULL DEFAULT CURRENT_DATE,
                left_at     date,
                created_at  timestamptz     NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS subscriptions (
                id              serial          PRIMARY KEY,
                client_id       int             NOT NULL REFERENCES clients(id),
                month           date            NOT NULL,
                total_lessons   int             NOT NULL DEFAULT 8,
                price           numeric(10,2)   NOT NULL,
                recipient       text,
                created_at      timestamptz     NOT NULL DEFAULT now(),
                UNIQUE (client_id, month)
            );

            CREATE TABLE IF NOT EXISTS user_roles (
                tg_id       bigint          PRIMARY KEY,
                role        text            NOT NULL,
                client_id   int             REFERENCES clients(id),
                created_at  timestamptz     NOT NULL DEFAULT now()
            );
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<TestDatabaseFixture>;
