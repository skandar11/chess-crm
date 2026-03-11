using ChessCrm.Bot.Services;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Handlers;

/// <summary>
/// Handles "new student" command.
/// Examples:
///   "Новый ученик Алия Сейткали, 4 ноября 2019, мама Жанар 777..."
///   "создай клиента Попов Богдан вт18 сб11"
/// </summary>
public class NewStudentHandler(
    DatabaseService dbService,
    AddToGroupHandler addToGroupHandler,
    GoogleSheetsService sheetsService,
    ILogger<NewStudentHandler> logger)
{
    public async Task<HandlerResult> HandleAsync(
        Dictionary<string, string?> intentParams,
        CancellationToken ct)
    {
        // ── 1. Extract params ──────────────────────────────────────────────
        if (!intentParams.TryGetValue("last_name", out var lastName) || string.IsNullOrWhiteSpace(lastName))
            return new HandlerResult(false,
                "❓ Не понял фамилию ученика. Попробуй:\n<i>Новый ученик Сейткали Алия, 04.11.2019, мама Жанар 777...</i>");

        intentParams.TryGetValue("first_name",    out var firstName);
        intentParams.TryGetValue("birth_date",    out var birthDateRaw);
        intentParams.TryGetValue("parent_name",   out var parentName);
        intentParams.TryGetValue("parent_phone",  out var parentPhone);
        intentParams.TryGetValue("parent_name_2",  out var parentName2);
        intentParams.TryGetValue("parent_phone_2", out var parentPhone2);
        intentParams.TryGetValue("level",          out var level);

        // ── 2. Build full_name ───────────────────────────────────────────
        var fullName = string.IsNullOrWhiteSpace(firstName)
            ? lastName.Trim()
            : $"{lastName.Trim()} {firstName.Trim()}";

        // ── 3. Parse birth date (DD.MM.YYYY) ─────────────────────────────
        DateOnly? birthDate = null;
        string? birthDateStr = null;
        if (!string.IsNullOrWhiteSpace(birthDateRaw)
            && DateOnly.TryParseExact(birthDateRaw.Trim(), "dd.MM.yyyy",
               System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.None, out var parsed))
        {
            birthDate    = parsed;
            birthDateStr = parsed.ToString("dd.MM.yyyy");
        }

        // ── 4. Insert into DB ────────────────────────────────────────────
        var clientId = await dbService.CreateClientAsync(new NewClientRecord(
            FullName:     fullName,
            BirthDate:    birthDate,
            ParentName:   parentName?.Trim(),
            ParentPhone:  parentPhone?.Trim(),
            ParentName2:  parentName2?.Trim(),
            ParentPhone2: parentPhone2?.Trim(),
            Level:        level?.Trim()
        ), ct);

        logger.LogInformation("Создан новый клиент #{Id}: {FullName}", clientId, fullName);

        // ── 5. Write to Sheets journal ─────────────────────────────────
        var journalOk = await sheetsService.WriteNewClientToJournalAsync(
            clientId, fullName, birthDateStr, level?.Trim(),
            parentName?.Trim(), parentPhone?.Trim());

        // ── 6. Build confirmation lines ──────────────────────────────────
        var lines = new List<string>
        {
            "✅ <b>Новый ученик добавлен</b>\n",
            $"👤 <b>{fullName}</b> (id: {clientId})"
        };

        if (birthDate.HasValue)
            lines.Add($"🎂 Дата рождения: {birthDate.Value:dd.MM.yyyy}");

        if (!string.IsNullOrWhiteSpace(level))
            lines.Add($"📊 Уровень: {level}");

        if (!string.IsNullOrWhiteSpace(parentName))
            lines.Add($"👩 Родитель: {parentName}");

        if (!string.IsNullOrWhiteSpace(parentPhone))
            lines.Add($"📞 Телефон: {parentPhone}");

        if (!string.IsNullOrWhiteSpace(parentName2))
            lines.Add($"👨 Родитель 2: {parentName2}");

        if (!string.IsNullOrWhiteSpace(parentPhone2))
            lines.Add($"📞 Телефон 2: {parentPhone2}");

        if (!journalOk)
            lines.Add("⚠️ <i>Не записался в лист Ученики — проверь вручную.</i>");

        // ── 7. Add to groups (array) ──────────────────────────────────
        var groupParams = ParseGroups(intentParams);

        if (groupParams.Count > 0)
        {
            lines.Add("");
            foreach (var gp in groupParams)
            {
                var addResult = await addToGroupHandler.HandleAsync(clientId, fullName, gp, ct);
                lines.Add(addResult.Message);
            }
        }
        else
        {
            lines.Add("\n<i>Не забудь записать в группу.</i>");
        }

        return new HandlerResult(true, string.Join("\n", lines));
    }

    /// <summary>
    /// Extracts group params from intent (flat format).
    /// </summary>
    private static List<Dictionary<string, string?>> ParseGroups(Dictionary<string, string?> intentParams)
    {
        var result = new List<Dictionary<string, string?>>();

        for (int n = 1; n <= 5; n++)
        {
            var dayKey  = $"group_{n}_day";
            var timeKey = $"group_{n}_time";
            var idKey   = $"group_{n}_id";

            var hasDay  = intentParams.TryGetValue(dayKey,  out var day)  && !string.IsNullOrWhiteSpace(day);
            var hasTime = intentParams.TryGetValue(timeKey, out var time) && !string.IsNullOrWhiteSpace(time);
            var hasId   = intentParams.TryGetValue(idKey,   out var gid)  && !string.IsNullOrWhiteSpace(gid);

            if (!hasDay && !hasTime && !hasId) break;

            var gp = new Dictionary<string, string?>();
            if (hasId)   gp["group_id"]    = gid;
            if (hasDay)  gp["day_of_week"] = day;
            if (hasTime) gp["time"]        = time;
            result.Add(gp);
        }

        if (result.Count == 0
            && intentParams.TryGetValue("group_id", out var topGid)
            && !string.IsNullOrWhiteSpace(topGid))
        {
            result.Add(new Dictionary<string, string?> { ["group_id"] = topGid });
        }

        return result;
    }
}
