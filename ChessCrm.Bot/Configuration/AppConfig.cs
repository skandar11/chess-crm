namespace ChessCrm.Bot.Configuration;

public class AppConfig
{
    public string BotToken { get; }
    public string DbConnectionString { get; }

    // Anthropic API — SQL generation + intent classification
    public string AnthropicApiKey { get; }

    // Google Sheets
    public string GoogleCredentialsPath { get; }
    public string? GoogleSpreadsheetIdCrm { get; }       // CRM таблица (Ученики, Абонементы, Инвайты)

    public AppConfig()
    {
        DbConnectionString = GetRequired("DB_CONNECTION_STRING");
        BotToken = GetRequired("TELEGRAM_BOT_TOKEN");
        AnthropicApiKey = GetRequired("ANTHROPIC_API_KEY");
        GoogleCredentialsPath = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_PATH") ?? "google-credentials.json";
        GoogleSpreadsheetIdCrm = Environment.GetEnvironmentVariable("GOOGLE_SPREADSHEET_ID_CRM");
    }

    private static string GetRequired(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        throw new InvalidOperationException($"Переменная окружения '{name}' не найдена в .env.");
    }
}
