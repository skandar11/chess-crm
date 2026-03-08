using ChessCrm.MigrationTool.Entities;
using ChessCrm.MigrationTool.Enums;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace ChessCrm.MigrationTool.Database.Seeders;

public class AttendanceSeeder(AppDbContext dbContext)
{
    // Планируем занятия на март 2026 — 8 штук на ученика в группе
    public async Task SeedAsync()
    {
        var alreadyExists = await dbContext.Attendances
            .AnyAsync(a => a.Date >= new DateOnly(2026, 3, 1) && a.Date <= new DateOnly(2026, 3, 31));

        if (alreadyExists)
        {
            AnsiConsole.MarkupLine("[yellow]Attendance за март 2026 уже есть, пропускаем.[/]");
            return;
        }

        // Загружаем все связки ученик-группа вместе с группой
        var groupStudents = await dbContext.GroupStudents
            .Include(gs => gs.Group)
            .Include(gs => gs.Client)
            .ToListAsync();

        var records = new List<Attendance>();

        foreach (var gs in groupStudents)
        {
            // Находим все даты занятий этой группы в марте 2026
            var lessonDates = GetLessonDatesInMonth(gs.Group.DayOfWeek, 2026, 3);

            // Берём не больше 8 дат (total_lessons из логики абонемента)
            foreach (var date in lessonDates.Take(8))
            {
                records.Add(new Attendance
                {
                    ClientId = gs.ClientId,
                    GroupId = gs.GroupId,
                    Date = date,
                    Status = AttendanceStatus.Scheduled
                });
            }
        }

        dbContext.Attendances.AddRange(records);
        await dbContext.SaveChangesAsync();

        AnsiConsole.MarkupLine($"[bold green]✓ Создано {records.Count} записей attendance за март 2026.[/]");
    }

    // Возвращает все даты указанного дня недели в заданном месяце
    private static List<DateOnly> GetLessonDatesInMonth(DayOfWeek dayOfWeek, int year, int month)
    {
        var dates = new List<DateOnly>();
        var daysInMonth = DateTime.DaysInMonth(year, month);

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(year, month, day);
            if (date.DayOfWeek == dayOfWeek)
                dates.Add(date);
        }

        return dates;
    }
}