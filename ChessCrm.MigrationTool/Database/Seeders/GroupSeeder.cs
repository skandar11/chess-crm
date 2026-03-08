using ChessCrm.MigrationTool.Entities;
using ChessCrm.MigrationTool.Enums;
using Microsoft.EntityFrameworkCore;
using Spectre.Console;

namespace ChessCrm.MigrationTool.Database.Seeders;

public class GroupSeeder
{
    private readonly AppDbContext _dbContext;
    public GroupSeeder(AppDbContext dbContext) => _dbContext = dbContext;

    public async Task SeedAsync()
    {
        if (await _dbContext.Groups.AnyAsync())
        {
            AnsiConsole.MarkupLine("[gray]Группы уже существуют. Пропускаем сидирование.[/]");
            return;
        }

        AnsiConsole.MarkupLine("[yellow]Заполняю базу данных группами из расписания...[/]");

        var offlineStart = new DateOnly(2026, 3, 1);
        var onlineStart = new DateOnly(2025, 9, 1);

        var groups = new List<Group>();

        // --- ПОНЕДЕЛЬНИК (Очно) ---
        groups.Add(Create("Старшая Пн 16:30", GroupLevel.Senior, TrainingFormat.Offline, DayOfWeek.Monday, "16:30", "Квитко Н.К.", offlineStart));
        groups.Add(Create("Средняя Пн 17:30", GroupLevel.Middle, TrainingFormat.Offline, DayOfWeek.Monday, "17:30", "Квитко Н.К.", offlineStart));
        groups.Add(Create("Младшая Пн 18:45", GroupLevel.Junior, TrainingFormat.Offline, DayOfWeek.Monday, "18:45", "Квитко Н.К.", offlineStart));

        // --- ВТОРНИК (Очно) ---
        groups.Add(Create("Новички Вт 16:30", GroupLevel.Beginner, TrainingFormat.Offline, DayOfWeek.Tuesday, "16:30", "Бурцев И.Л.", offlineStart));
        groups.Add(Create("Средняя Вт 17:30", GroupLevel.Middle, TrainingFormat.Offline, DayOfWeek.Tuesday, "17:30", "Бурцев И.Л.", offlineStart));
        groups.Add(Create("Младшая Вт 18:45", GroupLevel.Junior, TrainingFormat.Offline, DayOfWeek.Tuesday, "18:45", "Бурцев И.Л.", offlineStart));

        // --- СРЕДА (Очно) ---
        groups.Add(Create("Старшая Ср 16:30", GroupLevel.Senior, TrainingFormat.Offline, DayOfWeek.Wednesday, "16:30", "Квитко Н.К.", offlineStart));
        groups.Add(Create("Средняя Ср 17:30", GroupLevel.Middle, TrainingFormat.Offline, DayOfWeek.Wednesday, "17:30", "Квитко Н.К.", offlineStart));
        groups.Add(Create("Новички Ср 18:45", GroupLevel.Beginner, TrainingFormat.Offline, DayOfWeek.Wednesday, "18:45", "Квитко Н.К.", offlineStart));

        // --- ЧЕТВЕРГ (Очно) ---
        groups.Add(Create("Старшая Чт 16:30", GroupLevel.Senior, TrainingFormat.Offline, DayOfWeek.Thursday, "16:30", "Бурцев И.Л.", offlineStart));
        groups.Add(Create("Средняя Чт 17:30", GroupLevel.Middle, TrainingFormat.Offline, DayOfWeek.Thursday, "17:30", "Бурцев И.Л.", offlineStart));
        groups.Add(Create("Младшая Чт 18:45", GroupLevel.Junior, TrainingFormat.Offline, DayOfWeek.Thursday, "18:45", "Бурцев И.Л.", offlineStart));

        // --- СУББОТА (Очно) ---
        groups.Add(Create("Младшая Сб 10:00", GroupLevel.Junior, TrainingFormat.Offline, DayOfWeek.Saturday, "10:00", "Бурцев И.Л.", offlineStart));
        groups.Add(Create("Средняя Сб 11:00", GroupLevel.Middle, TrainingFormat.Offline, DayOfWeek.Saturday, "11:00", "Бурцев И.Л.", offlineStart));
        groups.Add(Create("Старшая Сб 12:00", GroupLevel.Senior, TrainingFormat.Offline, DayOfWeek.Saturday, "12:00", "Бурцев И.Л.", offlineStart));
        groups.Add(Create("Новички Сб 13:00", GroupLevel.Beginner, TrainingFormat.Offline, DayOfWeek.Saturday, "13:00", "Бурцев И.Л.", offlineStart));

        // --- ВОСКРЕСЕНЬЕ (Очно) ---
        groups.Add(Create("Младшая Вс 10:00", GroupLevel.Junior, TrainingFormat.Offline, DayOfWeek.Sunday, "10:00", "Бурцев И.Л.", offlineStart));
        groups.Add(Create("Средняя Вс 11:00", GroupLevel.Middle, TrainingFormat.Offline, DayOfWeek.Sunday, "11:00", "Бурцев И.Л.", offlineStart));
        groups.Add(Create("Старшая Вс 12:00", GroupLevel.Senior, TrainingFormat.Offline, DayOfWeek.Sunday, "12:00", "Бурцев И.Л.", offlineStart));

        // --- ONLINE (Вторник) ---
        groups.Add(Create("Online Старшая Вт 16:30", GroupLevel.Senior, TrainingFormat.Online, DayOfWeek.Tuesday, "16:30", "Квитко Н.К.", onlineStart));
        groups.Add(Create("Online Средняя Вт 17:30", GroupLevel.Middle, TrainingFormat.Online, DayOfWeek.Tuesday, "17:30", "Квитко Н.К.", onlineStart));

        // --- ONLINE (Четверг) ---
        groups.Add(Create("Online Средняя Чт 16:00", GroupLevel.Middle, TrainingFormat.Online, DayOfWeek.Thursday, "16:00", "Квитко Н.К.", onlineStart));
        groups.Add(Create("Online Старшая Чт 16:45", GroupLevel.Senior, TrainingFormat.Online, DayOfWeek.Thursday, "16:45", "Квитко Н.К.", onlineStart));

        _dbContext.Groups.AddRange(groups);
        await _dbContext.SaveChangesAsync();

        AnsiConsole.MarkupLine($"[bold green]✓ Успешно создано {groups.Count} групп![/]");
    }

    private Group Create(string name, GroupLevel level, TrainingFormat format, DayOfWeek day, string start, string coach, DateOnly effectiveFrom)
    {
        var startTime = TimeSpan.Parse(start);
        return new Group
        {
            Name = name,
            Level = level,
            Format = format,
            CoachName = coach,
            DayOfWeek = day,
            StartTime = startTime,
            EndTime = startTime.Add(TimeSpan.FromHours(1)),
            EffectiveFrom = effectiveFrom,
            IsActive = true,
            MaxStudents = 10
        };
    }
}