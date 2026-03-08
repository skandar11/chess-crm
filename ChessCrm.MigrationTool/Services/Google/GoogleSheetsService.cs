using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System.Text.RegularExpressions;

namespace ChessCrm.MigrationTool.Services.Google;

public class GoogleSheetsService
{
    private readonly SheetsService _sheetsService;

    public GoogleSheetsService()
    {
        var credentialPath = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_PATH")
                             ?? "google-credentials.json";

        var applicationName = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_NAME")
                              ?? "ChessCrmMigration";

        // Создаем учетные данные из файла
        GoogleCredential credential;
        using (var stream = new FileStream(credentialPath, FileMode.Open, FileAccess.Read))
        {
            credential = GoogleCredential.FromStream(stream)
                .CreateScoped(SheetsService.Scope.Spreadsheets);
        }

        // Инициализируем сервис
        _sheetsService = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = applicationName,
        });
    }

    /// <summary>
    /// Получает названия всех листов (вкладок) в указанной таблице.
    /// </summary>
    public async Task<List<string>> GetSheetTitlesAsync(string spreadsheetId)
    {
        var titles = new List<string>();

        // Запрашиваем у API метаданные таблицы
        var spreadsheet = await _sheetsService.Spreadsheets.Get(spreadsheetId).ExecuteAsync();

        foreach (var sheet in spreadsheet.Sheets)
        {
            titles.Add(sheet.Properties.Title);
        }

        return titles;
    }

    /// <summary>
    /// Читает данные из указанного диапазона ячеек.
    /// </summary>
    /// <param name="spreadsheetId">ID таблицы</param>
    /// <param name="range">Диапазон в формате "НазваниеЛиста!A1:Z100"</param>
    /// <returns>Массив строк, где каждая строка - это массив ячеек.</returns>
    public async Task<IList<IList<object>>> ReadSheetRangeAsync(string spreadsheetId, string range)
    {
        var request = _sheetsService.Spreadsheets.Values.Get(spreadsheetId, range);
        var response = await request.ExecuteAsync();
        return response.Values;
    }

    /// <summary>
    /// Добавляет строку в конец таблицы и возвращает номер добавленной строки.
    /// </summary>
    public async Task<int?> AppendRowAsync(string spreadsheetId, string range, IList<object> values)
    {
        var valueRange = new ValueRange { Values = new List<IList<object>> { values } };

        var request = _sheetsService.Spreadsheets.Values.Append(valueRange, spreadsheetId, range);
        // USERENTERED значит, что Гугл сам поймет, где числа, а где текст
        request.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

        var response = await request.ExecuteAsync();

        // Гугл возвращает UpdatedRange в формате "Ученики!A53:I53"
        // Нам нужно вытащить число 53
        var match = Regex.Match(response.Updates.UpdatedRange ?? "", @"[A-Z]+(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int rowIndex))
        {
            return rowIndex;
        }

        return null;
    }
}