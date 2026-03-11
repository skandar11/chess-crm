using System.Text;
using System.Text.Json;
using ChessCrm.Bot.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ChessCrm.Bot.Services;

/// <summary>Найденный клиент (для fuzzy-поиска).</summary>
public record ClientMatch(int Id, string FullName);

/// <summary>Результат проверки дубля имени (exact или fuzzy).</summary>
public record DuplicateMatch(int Id, string FullName, bool IsExact);

/// <summary>Данные для создания нового клиента.</summary>
public record NewClientRecord(
    string FullName,
    DateOnly? BirthDate = null,
    string? ParentName = null,
    string? ParentPhone = null,
    string? ParentName2 = null,
    string? ParentPhone2 = null,
    string? Level = null
);

/// <summary>Данные для создания абонемента.</summary>
public record SubscriptionRecord(
    int ClientId,
    DateOnly Month,      // первый день месяца: 2026-04-01
    int TotalLessons,
    decimal Price,
    string? Recipient = null
);

/// <summary>Расписание ребёнка (для родительского меню).</summary>
public record ChildScheduleRecord(string GroupName, string[] DayOfWeek, TimeOnly TimeStart, string? Coach, string Level);

/// <summary>Данные ребёнка (для родительского меню).</summary>
public record ChildInfoRecord(
    string FullName, DateOnly? BirthDate, string? Level,
    string? ParentName, string? ParentPhone, string? ParentName2, string? ParentPhone2,
    int SubscriptionsCount);

/// <summary>Последний абонемент ребёнка (для родительского меню).</summary>
public record ChildSubscriptionRecord(DateOnly Month, int TotalLessons, decimal Price, string? Recipient);

public class DatabaseService(AppConfig config, ILogger<DatabaseService> logger)
{
    // Выполняет SELECT и возвращает результат как JSON-строку
    public async Task<string> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        GuardReadOnly(sql);

        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.CommandTimeout = 30;

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var rows = new List<Dictionary<string, object?>>();

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var colName = reader.GetName(i);
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[colName] = value is TimeSpan ts ? ts.ToString(@"hh\:mm") : value;
            }
            rows.Add(row);
        }

        logger.LogInformation("SQL вернул {Count} строк:\n{Json}", rows.Count,
            JsonSerializer.Serialize(rows, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }));

        if (rows.Count == 0) return "[{\"result\": \"Данных не найдено\"}]";

        return JsonSerializer.Serialize(rows, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    // ── Read helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Ищет клиентов по имени (full_name).
    /// Возвращает список совпадений. Один результат = нашли точно.
    /// </summary>
    public async Task<List<ClientMatch>> FindClientByNameAsync(string name, CancellationToken ct = default)
    {
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        var found = new Dictionary<int, ClientMatch>();

        foreach (var part in parts)
        {
            const string sql = """
                SELECT id, full_name
                FROM clients
                WHERE full_name ILIKE @p
                  AND is_active = true
                ORDER BY full_name
                LIMIT 10
                """;

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p", $"%{part}%");

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt32(0);
                if (!found.ContainsKey(id))
                    found[id] = new ClientMatch(id, reader.GetString(1));
            }
        }

        var result = found.Values.ToList();

        // Если есть точное совпадение по слову — оставляем только точные
        var exactMatches = result.Where(m =>
            parts.Any(part =>
                m.FullName.Split(' ').Any(word =>
                    string.Equals(word, part, StringComparison.OrdinalIgnoreCase))
            )
        ).ToList();

        if (exactMatches.Count > 0)
            result = exactMatches;

        logger.LogInformation("FindClientByName('{Name}'): найдено {Count} (exact={Exact})",
            name, result.Count, exactMatches.Count);
        return result;
    }

    /// <summary>
    /// Проверяет дубли по имени: exact (LOWER) + fuzzy (pg_trgm).
    /// Если pg_trgm не установлен — возвращает только exact.
    /// </summary>
    public async Task<List<DuplicateMatch>> CheckDuplicateClientAsync(string fullName, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        var result = new List<DuplicateMatch>();

        // Exact match (case-insensitive)
        const string exactSql = """
            SELECT id, full_name
            FROM clients
            WHERE LOWER(full_name) = LOWER(@name)
              AND is_active = true
            """;

        await using (var cmd = new NpgsqlCommand(exactSql, conn))
        {
            cmd.Parameters.AddWithValue("name", fullName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(new DuplicateMatch(reader.GetInt32(0), reader.GetString(1), IsExact: true));
        }

        // Fuzzy match (pg_trgm) — может не быть расширения
        try
        {
            const string fuzzySql = """
                SELECT id, full_name
                FROM clients
                WHERE full_name % @name
                  AND is_active = true
                  AND LOWER(full_name) != LOWER(@name)
                ORDER BY similarity(full_name, @name) DESC
                LIMIT 5
                """;

            await using var cmd = new NpgsqlCommand(fuzzySql, conn);
            cmd.Parameters.AddWithValue("name", fullName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(new DuplicateMatch(reader.GetInt32(0), reader.GetString(1), IsExact: false));
        }
        catch (PostgresException ex) when (ex.SqlState == "42883") // undefined function
        {
            logger.LogWarning("pg_trgm не установлен — fuzzy-поиск пропущен");
        }

        return result;
    }

    /// <summary>
    /// Ищет клиентов с таким же номером телефона (сравнение только по цифрам).
    /// </summary>
    public async Task<List<ClientMatch>> FindClientsByPhoneAsync(string phoneDigits, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT id, full_name
            FROM clients
            WHERE REGEXP_REPLACE(parent_phone, '\D', '', 'g') = @digits
              AND is_active = true
            LIMIT 5
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("digits", phoneDigits);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<ClientMatch>();
        while (await reader.ReadAsync(ct))
            result.Add(new ClientMatch(reader.GetInt32(0), reader.GetString(1)));

        return result;
    }

    // ── Write ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Создаёт нового клиента. Возвращает id новой записи.
    /// </summary>
    public async Task<int> CreateClientAsync(NewClientRecord record, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            INSERT INTO clients
                (full_name, birth_date, parent_name, parent_phone, parent_name_2, parent_phone_2, level, is_active, created_at)
            VALUES
                (@fullName, @birthDate, @parentName, @parentPhone, @parentName2, @parentPhone2, @level, true, NOW())
            RETURNING id
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("fullName",     record.FullName);
        cmd.Parameters.AddWithValue("birthDate",    (object?)record.BirthDate    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("parentName",   (object?)record.ParentName   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("parentPhone",  (object?)record.ParentPhone  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("parentName2",  (object?)record.ParentName2  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("parentPhone2", (object?)record.ParentPhone2 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("level",        (object?)record.Level        ?? DBNull.Value);

        var newId = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);

        logger.LogInformation("CreateClient: id={Id}, name={FullName}", newId, record.FullName);

        return newId;
    }

    // ── Group operations ─────────────────────────────────────────────────────

    public record GroupInfo(int Id, string Name, int CurrentCount, int MaxStudents);

    /// <summary>
    /// Ищет группу по имени, id, дню+времени, или дню+часу (fallback).
    /// Каскад: groupName → groupId → day+time (exact) → day+hour (fallback).
    /// </summary>
    public async Task<GroupInfo?> FindGroupAsync(
        DayOfWeek? day, TimeSpan? time, int? groupId,
        CancellationToken ct = default, string? groupName = null)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        // Step 1: by groupName
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            var result = await QueryGroupAsync(conn, ct, """
                SELECT g.id, g.name, g.max_students,
                       COUNT(gs.client_id) FILTER (WHERE gs.left_at IS NULL) AS current_count
                FROM groups g
                LEFT JOIN group_students gs ON gs.group_id = g.id
                WHERE LOWER(g.name) = LOWER(@name) AND g.is_active = true
                GROUP BY g.id, g.name, g.max_students
                """, cmd => cmd.Parameters.AddWithValue("name", groupName));
            if (result != null) return result;
        }

        // Step 2: by groupId
        if (groupId.HasValue)
        {
            var result = await QueryGroupAsync(conn, ct, """
                SELECT g.id, g.name, g.max_students,
                       COUNT(gs.client_id) FILTER (WHERE gs.left_at IS NULL) AS current_count
                FROM groups g
                LEFT JOIN group_students gs ON gs.group_id = g.id
                WHERE g.id = @groupId AND g.is_active = true
                GROUP BY g.id, g.name, g.max_students
                """, cmd => cmd.Parameters.AddWithValue("groupId", groupId.Value));
            if (result != null) return result;
        }

        if (!day.HasValue) return null;
        var dayName = day.Value.ToString().ToLowerInvariant();

        // Step 3: by day + exact time
        if (time.HasValue)
        {
            var result = await QueryGroupAsync(conn, ct, """
                SELECT g.id, g.name, g.max_students,
                       COUNT(gs.client_id) FILTER (WHERE gs.left_at IS NULL) AS current_count
                FROM groups g
                LEFT JOIN group_students gs ON gs.group_id = g.id
                WHERE @day = ANY(g.day_of_week)
                  AND g.time_start = @time
                  AND g.is_active  = true
                GROUP BY g.id, g.name, g.max_students
                LIMIT 1
                """, cmd =>
            {
                cmd.Parameters.AddWithValue("day", dayName);
                cmd.Parameters.AddWithValue("time", time.Value);
            });
            if (result != null) return result;
        }

        // Step 4: fallback — day + hour only (ignore minutes)
        if (time.HasValue)
        {
            var result = await QueryGroupAsync(conn, ct, """
                SELECT g.id, g.name, g.max_students,
                       COUNT(gs.client_id) FILTER (WHERE gs.left_at IS NULL) AS current_count
                FROM groups g
                LEFT JOIN group_students gs ON gs.group_id = g.id
                WHERE @day = ANY(g.day_of_week)
                  AND EXTRACT(HOUR FROM g.time_start) = @hour
                  AND g.is_active = true
                GROUP BY g.id, g.name, g.max_students
                LIMIT 1
                """, cmd =>
            {
                cmd.Parameters.AddWithValue("day", dayName);
                cmd.Parameters.AddWithValue("hour", (double)time.Value.Hours);
            });
            if (result != null) return result;
        }

        return null;
    }

    private static async Task<GroupInfo?> QueryGroupAsync(
        NpgsqlConnection conn, CancellationToken ct,
        string sql, Action<NpgsqlCommand> addParams)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        addParams(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new GroupInfo(
            reader.GetInt32(0),
            reader.GetString(1),
            (int)reader.GetInt64(3),
            reader.GetInt32(2));
    }

    /// <summary>Проверяет, записан ли уже клиент в эту группу (активный).</summary>
    public async Task<bool> IsClientInGroupAsync(int clientId, int groupId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT COUNT(1) FROM group_students
            WHERE client_id = @clientId AND group_id = @groupId AND left_at IS NULL
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("clientId", clientId);
        cmd.Parameters.AddWithValue("groupId",  groupId);

        return (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L) > 0;
    }

    /// <summary>Добавляет клиента в группу (group_students).</summary>
    public async Task AddClientToGroupAsync(int clientId, int groupId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            INSERT INTO group_students (group_id, client_id, joined_at, created_at)
            VALUES (@groupId, @clientId, CURRENT_DATE, NOW())
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("groupId",  groupId);
        cmd.Parameters.AddWithValue("clientId", clientId);

        await cmd.ExecuteNonQueryAsync(ct);

        logger.LogInformation("AddToGroup: client={ClientId} → group={GroupId}", clientId, groupId);
    }

    /// <summary>Убирает клиента из группы (ставит left_at = сегодня). Возвращает true если запись обновлена.</summary>
    public async Task<bool> RemoveClientFromGroupAsync(int clientId, int groupId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            UPDATE group_students
            SET left_at = CURRENT_DATE
            WHERE client_id = @clientId AND group_id = @groupId AND left_at IS NULL
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("clientId", clientId);
        cmd.Parameters.AddWithValue("groupId",  groupId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        logger.LogInformation("RemoveFromGroup: client={ClientId}, group={GroupId}, rows={Rows}",
            clientId, groupId, rows);

        return rows > 0;
    }

    // ── Subscriptions ──────────────────────────────────────────────────────────

    /// <summary>Возвращает активные группы клиента (left_at IS NULL).</summary>
    public async Task<List<GroupInfo>> GetClientActiveGroupsAsync(int clientId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT g.id, g.name, g.max_students,
                   COUNT(gs2.client_id) FILTER (WHERE gs2.left_at IS NULL) AS current_count
            FROM groups g
            JOIN group_students gs ON gs.group_id = g.id
            LEFT JOIN group_students gs2 ON gs2.group_id = g.id
            WHERE gs.client_id = @clientId AND gs.left_at IS NULL AND g.is_active = true
            GROUP BY g.id, g.name, g.max_students
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("clientId", clientId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<GroupInfo>();

        while (await reader.ReadAsync(ct))
        {
            result.Add(new GroupInfo(
                reader.GetInt32(0),
                reader.GetString(1),
                (int)reader.GetInt64(3),
                reader.GetInt32(2)));
        }

        return result;
    }

    /// <summary>Проверяет, есть ли уже абонемент за указанный месяц для клиента.</summary>
    public async Task<bool> HasSubscriptionForMonthAsync(
        int clientId, DateOnly month, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT COUNT(1) FROM subscriptions
            WHERE client_id = @clientId
              AND month     = @month
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("clientId", clientId);
        cmd.Parameters.AddWithValue("month",    month);

        return (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L) > 0;
    }

    /// <summary>Создаёт абонемент и увеличивает счётчик. Возвращает id новой записи.</summary>
    public async Task<int> CreateSubscriptionAsync(SubscriptionRecord record, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string insertSql = """
            INSERT INTO subscriptions
                (client_id, month, total_lessons, price, recipient, created_at)
            VALUES
                (@clientId, @month, @totalLessons, @price, @recipient, NOW())
            RETURNING id
            """;

        await using var cmd = new NpgsqlCommand(insertSql, conn);
        cmd.Parameters.AddWithValue("clientId",     record.ClientId);
        cmd.Parameters.AddWithValue("month",        record.Month);
        cmd.Parameters.AddWithValue("totalLessons", record.TotalLessons);
        cmd.Parameters.AddWithValue("price",        record.Price);
        cmd.Parameters.AddWithValue("recipient",    (object?)record.Recipient ?? DBNull.Value);

        var newId = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);

        // Инкрементируем счётчик абонементов
        const string updateSql = """
            UPDATE clients SET subscriptions_count = subscriptions_count + 1
            WHERE id = @clientId
            """;
        await using var updateCmd = new NpgsqlCommand(updateSql, conn);
        updateCmd.Parameters.AddWithValue("clientId", record.ClientId);
        await updateCmd.ExecuteNonQueryAsync(ct);

        logger.LogInformation("CreateSubscription: id={Id}, client={ClientId}, month={Month}",
            newId, record.ClientId, record.Month);

        return newId;
    }

    // ── Edit student ──────────────────────────────────────────────────────────

    private static readonly HashSet<string> EditableClientFields = new(StringComparer.Ordinal)
    {
        "birth_date", "parent_name", "parent_phone",
        "parent_name_2", "parent_phone_2", "level",
        "notes", "is_active", "subscriptions_count"
    };

    /// <summary>
    /// Читает указанные поля клиента из БД. Возвращает словарь field_name → value.
    /// Принимает только поля из белого списка.
    /// </summary>
    public async Task<Dictionary<string, object?>> GetClientFieldsAsync(
        int clientId, IEnumerable<string> fieldNames, CancellationToken ct = default)
    {
        var safeFields = fieldNames.Where(f => EditableClientFields.Contains(f)).ToList();
        if (safeFields.Count == 0)
            return new Dictionary<string, object?>();

        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        var columns = string.Join(", ", safeFields);
        var sql = $"SELECT {columns} FROM clients WHERE id = @id";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", clientId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, object?>();

        if (await reader.ReadAsync(ct))
        {
            for (int i = 0; i < safeFields.Count; i++)
                result[safeFields[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return result;
    }

    /// <summary>
    /// Обновляет указанные поля клиента. Принимает только поля из белого списка.
    /// Возвращает true если запись обновлена.
    /// </summary>
    public async Task<bool> UpdateClientFieldsAsync(
        int clientId, Dictionary<string, object?> fields, CancellationToken ct = default)
    {
        var safeFields = fields.Where(f => EditableClientFields.Contains(f.Key)).ToList();
        if (safeFields.Count == 0)
            return false;

        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        var setClauses = new List<string>();
        var cmd = new NpgsqlCommand();
        cmd.Connection = conn;

        for (int i = 0; i < safeFields.Count; i++)
        {
            var paramName = $"@p{i}";
            setClauses.Add($"{safeFields[i].Key} = {paramName}");
            cmd.Parameters.AddWithValue($"p{i}", safeFields[i].Value ?? DBNull.Value);
        }

        cmd.Parameters.AddWithValue("id", clientId);
        cmd.CommandText = $"UPDATE clients SET {string.Join(", ", setClauses)} WHERE id = @id";

        await using (cmd)
        {
            var rows = await cmd.ExecuteNonQueryAsync(ct);
            logger.LogInformation("UpdateClientFields: id={Id}, fields=[{Fields}], rows={Rows}",
                clientId, string.Join(", ", safeFields.Select(f => f.Key)), rows);
            return rows > 0;
        }
    }

    // ── Roles & Invites ────────────────────────────────────────────────────────

    /// <summary>Возвращает роль и client_id пользователя Telegram. null если не найден.</summary>
    public async Task<(string Role, int? ClientId)?> GetUserRoleAsync(long tgId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = "SELECT role, client_id FROM user_roles WHERE tg_id = @tgId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tgId", tgId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var role = reader.GetString(0);
        var clientId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1);
        return (role, clientId);
    }

    /// <summary>Сохраняет invite_token и invite_expires_at для клиента. Перезаписывает предыдущий токен.</summary>
    public async Task<bool> CreateInviteTokenAsync(
        int clientId, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            UPDATE clients
            SET invite_token = @token,
                invite_expires_at = @expiresAt,
                invite_activated_at = NULL
            WHERE id = @clientId
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("token", token);
        cmd.Parameters.AddWithValue("expiresAt", expiresAt);
        cmd.Parameters.AddWithValue("clientId", clientId);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        logger.LogInformation("CreateInviteToken: clientId={ClientId}, rows={Rows}", clientId, rows);
        return rows > 0;
    }

    /// <summary>
    /// Активирует инвайт: привязывает Telegram-аккаунт родителя к ученику, создаёт роль parent.
    /// Возвращает (success, clientFullName, error).
    /// </summary>
    public async Task<(bool Success, string? ClientFullName, string? Error)> ActivateInviteAsync(
        string token, long tgId, string? tgUsername, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // 1. Find client by token
            const string selectSql = """
                SELECT id, full_name, invite_expires_at, invite_activated_at
                FROM clients WHERE invite_token = @token
                """;

            await using var selectCmd = new NpgsqlCommand(selectSql, conn, tx);
            selectCmd.Parameters.AddWithValue("token", token);

            int clientId;
            string fullName;
            DateTimeOffset? expiresAt;
            DateTimeOffset? activatedAt;

            await using (var reader = await selectCmd.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    return (false, null, "not_found");

                clientId = reader.GetInt32(0);
                fullName = reader.GetString(1);
                expiresAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2);
                activatedAt = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3);
            }

            // 2. Validate
            if (activatedAt.HasValue)
                return (false, null, "already_activated");

            if (expiresAt.HasValue && expiresAt.Value < DateTimeOffset.UtcNow)
                return (false, null, "expired");

            // 3. Update client
            const string updateSql = """
                UPDATE clients
                SET parent_tg_id = @tgId,
                    parent_tg_username = @tgUsername,
                    invite_activated_at = NOW()
                WHERE id = @clientId
                """;

            await using var updateCmd = new NpgsqlCommand(updateSql, conn, tx);
            updateCmd.Parameters.AddWithValue("tgId", tgId);
            updateCmd.Parameters.AddWithValue("tgUsername", (object?)tgUsername ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("clientId", clientId);
            await updateCmd.ExecuteNonQueryAsync(ct);

            // 4. Insert user_roles
            const string roleSql = """
                INSERT INTO user_roles (tg_id, role, client_id)
                VALUES (@tgId, 'parent', @clientId)
                ON CONFLICT (tg_id) DO UPDATE SET role = 'parent', client_id = @clientId
                """;

            await using var roleCmd = new NpgsqlCommand(roleSql, conn, tx);
            roleCmd.Parameters.AddWithValue("tgId", tgId);
            roleCmd.Parameters.AddWithValue("clientId", clientId);
            await roleCmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);

            logger.LogInformation("ActivateInvite: token activated for client={ClientId} ({FullName}), tgId={TgId}",
                clientId, fullName, tgId);

            return (true, fullName, null);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>Возвращает full_name клиента по id (для приветствия родителя).</summary>
    public async Task<string?> GetClientInfoForParentAsync(int clientId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = "SELECT full_name FROM clients WHERE id = @clientId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("clientId", clientId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    // ── Parent queries ──────────────────────────────────────────────────────

    /// <summary>Возвращает активные группы ребёнка с расписанием (для родительского меню).</summary>
    public async Task<List<ChildScheduleRecord>> GetChildScheduleAsync(int clientId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT g.name, g.day_of_week, g.time_start, g.coach, g.level
            FROM groups g
            JOIN group_students gs ON g.id = gs.group_id
            WHERE gs.client_id = @clientId AND gs.left_at IS NULL AND g.is_active = true
            ORDER BY g.time_start
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("clientId", clientId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<ChildScheduleRecord>();

        while (await reader.ReadAsync(ct))
        {
            result.Add(new ChildScheduleRecord(
                reader.GetString(0),
                reader.GetFieldValue<string[]>(1),
                TimeOnly.FromTimeSpan(reader.GetFieldValue<TimeSpan>(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4)));
        }

        return result;
    }

    /// <summary>Возвращает данные ребёнка для родительского меню.</summary>
    public async Task<ChildInfoRecord?> GetChildInfoAsync(int clientId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT full_name, birth_date, level, parent_name, parent_phone,
                   parent_name_2, parent_phone_2, subscriptions_count
            FROM clients WHERE id = @clientId
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("clientId", clientId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new ChildInfoRecord(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt32(7));
    }

    /// <summary>Возвращает последний абонемент ребёнка (или null).</summary>
    public async Task<ChildSubscriptionRecord?> GetChildSubscriptionAsync(int clientId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(config.DbConnectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT month, total_lessons, price, recipient
            FROM subscriptions
            WHERE client_id = @clientId
            ORDER BY month DESC
            LIMIT 1
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("clientId", clientId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new ChildSubscriptionRecord(
            reader.GetFieldValue<DateOnly>(0),
            reader.GetInt32(1),
            reader.GetDecimal(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    // ── Guard ─────────────────────────────────────────────────────────────────

    private static void GuardReadOnly(string sql)
    {
        var normalized = sql.ToUpperInvariant();
        string[] forbidden = ["INSERT", "UPDATE", "DELETE", "DROP", "TRUNCATE", "ALTER", "CREATE", "GRANT", "REVOKE"];

        foreach (var keyword in forbidden)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, $@"\b{keyword}\b"))
            {
                throw new InvalidOperationException(
                    $"Запрос содержит запрещённую операцию: {keyword}. Бот работает только в режиме чтения.");
            }
        }
    }
}
