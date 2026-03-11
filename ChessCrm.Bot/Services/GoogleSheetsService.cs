using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using ChessCrm.Bot.Configuration;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Services;

/// <summary>
/// Handles writing to Google Sheets (CRM table chess_crm_v2).
/// Листы: Ученики, Абонементы.
/// </summary>
public class GoogleSheetsService(AppConfig config, ILogger<GoogleSheetsService> logger)
{
    private const string SHEET_STUDENTS       = "Ученики";
    private const string SHEET_SUBSCRIPTIONS  = "Абонементы";
    private const string SHEET_INVITES        = "Инвайты";

    private SheetsService CreateService()
    {
        GoogleCredential credential;
        using var stream = new FileStream(config.GoogleCredentialsPath, FileMode.Open, FileAccess.Read);
        credential = GoogleCredential.FromStream(stream)
            .CreateScoped(SheetsService.Scope.Spreadsheets);

        return new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ChessCrmBot"
        });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ЛИСТ: УЧЕНИКИ
    // Колонки: A=ID, B=ФИО, C=ДР, D=Уровень, E=Родитель, F=Телефон,
    //          G=Статус, H=Дата добавления
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<bool> WriteNewClientToJournalAsync(
        int clientId, string fullName, string? birthDate,
        string? level, string? parentName, string? parentPhone)
    {
        var spreadsheetId = config.GoogleSpreadsheetIdCrm;
        if (string.IsNullOrWhiteSpace(spreadsheetId))
        {
            logger.LogWarning("GOOGLE_SPREADSHEET_ID_CRM не задан — запись в Sheets пропущена");
            return false;
        }

        var service = CreateService();

        var row = new List<object>
        {
            clientId,                                       // A: ID
            fullName,                                       // B: ФИО
            birthDate ?? "",                                // C: Дата рождения
            level ?? "",                                    // D: Уровень
            parentName ?? "",                               // E: Родитель
            parentPhone ?? "",                              // F: Телефон
            "активен",                                     // G: Статус
            DateTime.Now.ToString("dd.MM.yyyy"),            // H: Дата добавления
        };

        var valueRange = new ValueRange { Values = [row] };
        var request = service.Spreadsheets.Values
            .Append(valueRange, spreadsheetId, $"{SHEET_STUDENTS}!A:H");
        request.ValueInputOption =
            SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        await request.ExecuteAsync();

        logger.LogInformation("Sheets [Ученики]: записан {FullName} (id={Id})", fullName, clientId);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ЛИСТ: АБОНЕМЕНТЫ
    // Колонки: A=ID, B=ФИО, C=Месяц, D=Получатель, E=Сумма, F=Дата продажи
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<bool> WriteSubscriptionAsync(
        int subscriptionId, string fullName, string monthName,
        string? recipient, decimal price, DateOnly saleDate)
    {
        var spreadsheetId = config.GoogleSpreadsheetIdCrm;
        if (string.IsNullOrWhiteSpace(spreadsheetId))
        {
            logger.LogWarning("GOOGLE_SPREADSHEET_ID_CRM не задан — запись абонемента пропущена");
            return false;
        }

        var service = CreateService();

        var row = new List<object>
        {
            subscriptionId,                                 // A: ID
            fullName,                                       // B: ФИО
            monthName,                                      // C: Месяц абонемента
            recipient ?? "",                                // D: Получатель
            price,                                          // E: Сумма
            saleDate.ToString("dd.MM.yyyy")                 // F: Дата продажи
        };

        var valueRange = new ValueRange { Values = [row] };
        var request = service.Spreadsheets.Values
            .Append(valueRange, spreadsheetId, $"{SHEET_SUBSCRIPTIONS}!A:F");
        request.ValueInputOption =
            SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        await request.ExecuteAsync();

        logger.LogInformation("Sheets [Абонементы]: записан {FullName} — {Month}, {Price}",
            fullName, monthName, price);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ЛИСТ: ИНВАЙТЫ
    // Колонки: A=ClientID, B=ФИО, C=Token, D=DeepLink, E=ExpiresAt
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<bool> WriteInviteAsync(
        int clientId, string clientFullName, string token,
        string deepLink, DateTimeOffset expiresAt)
    {
        var spreadsheetId = config.GoogleSpreadsheetIdCrm;
        if (string.IsNullOrWhiteSpace(spreadsheetId))
        {
            logger.LogWarning("GOOGLE_SPREADSHEET_ID_CRM не задан — запись инвайта пропущена");
            return false;
        }

        var service = CreateService();

        var row = new List<object>
        {
            clientId,                                           // A: ClientID
            clientFullName,                                     // B: ФИО
            token,                                              // C: Token
            deepLink,                                           // D: DeepLink
            expiresAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm") // E: ExpiresAt
        };

        var valueRange = new ValueRange { Values = [row] };
        var request = service.Spreadsheets.Values
            .Append(valueRange, spreadsheetId, $"{SHEET_INVITES}!A:E");
        request.ValueInputOption =
            SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        await request.ExecuteAsync();

        logger.LogInformation("Sheets [Инвайты]: записан {FullName} (id={Id})", clientFullName, clientId);
        return true;
    }
}
