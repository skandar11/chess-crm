using System.Text;
using System.Text.Json;
using ChessCrm.Bot.Configuration;
using ChessCrm.Bot.Prompts;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Services;

public class AiService(AppConfig config, HttpClient httpClient, ILogger<AiService> logger)
{
    // Генерация SQL через тяжёлую модель
    public async Task<string> GenerateSqlAsync(string userQuestion, CancellationToken ct)
    {
        var raw = await CallModelAsync(
            apiUrl: config.SqlModelApiUrl,
            modelName: config.SqlModelName,
            systemPrompt: ChessPrompts.GetSqlSystemPrompt(),
            userMessage: ChessPrompts.GetSqlUserPrompt(userQuestion),
            ct: ct
        );

        // Вырезаем SQL из возможного markdown-блока
        return ExtractSql(raw);
    }

    // Форматирование ответа через быструю модель
    public async Task<string> AnalyzeDataAsync(string userQuestion, string jsonData, CancellationToken ct)
    {
        return await CallModelAsync(
            apiUrl: config.ChatModelApiUrl,
            modelName: config.ChatModelName,
            systemPrompt: ChessPrompts.GetAnalysisSystemPrompt(),
            userMessage: ChessPrompts.GetAnalysisUserPrompt(userQuestion, jsonData),
            ct: ct
        );
    }

    // Исправляет SQL на основе ошибки PostgreSQL
    public async Task<string> FixSqlAsync(string originalQuestion, string brokenSql, string pgError, CancellationToken ct)
    {
        var message = $"""
        Ты написал SQL запрос, но он вернул ошибку PostgreSQL.
        
        Исходный вопрос: {originalQuestion}
        
        Твой SQL:
        {brokenSql}
        
        Ошибка PostgreSQL:
        {pgError}
        
        Исправь SQL. Помни:
        - JOIN groups g ON g.id = a.group_id ОБЯЗАТЕЛЕН если используешь g.start_time
        - Функций date() и time() нет в PostgreSQL, используй ::date и ::time
        - Пиши только исправленный SQL без объяснений
        """;

        var raw = await CallModelAsync(
            apiUrl: config.SqlModelApiUrl,
            modelName: config.SqlModelName,
            systemPrompt: ChessPrompts.GetSqlSystemPrompt(),
            userMessage: message,
            ct: ct
        );

        return ExtractSql(raw);
    }

    private async Task<string> CallModelAsync(
        string apiUrl, string modelName,
        string systemPrompt, string userMessage,
        CancellationToken ct)
    {
        var payload = new
        {
            model = modelName,
            temperature = 0.1,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userMessage  }
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        logger.LogDebug("→ Запрос к модели {Model} @ {Url}", modelName, apiUrl);

        var response = await httpClient.PostAsync(apiUrl, content, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var result = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        return result.Trim();
    }

    // Убираем ```sql ... ``` если модель завернула ответ в markdown
    private static string ExtractSql(string raw)
    {
        var start = raw.IndexOf("```sql", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += 6;
            var end = raw.IndexOf("```", start, StringComparison.OrdinalIgnoreCase);
            if (end > start) return raw[start..end].Trim();
        }

        // Пробуем без указания языка
        start = raw.IndexOf("```", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += 3;
            var end = raw.IndexOf("```", start, StringComparison.OrdinalIgnoreCase);
            if (end > start) return raw[start..end].Trim();
        }

        return raw.Trim();
    }
}