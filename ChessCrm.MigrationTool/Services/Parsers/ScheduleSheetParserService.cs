using ChessCrm.MigrationTool.Dtos;
using Serilog;

namespace ChessCrm.MigrationTool.Services.Parsers;

public class ScheduleSheetParserService
{
    // Пары колонок: { ИндексКолонкиID, ИндексКолонкиИмени }
    private readonly List<(int IdCol, int NameCol)> _dayColumns = new()
    {
        (0, 3),   // Понедельник
        (5, 8),   // Вторник
        (10, 13), // Среда
        (15, 18), // Четверг
        (20, 23), // Суббота
        (25, 28)  // Воскресенье
    };

    // Стартовые строки блоков (нумерация с 0, т.е. строка 6 = индекс 5)
    private readonly int[] _blockStartRows = { 5, 15, 25, 35 };

    public List<RawEnrollmentDto> Parse(IList<IList<object>> values, string sheetName)
    {
        var enrollments = new List<RawEnrollmentDto>();
        if (values == null) return enrollments;

        // 1. Проходим по каждому дню (блоку колонок)
        foreach (var (idCol, nameCol) in _dayColumns)
        {
            // 2. Проходим по каждому временному блоку (по вертикали)
            foreach (var startRow in _blockStartRows)
            {
                // Проверка: не вышли ли мы за пределы таблицы
                if (startRow >= values.Count) continue;

                var row = values[startRow];

                // Проверка: существует ли колонка с ID в этой строке
                if (row.Count <= idCol) continue;

                // 3. Пытаемся прочитать GroupId из заголовка блока
                var groupIdRaw = row[idCol]?.ToString()?.Trim();

                if (string.IsNullOrWhiteSpace(groupIdRaw) || !int.TryParse(groupIdRaw, out int groupId))
                {
                    // Если ID группы не указан — пропускаем весь блок
                    continue;
                }

                // 4. Читаем 8 строк с именами учеников внутри этого блока
                for (int i = 0; i < 8; i++)
                {
                    int currentRowIndex = startRow + i;
                    if (currentRowIndex >= values.Count) break;

                    var studentRow = values[currentRowIndex];
                    if (studentRow.Count <= nameCol) continue;

                    var studentName = studentRow[nameCol]?.ToString()?.Trim();

                    // Игнорируем пустые строки и цифры (нумерацию списка)
                    if (!string.IsNullOrWhiteSpace(studentName) && !int.TryParse(studentName, out _))
                    {
                        enrollments.Add(new RawEnrollmentDto
                        {
                            GroupId = groupId,
                            StudentName = studentName,
                            SheetName = sheetName,
                            RowIndex = currentRowIndex + 1
                        });
                    }
                }
            }
        }

        return enrollments;
    }
}