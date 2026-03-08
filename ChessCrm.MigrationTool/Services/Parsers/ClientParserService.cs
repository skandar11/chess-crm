using ChessCrm.MigrationTool.Dtos;
using Serilog;

namespace ChessCrm.MigrationTool.Services.Parsers;

public class ClientParserService
{
    // Вспомогательный метод для безопасного получения значения из ячейки
    private string GetValue(IList<object> row, int index)
    {
        return row.Count > index ? row[index]?.ToString()?.Trim() ?? string.Empty : string.Empty;
    }

    public List<RawClientDto> Parse(IList<IList<object>> values)
    {
        var clients = new List<RawClientDto>();
        if (values == null || values.Count == 0)
        {
            Log.Warning("Данные для парсинга клиентов не найдены.");
            return clients;
        }

        // Пропускаем заголовок (первую строку)
        for (int i = 1; i < values.Count; i++)
        {
            var row = values[i];

            // Пропускаем пустые строки
            if (row.All(cell => string.IsNullOrWhiteSpace(cell?.ToString()))) continue;

            var clientDto = new RawClientDto
            {
                RowIndex = i + 1, // +1 т.к. индексация с 0
                Id = GetValue(row, 0),
                LastName = GetValue(row, 1),
                FirstName = GetValue(row, 2),
                MiddleName = GetValue(row, 3),
                BirthDate = GetValue(row, 4),
                Parent1 = GetValue(row, 5),
                Phone1 = GetValue(row, 6),
                Parent2 = GetValue(row, 7),
                Phone2 = GetValue(row, 8),
            };

            // Добавляем, только если есть хотя бы имя или фамилия
            if (!string.IsNullOrWhiteSpace(clientDto.FirstName) || !string.IsNullOrWhiteSpace(clientDto.LastName))
            {
                clients.Add(clientDto);
            }
        }

        return clients;
    }
}