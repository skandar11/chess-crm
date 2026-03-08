namespace ChessCrm.MigrationTool.Configuration;

public class AppConfig
{
    public string DbConnectionString { get; }
    public string GoogleCredentialsPath { get; }
    public string GoogleAppName { get; }

    // ID гугл таблиц
    public string SpreadsheetIdSchedule { get; }
    public string SpreadsheetIdPayments { get; }
    public string SpreadsheetIdAttendance { get; }

    public AppConfig()
    {
        // Читаем все переменные при старте
        DbConnectionString = GetEnvVariable("DB_CONNECTION_STRING");
        GoogleCredentialsPath = GetEnvVariable("GOOGLE_CREDENTIALS_PATH", "google-credentials.json");
        GoogleAppName = GetEnvVariable("GOOGLE_APPLICATION_NAME", "ChessCrmMigration");

        SpreadsheetIdSchedule = GetEnvVariable("GOOGLE_SPREADSHEET_ID_SCHEDULE");
        SpreadsheetIdPayments = GetEnvVariable("GOOGLE_SPREADSHEET_ID_PAYMENTS");
        SpreadsheetIdAttendance = GetEnvVariable("GOOGLE_SPREADSHEET_ID_ATTENDANCE");
    }

    private static string GetEnvVariable(string name, string? defaultValue = null)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (defaultValue != null)
        {
            return defaultValue;
        }

        // Если переменная обязательна и не найдена - падаем с ошибкой
        throw new InvalidOperationException($"Критическая ошибка: переменная окружения '{name}' не найдена в .env файле.");
    }
}