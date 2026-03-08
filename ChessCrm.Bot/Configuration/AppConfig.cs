namespace ChessCrm.Bot.Configuration;

public class AppConfig
{
    public string BotToken { get; }
    public string DbConnectionString { get; }

    // Тяжёлая модель: генерация SQL
    public string SqlModelApiUrl { get; }
    public string SqlModelName { get; }

    // Быстрая модель: форматирование ответа
    public string ChatModelApiUrl { get; }
    public string ChatModelName { get; }

    // Белый список Telegram user id (через запятую), пусто = без ограничений
    public HashSet<long> AllowedUserIds { get; }

    public AppConfig()
    {
        DbConnectionString = GetRequired("DB_CONNECTION_STRING");
        BotToken = GetRequired("TELEGRAM_BOT_TOKEN");
        SqlModelApiUrl = GetRequired("SQL_MODEL_API_URL");
        SqlModelName = GetRequired("SQL_MODEL_NAME");
        ChatModelApiUrl = GetRequired("CHAT_MODEL_API_URL");
        ChatModelName = GetRequired("CHAT_MODEL_NAME");

        var allowed = Environment.GetEnvironmentVariable("ALLOWED_TELEGRAM_USER_IDS") ?? "";
        AllowedUserIds = allowed
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => long.TryParse(s.Trim(), out var id) ? id : 0)
            .Where(id => id != 0)
            .ToHashSet();
    }

    private static string GetRequired(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        throw new InvalidOperationException($"Переменная окружения '{name}' не найдена в .env.");
    }
}