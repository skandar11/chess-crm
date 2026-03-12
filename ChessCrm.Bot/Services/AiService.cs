using System.Text;
using System.Text.Json;
using ChessCrm.Bot.Configuration;
using ChessCrm.Bot.Prompts;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Services;

/// <summary>
/// Handles all AI calls: SQL generation and response formatting via Claude API.
/// </summary>
public class AiService(AppConfig config, HttpClient httpClient, ILogger<AiService> logger)
{
    private const string ClaudeApiUrl = "https://api.anthropic.com/v1/messages";
    internal const string DefaultModel = "claude-haiku-4-5-20251001";
    private const string SqlModel     = DefaultModel;
    private const string FormatModel  = DefaultModel;

    // Generate SQL from natural language question
    public async Task<string> GenerateSqlAsync(string userQuestion, CancellationToken ct)
    {
        var raw = await CallClaudeAsync(
            model: SqlModel,
            systemPrompt: ChessPrompts.GetSqlSystemPrompt(),
            userMessage: ChessPrompts.GetSqlUserPrompt(userQuestion),
            maxTokens: 1024,
            ct: ct
        );

        return ExtractSql(raw);
    }

    // Format raw JSON data into a human-readable Telegram message
    public async Task<string> AnalyzeDataAsync(string userQuestion, string jsonData, CancellationToken ct)
    {
        return await CallClaudeAsync(
            model: FormatModel,
            systemPrompt: ChessPrompts.GetAnalysisSystemPrompt(),
            userMessage: ChessPrompts.GetAnalysisUserPrompt(userQuestion, jsonData),
            maxTokens: 1024,
            ct: ct
        );
    }

    // Fix broken SQL using the PostgreSQL error message
    public async Task<string> FixSqlAsync(string originalQuestion, string brokenSql, string pgError, CancellationToken ct)
    {
        var message = $"""
            You wrote an SQL query that returned a PostgreSQL error.

            Original question: {originalQuestion}

            Your SQL:
            {brokenSql}

            PostgreSQL error:
            {pgError}

            Fix the SQL. Rules:
            - JOIN groups g ON g.id = a.group_id is REQUIRED when using g.start_time
            - date() and time() functions do not exist in PostgreSQL — use ::date and ::time casting
            - Return only the fixed SQL, no explanations
            """;

        var raw = await CallClaudeAsync(
            model: SqlModel,
            systemPrompt: ChessPrompts.GetSqlSystemPrompt(),
            userMessage: message,
            maxTokens: 1024,
            ct: ct
        );

        return ExtractSql(raw);
    }

    // Format parent data into a warm, readable Telegram message
    public async Task<string> FormatParentResponseAsync(string dataType, string rawData, CancellationToken ct)
    {
        return await CallClaudeAsync(
            model: FormatModel,
            systemPrompt: ChessPrompts.GetParentResponsePrompt(),
            userMessage: ChessPrompts.GetParentResponseUserPrompt(dataType, rawData),
            maxTokens: 1024,
            ct: ct
        );
    }

    // Core Claude API call
    private async Task<string> CallClaudeAsync(
        string model, string systemPrompt, string userMessage,
        int maxTokens, CancellationToken ct)
    {
        var payload = new
        {
            model,
            max_tokens = maxTokens,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userMessage }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, ClaudeApiUrl);
        request.Headers.Add("x-api-key", config.AnthropicApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        logger.LogDebug("→ Claude API [{Model}]: {Message}", model, userMessage[..Math.Min(80, userMessage.Length)]);

        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var result = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;

        return result.Trim();
    }

    // Strip ```sql ... ``` markdown fences if model wrapped the output
    private static string ExtractSql(string raw)
    {
        var start = raw.IndexOf("```sql", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start += 6;
            var end = raw.IndexOf("```", start, StringComparison.OrdinalIgnoreCase);
            if (end > start) return raw[start..end].Trim();
        }

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
