using System.Text;
using System.Text.Json;
using ChessCrm.Bot.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ChessCrm.Bot.Services;

public class DatabaseService(AppConfig config, ILogger<DatabaseService> logger)
{
    // Выполняет SELECT и возвращает результат как JSON-строку
    public async Task<string> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        // Запрещаем любые мутирующие операции на уровне сервиса
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
                // TimeSpan не сериализуется нативно — переводим в строку
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

    private static void GuardReadOnly(string sql)
    {
        var normalized = sql.ToUpperInvariant();
        string[] forbidden = ["INSERT", "UPDATE", "DELETE", "DROP", "TRUNCATE", "ALTER", "CREATE", "GRANT", "REVOKE"];

        foreach (var keyword in forbidden)
        {
            // Проверяем как отдельное слово (избегаем false positive на "INSERTED" и т.п.)
            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, $@"\b{keyword}\b"))
            {
                throw new InvalidOperationException(
                    $"Запрос содержит запрещённую операцию: {keyword}. Бот работает только в режиме чтения.");
            }
        }
    }
}