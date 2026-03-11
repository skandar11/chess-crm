using System.Text;
using System.Text.Json;
using ChessCrm.Bot.Configuration;
using ChessCrm.Bot.Prompts;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Services;

/// <summary>
/// Классифицирует намерение менеджера через Claude API.
/// Возвращает тип намерения и извлечённые параметры.
/// </summary>
public class IntentService(AppConfig config, HttpClient httpClient, ILogger<IntentService> logger)
{
    public async Task<IntentResult> ClassifyAsync(string userMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.AnthropicApiKey))
        {
            logger.LogWarning("ANTHROPIC_API_KEY не задан — все запросы трактуются как analytics");
            return new IntentResult("analytics", new Dictionary<string, string?>());
        }

        var payload = new
        {
            model = "claude-haiku-4-5",
            max_tokens = 512,
            system = IntentPrompts.GetIntentSystemPrompt(),
            messages = new[]
            {
                new { role = "user", content = IntentPrompts.GetIntentUserPrompt(userMessage) }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", config.AnthropicApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        logger.LogDebug("→ IntentService: классифицируем '{Message}'", userMessage);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка обращения к Claude API для классификации намерения");
            return new IntentResult("analytics", new Dictionary<string, string?>());
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var rawJson = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "{}";

        logger.LogDebug("← IntentService ответ: {Json}", rawJson);

        return ParseIntentResult(rawJson);
    }

    /// <summary>
    /// Извлекает дополнительные данные для уже существующего черновика.
    /// Знает контекст (интент + текущие парамы) — не путает имя родителя с именем ученика.
    /// </summary>
    public async Task<Dictionary<string, string?>> ClassifyAdditionalDataAsync(
        string userMessage,
        string intent,
        Dictionary<string, string?> currentParams,
        CancellationToken ct)
    {
        var currentParamsJson = System.Text.Json.JsonSerializer.Serialize(currentParams,
            new System.Text.Json.JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

        var payload = new
        {
            model = "claude-haiku-4-5",
            max_tokens = 512,
            system = IntentPrompts.GetAdditionalDataSystemPrompt(intent, currentParamsJson),
            messages = new[]
            {
                new { role = "user", content = IntentPrompts.GetAdditionalDataUserPrompt(userMessage) }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", config.AnthropicApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );

        logger.LogDebug("→ ClassifyAdditionalData: '{Message}'", userMessage);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка ClassifyAdditionalData");
            return new Dictionary<string, string?>();
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var rawJson = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "{}";

        logger.LogDebug("← ClassifyAdditionalData: {Json}", rawJson);

        return ParseAdditionalDataResult(rawJson);
    }

    private Dictionary<string, string?> ParseAdditionalDataResult(string rawJson)
    {
        try
        {
            var json = rawJson.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('\n');
                var end   = json.LastIndexOf("```");
                json = (start >= 0 && end > start)
                    ? json[(start + 1)..end].Trim()
                    : json.Trim('`').Trim();
            }

            using var doc = JsonDocument.Parse(json);
            var result = new Dictionary<string, string?>();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()
                    : prop.Value.ToString();
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось распарсить ClassifyAdditionalData: {Raw}", rawJson);
            return new Dictionary<string, string?>();
        }
    }

    private IntentResult ParseIntentResult(string rawJson)
    {
        try
        {
            // Strip markdown code fences if model wrapped the response
            var json = rawJson.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('\n');
                var end   = json.LastIndexOf("```");
                json = (start >= 0 && end > start)
                    ? json[(start + 1)..end].Trim()
                    : json.Trim('`').Trim();
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var intent = root.TryGetProperty("intent", out var intentEl)
                ? intentEl.GetString() ?? "unknown"
                : "unknown";

            var paramsDict = new Dictionary<string, string?>();
            if (root.TryGetProperty("params", out var paramsEl) && paramsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in paramsEl.EnumerateObject())
                {
                    paramsDict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
                }
            }

            return new IntentResult(intent, paramsDict);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось распарсить ответ IntentService: {Raw}", rawJson);
            return new IntentResult("analytics", new Dictionary<string, string?>());
        }
    }
}

/// <summary>Результат классификации намерения.</summary>
/// <param name="Intent">Тип: analytics | sell_subscription | new_student | add_to_group | edit_student | remove_from_group | transfer_group | unknown</param>
/// <param name="Params">Извлечённые параметры.</param>
public record IntentResult(string Intent, Dictionary<string, string?> Params);
