using ChessCrm.Bot.Configuration;
using ChessCrm.Bot.Handlers;
using ChessCrm.Bot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Tests;

/// <summary>
/// Helper methods for creating services and seed data in tests.
/// Each test class seeds its own data and cleans up after.
/// </summary>
public static class TestHelper
{
    /// <summary>Creates an AppConfig that points to the test database.</summary>
    public static AppConfig CreateConfig(string connectionString)
    {
        Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", connectionString);
        Environment.SetEnvironmentVariable("TELEGRAM_BOT_TOKEN", "fake-token");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "fake-key");
        return new AppConfig();
    }

    public static DatabaseService CreateDbService(AppConfig config) =>
        new(config, NullLogger<DatabaseService>.Instance);

    public static AddToGroupHandler CreateAddToGroupHandler(DatabaseService db) =>
        new(db, NullLogger<AddToGroupHandler>.Instance);

    public static RemoveFromGroupHandler CreateRemoveFromGroupHandler(DatabaseService db) =>
        new(db, NullLogger<RemoveFromGroupHandler>.Instance);

    public static TransferGroupHandler CreateTransferGroupHandler(DatabaseService db) =>
        new(db, NullLogger<TransferGroupHandler>.Instance);

    // ── Seed helpers ──────────────────────────────────────────────────────

    public static async Task<int> InsertClientAsync(string connStr, string fullName)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        const string sql = """
            INSERT INTO clients (full_name, is_active, created_at)
            VALUES (@name, true, NOW())
            RETURNING id
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", fullName);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task<int> InsertGroupAsync(
        string connStr, string name, string dayOfWeek, TimeSpan timeStart,
        int maxStudents = 8)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        const string sql = """
            INSERT INTO groups (name, day_of_week, time_start, coach, level, max_students, is_active, created_at)
            VALUES (@name, @days, @time, 'Тест', 'тест', @max, true, NOW())
            RETURNING id
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("days", new[] { dayOfWeek });
        cmd.Parameters.AddWithValue("time", timeStart);
        cmd.Parameters.AddWithValue("max", maxStudents);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task InsertGroupStudentAsync(
        string connStr, int clientId, int groupId)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        const string sql = """
            INSERT INTO group_students (client_id, group_id, joined_at, created_at)
            VALUES (@clientId, @groupId, CURRENT_DATE, NOW())
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("clientId", clientId);
        cmd.Parameters.AddWithValue("groupId", groupId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Cleans all test data from tables (preserving table structure).</summary>
    public static async Task CleanAllAsync(string connStr)
    {
        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            DELETE FROM group_students;
            DELETE FROM subscriptions;
            DELETE FROM groups;
            DELETE FROM clients;
            DELETE FROM user_roles;
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
