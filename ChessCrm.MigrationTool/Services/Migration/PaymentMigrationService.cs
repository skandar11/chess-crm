using ChessCrm.MigrationTool.Configuration;
using ChessCrm.MigrationTool.Database;
using ChessCrm.MigrationTool.Dtos;
using ChessCrm.MigrationTool.Entities;
using ChessCrm.MigrationTool.Enums;
using ChessCrm.MigrationTool.Services.Google;
using FuzzySharp;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Spectre.Console;

namespace ChessCrm.MigrationTool.Services.Migration;

public class PaymentMigrationService
{
    private readonly AppDbContext _dbContext;
    private readonly GoogleSheetsService _googleService;
    private readonly AppConfig _config;

    public PaymentMigrationService(AppDbContext dbContext, GoogleSheetsService googleService, AppConfig config)
    {
        _dbContext = dbContext;
        _googleService = googleService;
        _config = config;
    }

    public async Task MigrateAsync(List<RawPaymentDto> paymentsDto)
    {
        var allClients = await _dbContext.Clients.ToListAsync();
        var newPayments = new List<Payment>();

        foreach (var dto in paymentsDto)
        {
            var payment = new Payment
            {
                ReceiverName = dto.ReceiverName,
                GoogleSheetRowIndex = dto.RowIndex,
                Method = !string.IsNullOrWhiteSpace(dto.PaymentMethodHint) ? PaymentMethod.Card : PaymentMethod.Cash,
                CardLastDigits = string.IsNullOrWhiteSpace(dto.PaymentMethodHint) ? null : dto.PaymentMethodHint
            };

            // Убираем пробелы и заменяем запятую на точку
            var amountRaw = dto.Amount.Replace(" ", "").Replace(",", ".");
            if (decimal.TryParse(amountRaw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var amount))
                payment.Amount = amount;
            else continue;

            // 1. Обработка пустой даты
            if (string.IsNullOrWhiteSpace(dto.Date))
            {
                payment.PaymentDate = null;
            }
            else
            {
                // Формат "01.02" — день.месяц без года, год берём из названия листа
                if (DateTime.TryParseExact(
                        $"{dto.Date}.{dto.SheetYear}",
                        "dd.MM.yyyy",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out var date))
                {
                    payment.PaymentDate = DateTime.SpecifyKind(date, DateTimeKind.Utc);
                }
                else if (DateTime.TryParse(dto.Date, out var fallbackDate))
                {
                    // Фолбэк для полных дат типа "01.02.2025"
                    payment.PaymentDate = DateTime.SpecifyKind(fallbackDate, DateTimeKind.Utc);
                }
                else
                {
                    Log.Warning("Не удалось распарсить дату '{Date}' на листе '{Sheet}'", dto.Date, dto.SheetName);
                    payment.PaymentDate = null;
                }
            }

            var identifier = dto.ClientIdentifier.Trim();
            var normalizedIdentifier = identifier.ToLower().Replace(" ", "");

            if (identifier.Contains("турнир", StringComparison.OrdinalIgnoreCase) || identifier.Any(char.IsDigit))
            {
                payment.Comment = identifier;
                payment.ClientId = null;
            }
            else
            {
                var scoredClients = allClients.Select(c => new
                {
                    Client = c,
                    FullName = $"{c.LastName} {c.FirstName}".Trim(),
                    Score = Fuzz.PartialRatio(normalizedIdentifier, $"{c.LastName}{c.FirstName}".ToLower())
                })
                .OrderByDescending(x => x.Score)
                .ToList();

                var bestMatch = scoredClients.FirstOrDefault();

                if (bestMatch != null && bestMatch.Score >= 80)
                {
                    payment.ClientId = bestMatch.Client.Id;
                    payment.Comment = $"Оплата от '{identifier}' (авто-привязка)";
                    AnsiConsole.MarkupLine($"[green]Авто-привязка:[/] '{identifier}' -> {bestMatch.FullName} ({bestMatch.Score}%)");
                }
                else
                {
                    AnsiConsole.MarkupLine($"\n[bold yellow]! Не найден клиент:[/] Лист: [magenta]{dto.SheetName}[/], Строка:[magenta]{dto.RowIndex}[/] | Дата: {dto.Date} | Имя: [cyan]'{identifier}'[/] | Сумма: {dto.Amount} руб.");

                    var candidates = scoredClients.Take(5).ToList();
                    var prompt = new SelectionPrompt<string>()
                        .Title("Выберите действие:")
                        .PageSize(10)
                        .AddChoices(candidates.Select(c => $"{c.FullName} (Совпадение: {c.Score}%)"))
                        .AddChoices(
                            "[green]+ Добавить нового клиента (в БД и Гугл)[/]",
                            "[red]⨉ Никто из списка (оставить как комментарий)[/]"
                        );

                    var choice = AnsiConsole.Prompt(prompt);

                    if (choice == "[red]⨉ Никто из списка (оставить как комментарий)[/]")
                    {
                        payment.ClientId = null;
                        payment.Comment = $"Оплата от '{identifier}' (клиент не выбран)";
                        AnsiConsole.MarkupLine("[gray]Пропущено.[/]");
                    }
                    else if (choice == "[green]+ Добавить нового клиента (в БД и Гугл)[/]")
                    {
                        var newClient = new Client { LastName = identifier, Status = ClientStatus.Active };
                        _dbContext.Clients.Add(newClient);
                        await _dbContext.SaveChangesAsync();

                        var rowValues = new List<object> { newClient.Id.ToString(), identifier };
                        var rowIndex = await _googleService.AppendRowAsync(_config.SpreadsheetIdAttendance, "Ученики!A:B", rowValues);

                        newClient.GoogleSheetRowIndex = rowIndex;
                        await _dbContext.SaveChangesAsync();

                        allClients.Add(newClient);

                        payment.ClientId = newClient.Id;
                        payment.Comment = $"Оплата от '{identifier}' (создан новый клиент)";
                        AnsiConsole.MarkupLine($"[bold green]Создан новый клиент:[/] {identifier} (ID: {newClient.Id}, Строка: {rowIndex})");
                    }
                    else
                    {
                        var selectedCandidate = candidates.First(c => $"{c.FullName} (Совпадение: {c.Score}%)" == choice);
                        payment.ClientId = selectedCandidate.Client.Id;
                        payment.Comment = $"Оплата от '{identifier}' (ручная привязка)";
                        AnsiConsole.MarkupLine($"[green]Привязано к:[/] {selectedCandidate.FullName}");
                    }
                }
            }

            // --- MVP: АВТО-СОЗДАНИЕ АБОНЕМЕНТА ---
            // Если платеж привязан к клиенту, генерируем ему абонемент на 1 месяц
            if (payment.ClientId.HasValue)
            {
                var startDate = payment.PaymentDate.HasValue
                    ? DateOnly.FromDateTime(payment.PaymentDate.Value.ToLocalTime())
                    : DateOnly.FromDateTime(DateTime.UtcNow);

                payment.Subscription = new Subscription
                {
                    ClientId = payment.ClientId.Value,
                    Name = "Базовый (Авто-генерация)",
                    TotalLessons = 8, // Дефолтное значение для MVP
                    LessonsLeft = 8,
                    StartDate = startDate,
                    EndDate = startDate.AddMonths(1),
                    Price = payment.Amount,
                    IsActive = true
                };
            }

            newPayments.Add(payment);
        }

        if (newPayments.Any())
        {
            _dbContext.Payments.AddRange(newPayments);
            await _dbContext.SaveChangesAsync();
            Log.Information("Успешно сохранено {Count} новых платежей и сгенерированы абонементы.", newPayments.Count);
        }
    }
}