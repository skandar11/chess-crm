using ChessCrm.MigrationTool.Dtos;
using Serilog;

namespace ChessCrm.MigrationTool.Services.Parsers;

public class PaymentParserService
{
    private string GetValue(IList<object> row, int index)
    {
        return row.Count > index ? row[index]?.ToString()?.Trim() ?? string.Empty : string.Empty;
    }

    public List<RawPaymentDto> Parse(IList<IList<object>> values, string receiverName, string sheetName)
    {
        var payments = new List<RawPaymentDto>();
        if (values == null || values.Count == 0) return payments;

        // Извлекаем год из названия листа ("Февраль 2025" -> 2026, "Август 2025" -> 2025)
        int sheetYear = ExtractYearFromSheetName(sheetName);

        for (int i = 1; i < values.Count; i++)
        {
            var row = values[i];
            if (row.All(cell => string.IsNullOrWhiteSpace(cell?.ToString()))) continue;

            var paymentDto = new RawPaymentDto
            {
                SheetName = sheetName,
                RowIndex = i + 2,
                Date = GetValue(row, 0),
                SheetYear = sheetYear,
                ClientIdentifier = GetValue(row, 1),
                Amount = GetValue(row, 2),
                PaymentMethodHint = GetValue(row, 3),
                ReceiverName = receiverName
            };

            if (!string.IsNullOrWhiteSpace(paymentDto.Amount) &&
                !string.IsNullOrWhiteSpace(paymentDto.ClientIdentifier))
            {
                payments.Add(paymentDto);
            }
        }
        return payments;
    }

    private static int ExtractYearFromSheetName(string sheetName)
    {
        // Ищем 4-значное число в названии листа
        var match = System.Text.RegularExpressions.Regex.Match(sheetName, @"\d{4}");
        if (match.Success && int.TryParse(match.Value, out int year))
            return year;

        // Фолбэк: текущий год
        Log.Warning("Не удалось извлечь год из названия листа '{Sheet}', используем текущий год.", sheetName);
        return DateTime.Now.Year;
    }
}