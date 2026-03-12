using ChessCrm.Bot.Services;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Handlers;

/// <summary>
/// Обрабатывает команду "оформить абонемент".
/// Пример: "Айгерим оплатила 15000 за апрель Никите"
/// Создаёт запись в subscriptions + Google Sheets "Абонементы".
/// </summary>
public class SubscriptionHandler(
    DatabaseService dbService,
    GoogleSheetsService sheetsService,
    ILogger<SubscriptionHandler> logger)
{
    private static readonly Dictionary<string, int> RussianMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["январь"]  = 1,  ["января"]  = 1,
        ["февраль"] = 2,  ["февраля"] = 2,
        ["март"]    = 3,  ["марта"]   = 3,
        ["апрель"]  = 4,  ["апреля"]  = 4,
        ["май"]     = 5,  ["мая"]     = 5,
        ["июнь"]    = 6,  ["июня"]    = 6,
        ["июль"]    = 7,  ["июля"]    = 7,
        ["август"]  = 8,  ["августа"] = 8,
        ["сентябрь"]= 9,  ["сентября"]= 9,
        ["октябрь"] = 10, ["октября"] = 10,
        ["ноябрь"]  = 11, ["ноября"]  = 11,
        ["декабрь"] = 12, ["декабря"] = 12,
    };

    /// <summary>
    /// Called from TelegramBotService after preview: client and period already resolved.
    /// Skips steps 1-4 (find client, parse period) — starts with duplicate check (race condition) and INSERT.
    /// </summary>
    public async Task<HandlerResult> HandleAsync(
        int clientId, string clientFullName,
        DateOnly monthDate, decimal amount, string? recipient,
        CancellationToken ct)
    {
        // ── Duplicate check (race condition guard) ──────────────────────
        if (await dbService.HasSubscriptionForMonthAsync(clientId, monthDate, ct))
        {
            var monthName = GetRussianMonthName(monthDate.Month);
            return new HandlerResult(false,
                $"⚠️ У <b>{clientFullName}</b> уже есть абонемент за <b>{monthName} {monthDate.Year}</b>.");
        }

        // ── INSERT subscription ─────────────────────────────────────────
        var subscriptionId = await dbService.CreateSubscriptionAsync(new SubscriptionRecord(
            ClientId:     clientId,
            Month:        monthDate,
            TotalLessons: 8,
            Price:        amount,
            Recipient:    recipient?.Trim()
        ), ct);

        logger.LogInformation(
            "Абонемент #{SubId}: клиент={Client}, период={Month}/{Year}, получатель={Recipient}",
            subscriptionId, clientFullName, monthDate.Month, monthDate.Year, recipient);

        // ── Google Sheets ───────────────────────────────────────────────
        var monthLabel = $"{GetRussianMonthName(monthDate.Month)} {monthDate.Year}";
        var sheetsOk = await sheetsService.WriteSubscriptionAsync(
            subscriptionId, clientFullName, monthLabel,
            recipient?.Trim(), amount, DateOnly.FromDateTime(DateTime.Now));

        var sheetsNote = sheetsOk ? "" : "\n⚠️ <i>Не записался в Sheets — проверь вручную.</i>";

        // ── Confirmation ────────────────────────────────────────────────
        var lines = new List<string>
        {
            "✅ <b>Абонемент оформлен</b>\n",
            $"👤 Ученик: <b>{clientFullName}</b>",
            $"🗓 Период: <b>{monthLabel}</b>",
            $"💰 Сумма: <b>{amount:N0} ₸</b>",
        };
        if (!string.IsNullOrWhiteSpace(recipient))
            lines.Add($"🧑 Получатель: <b>{recipient}</b>");

        return new HandlerResult(true, string.Join("\n", lines) + sheetsNote);
    }

    public async Task<HandlerResult> HandleAsync(
        Dictionary<string, string?> intentParams,
        CancellationToken ct)
    {
        // ── 1. Извлекаем параметры ─────────────────────────────────────────
        if (!intentParams.TryGetValue("student_name", out var studentName) || string.IsNullOrWhiteSpace(studentName))
            return new HandlerResult(false, "❓ Не понял имя ученика. Попробуй ещё раз:\n<i>Айгерим оплатила 15000 за апрель</i>");

        if (!intentParams.TryGetValue("amount", out var amountStr) || !decimal.TryParse(amountStr, out var amount) || amount <= 0)
            return new HandlerResult(false, "❓ Не понял сумму. Укажи число:\n<i>Айгерим оплатила 15000 за апрель</i>");

        intentParams.TryGetValue("period", out var periodStr);
        intentParams.TryGetValue("recipient", out var recipient);

        // ── 2. Парсим период (месяц + год) ───────────────────────────────
        var (paymentMonth, paymentYear) = ParsePeriod(periodStr);
        var monthDate = new DateOnly(paymentYear, paymentMonth, 1);

        // ── 3. Ищем ученика ──────────────────────────────────────────────
        var clients = await dbService.FindClientByNameAsync(studentName!, ct);

        if (clients.Count == 0)
            return new HandlerResult(false, $"❌ Ученик <b>{studentName}</b> не найден в базе.\n\nПроверь написание имени.");

        if (clients.Count > 1)
        {
            var list = string.Join("\n", clients.Select(c => $"• {c.FullName} (id: {c.Id})"));
            return new HandlerResult(false,
                $"🔍 Нашёл несколько учеников с именем <b>{studentName}</b>:\n{list}\n\nУточни полное ФИО.");
        }

        var client = clients[0];

        // ── 4. Проверка дубля ─────────────────────────────────────────────
        var isDuplicate = await dbService.HasSubscriptionForMonthAsync(client.Id, monthDate, ct);
        if (isDuplicate)
        {
            var monthName = GetRussianMonthName(paymentMonth);
            return new HandlerResult(false,
                $"⚠️ У <b>{client.FullName}</b> уже есть абонемент за <b>{monthName} {paymentYear}</b>.");
        }

        // ── 5. Создаём абонемент в PostgreSQL ──────────────────────────────
        var subscriptionId = await dbService.CreateSubscriptionAsync(new SubscriptionRecord(
            ClientId:     client.Id,
            Month:        monthDate,
            TotalLessons: 8,
            Price:        amount,
            Recipient:    recipient?.Trim()
        ), ct);

        logger.LogInformation(
            "Абонемент #{SubId}: клиент={Client}, период={Month}/{Year}, получатель={Recipient}",
            subscriptionId, client.FullName, paymentMonth, paymentYear, recipient);

        // ── 6. Пишем в Google Sheets ───────────────────────────────────────
        var monthLabel = $"{GetRussianMonthName(paymentMonth)} {paymentYear}";
        var sheetsOk = await sheetsService.WriteSubscriptionAsync(
            subscriptionId, client.FullName, monthLabel,
            recipient?.Trim(), amount, DateOnly.FromDateTime(DateTime.Now));

        var sheetsNote = sheetsOk ? "" : "\n⚠️ <i>Не записался в Sheets — проверь вручную.</i>";

        // ── 7. Формируем подтверждение ────────────────────────────────────
        var lines = new List<string>
        {
            "✅ <b>Абонемент оформлен</b>\n",
            $"👤 Ученик: <b>{client.FullName}</b>",
            $"🗓 Период: <b>{monthLabel}</b>",
            $"💰 Сумма: <b>{amount:N0} ₸</b>",
        };
        if (!string.IsNullOrWhiteSpace(recipient))
            lines.Add($"🧑 Получатель: <b>{recipient}</b>");

        return new HandlerResult(true, string.Join("\n", lines) + sheetsNote);
    }

    // ── Вспомогательные методы ─────────────────────────────────────────────

    public static (int month, int year) ParsePeriod(string? period)
    {
        var now = DateTime.Now;
        if (string.IsNullOrWhiteSpace(period))
            return (now.Month, now.Year);

        var parts = period.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int month = now.Month;
        int year = now.Year;

        foreach (var part in parts)
        {
            if (RussianMonths.TryGetValue(part, out var m)) month = m;
            if (int.TryParse(part, out var y) && y > 2000 && y < 2100) year = y;
        }

        return (month, year);
    }

    internal static string GetRussianMonthName(int month) => month switch
    {
        1  => "январь",  2  => "февраль", 3  => "март",
        4  => "апрель",  5  => "май",     6  => "июнь",
        7  => "июль",    8  => "август",  9  => "сентябрь",
        10 => "октябрь", 11 => "ноябрь",  12 => "декабрь",
        _  => "?"
    };
}
