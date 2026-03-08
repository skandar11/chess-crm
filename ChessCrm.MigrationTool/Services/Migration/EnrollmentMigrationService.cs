using ChessCrm.MigrationTool.Database;
using ChessCrm.MigrationTool.Dtos;
using ChessCrm.MigrationTool.Entities;
using FuzzySharp;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace ChessCrm.MigrationTool.Services.Migration;

public class EnrollmentMigrationService
{
    private readonly AppDbContext _dbContext;

    public EnrollmentMigrationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task MigrateAsync(List<RawEnrollmentDto> enrollments)
    {
        var clients = await _dbContext.Clients.ToListAsync();
        var groups = await _dbContext.Groups.ToDictionaryAsync(g => g.Id); // Кэшируем группы в словарь для скорости

        var newLinks = new List<GroupStudent>();
        var existingLinks = await _dbContext.GroupStudents.ToListAsync(); // Чтобы не дублировать

        foreach (var dto in enrollments)
        {
            // 1. Ищем группу по ID (теперь это мгновенно и точно)
            if (!groups.TryGetValue(dto.GroupId, out var group))
            {
                AnsiConsole.MarkupLine($"[red]Ошибка:[/] Группа с ID={dto.GroupId} не найдена в БД! (Строка {dto.RowIndex}, Ученик: {dto.StudentName})");
                continue;
            }

            // 2. Ищем ученика (тут всё по-старому: Fuzzy Search)
            var nameToSearch = dto.StudentName.ToLower().Replace(".", "").Trim();

            // Ищем точное совпадение начала (для "Синявский И")
            var candidates = clients.Where(c =>
                $"{c.LastName} {c.FirstName}".ToLower().StartsWith(nameToSearch) ||
                $"{c.LastName} {c.FirstName}".ToLower().Replace("ё", "е") == nameToSearch.Replace("ё", "е")
            ).ToList();

            Client? selectedClient = null;

            if (candidates.Count == 1)
            {
                selectedClient = candidates.First();
                AnsiConsole.MarkupLine($"[green]Найдено:[/] '{dto.StudentName}' -> {selectedClient.LastName} {selectedClient.FirstName} (в группу {group.Name})");
            }
            else
            {
                // Если точного нет или их много - включаем нечеткий поиск
                var fuzzyCandidates = clients.Select(c => new
                {
                    Client = c,
                    Score = Fuzz.PartialRatio(nameToSearch, $"{c.LastName} {c.FirstName}".ToLower())
                })
                .Where(x => x.Score > 60)
                .OrderByDescending(x => x.Score)
                .Take(5)
                .Select(x => x.Client)
                .ToList();

                if (fuzzyCandidates.Any())
                {
                    selectedClient = AskUser(dto, fuzzyCandidates, group.Name);
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Ученик не найден:[/] {dto.StudentName} (строка {dto.RowIndex})");
                }
            }

            // 3. Сохраняем связь, если её еще нет
            if (selectedClient != null)
            {
                bool alreadyExists = existingLinks.Any(l => l.GroupId == group.Id && l.ClientId == selectedClient.Id) ||
                                     newLinks.Any(l => l.GroupId == group.Id && l.ClientId == selectedClient.Id);

                if (!alreadyExists)
                {
                    newLinks.Add(new GroupStudent { GroupId = group.Id, ClientId = selectedClient.Id });
                }
            }
        }

        if (newLinks.Any())
        {
            _dbContext.GroupStudents.AddRange(newLinks);
            await _dbContext.SaveChangesAsync();
            AnsiConsole.MarkupLine($"[bold green]Успешно записано {newLinks.Count} учеников в группы![/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Новых записей в группы не найдено.[/]");
        }
    }

    private Client? AskUser(RawEnrollmentDto dto, List<Client> candidates, string groupName)
    {
        // Создаем список вариантов: сначала кандидаты, потом null (пропуск)
        var choices = candidates.Cast<Client?>().ToList();
        choices.Add(null);

        var prompt = new SelectionPrompt<Client?>()
            .Title($"Кто такой [yellow]{dto.StudentName}[/] в группе [cyan]{groupName} (ID:{dto.GroupId})[/]?")
            .PageSize(10)
            .AddChoices(choices)
            // Явно указываем тип Client? в лямбде
            .UseConverter((Client? c) =>
            {
                if (c == null) return "[red]Пропустить (нет в списке)[/]";
                return $"{c.LastName} {c.FirstName} {c.MiddleName}";
            });

        return AnsiConsole.Prompt(prompt);
    }
}