using ChessCrm.MigrationTool.Configuration;
using ChessCrm.MigrationTool.Database;
using ChessCrm.MigrationTool.Database.Seeders;
using ChessCrm.MigrationTool.Dtos;
using ChessCrm.MigrationTool.Services.Google;
using ChessCrm.MigrationTool.Services.Migration;
using ChessCrm.MigrationTool.Services.Parsers;
using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Spectre.Console;

Env.Load();
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.File("logs/migration.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

AnsiConsole.Write(new FigletText("ChessCRM").LeftJustified().Color(Color.Teal));

try
{
    var config = new AppConfig();
    AnsiConsole.MarkupLine("[green]Конфигурация загружена.[/]");

    var googleService = new GoogleSheetsService();
    AnsiConsole.MarkupLine("[green]Google Sheets API подключён.[/]");

    using var dbContext = new AppDbContext();

    AnsiConsole.MarkupLine("\n[yellow]Применяю миграции БД...[/]");
    await dbContext.Database.MigrateAsync();
    AnsiConsole.MarkupLine("[green]Миграции применены.[/]");

    // --- ШАГ 1: ГРУППЫ ---
    AnsiConsole.MarkupLine("\n[bold yellow]Шаг 1: Создание групп...[/]");
    var groupSeeder = new GroupSeeder(dbContext);
    await groupSeeder.SeedAsync();

    // --- ШАГ 2: КЛИЕНТЫ ---
    AnsiConsole.MarkupLine("\n[bold yellow]Шаг 2: Миграция клиентов...[/]");
    var rawData = await googleService.ReadSheetRangeAsync(config.SpreadsheetIdAttendance, "Ученики!A:I");
    AnsiConsole.MarkupLine($"[gray]Прочитано {rawData?.Count ?? 0} строк.[/]");
    var parsedClients = new ClientParserService().Parse(rawData);
    AnsiConsole.MarkupLine($"[gray]Распознано {parsedClients.Count} клиентов.[/]");
    await new ClientMigrationService(dbContext).MigrateAsync(parsedClients);

    // --- ШАГ 3: ПЛАТЕЖИ ---
    AnsiConsole.MarkupLine("\n[bold yellow]Шаг 3: Миграция платежей...[/]");
    var allPaymentSheets = await googleService.GetSheetTitlesAsync(config.SpreadsheetIdPayments);
    var monthRegex = new System.Text.RegularExpressions.Regex(
        @"^(Январь|Февраль|Март|Апрель|Май|Июнь|Июль|Август|Сентябрь|Октябрь|Ноябрь|Декабрь)\s*\d{4}$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    var targetSheets = allPaymentSheets.Where(t => monthRegex.IsMatch(t.Trim())).ToList();
    AnsiConsole.MarkupLine($"[gray]Найдено листов с платежами: {targetSheets.Count} ({string.Join(", ", targetSheets)})[/]");

    var paymentParser = new PaymentParserService();
    var allPayments = new List<RawPaymentDto>();

    foreach (var sheetName in targetSheets)
    {
        AnsiConsole.MarkupLine($"[gray]  Читаем лист: {sheetName}...[/]");
        var nursultanData = await googleService.ReadSheetRangeAsync(config.SpreadsheetIdPayments, $"'{sheetName}'!A3:D");
        var nikitaData = await googleService.ReadSheetRangeAsync(config.SpreadsheetIdPayments, $"'{sheetName}'!E3:H");
        allPayments.AddRange(paymentParser.Parse(nursultanData, "Нурсултан", sheetName));
        allPayments.AddRange(paymentParser.Parse(nikitaData, "Никита", sheetName));
    }

    AnsiConsole.MarkupLine($"[gray]Всего распознано платежей: {allPayments.Count}[/]");
    await new PaymentMigrationService(dbContext, googleService, config).MigrateAsync(allPayments);

    // --- ШАГ 4: РАСПИСАНИЕ (ученики в группы) ---
    AnsiConsole.MarkupLine("\n[bold yellow]Шаг 4: Запись учеников в группы...[/]");
    var scheduleSheetName = "Очные группы 25/26";
    var scheduleData = await googleService.ReadSheetRangeAsync(
        config.SpreadsheetIdSchedule, $"'{scheduleSheetName}'!A1:AC45");
    var enrollments = new ScheduleSheetParserService().Parse(scheduleData, scheduleSheetName);
    AnsiConsole.MarkupLine($"[gray]Найдено записей в ячейках: {enrollments.Count}[/]");
    await new EnrollmentMigrationService(dbContext).MigrateAsync(enrollments);

    // --- ШАГ 5: СИДЕР ATTENDANCE (тестовые занятия на март 2026) ---
    AnsiConsole.MarkupLine("\n[bold yellow]Шаг 5: Генерация занятий на март 2026...[/]");
    await new AttendanceSeeder(dbContext).SeedAsync();

    AnsiConsole.MarkupLine("\n[bold green]Миграция полностью завершена! База данных готова к работе.[/]");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Критическая ошибка");
    AnsiConsole.WriteException(ex);
}
finally
{
    await Log.CloseAndFlushAsync();
}