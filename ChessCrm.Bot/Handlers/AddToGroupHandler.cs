using ChessCrm.Bot.Services;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Handlers;

/// <summary>
/// Add an existing or new student into a group.
/// Checks:
///   1. Client found (by name or id)
///   2. Group found (by group_id or day+time)
///   3. Client not already in group
///   4. Group not full (current &lt; max_students)
/// Then writes to group_students (DB).
/// </summary>
public class AddToGroupHandler(
    DatabaseService dbService,
    ILogger<AddToGroupHandler> logger)
{
    /// <summary>
    /// Add by client id (already known) + params from intent.
    /// Used by NewStudentHandler right after creating a client.
    /// </summary>
    public Task<HandlerResult> HandleAsync(
        int clientId, string clientFullName, Dictionary<string, string?> intentParams, CancellationToken ct)
        => RunAsync(clientId, clientFullName, intentParams, ct);

    /// <summary>
    /// Add triggered by "add_to_group" intent (student_name in params).
    /// Resolves client by name first.
    /// </summary>
    public async Task<HandlerResult> HandleAsync(
        Dictionary<string, string?> intentParams, CancellationToken ct)
    {
        // ── Resolve client by name ─────────────────────────────────────────
        if (!intentParams.TryGetValue("student_name", out var rawName) || string.IsNullOrWhiteSpace(rawName))
            return new HandlerResult(false, "❓ Не понял имя ученика. Напиши: <i>Записать Иванова в среду 18:00</i>");

        var matches = await dbService.FindClientByNameAsync(rawName, ct);

        if (matches.Count == 0)
            return new HandlerResult(false, $"❌ Ученик <b>{rawName}</b> не найден в базе.");

        if (matches.Count > 1)
        {
            var list = string.Join("\n", matches.Select(m => $"• {m.FullName} (id {m.Id})"));
            return new HandlerResult(false,
                $"🔎 Нашёл несколько совпадений — уточни полное имя:\n{list}");
        }

        var client = matches[0];
        return await RunAsync(client.Id, client.FullName, intentParams, ct);
    }

    // ── Core logic ─────────────────────────────────────────────────────────

    private async Task<HandlerResult> RunAsync(
        int clientId, string clientFullName,
        Dictionary<string, string?> intentParams,
        CancellationToken ct)
    {
        // ── Parse group lookup params ─────────────────────────────────────
        intentParams.TryGetValue("group_id",    out var groupIdStr);
        var dayStr    = intentParams.GetValueOrDefault("group_1_day") ?? intentParams.GetValueOrDefault("day_of_week");
        var timeStr   = intentParams.GetValueOrDefault("group_1_time") ?? intentParams.GetValueOrDefault("time");
        var groupName = intentParams.GetValueOrDefault("group_name");

        int?       groupId = int.TryParse(groupIdStr, out var gid) ? gid : null;
        DayOfWeek? day     = ParseDay(dayStr);
        TimeSpan?  time    = ParseTime(timeStr);

        if (string.IsNullOrWhiteSpace(groupName) && groupId == null && (day == null || time == null))
            return new HandlerResult(false,
                "❓ Не понял группу. Укажи день и время: <i>Записать Иванова в среду 18:00</i>\n" +
                "Или номер группы: <i>Записать Иванова в группу 5</i>");

        // ── Find group ────────────────────────────────────────────────────
        var group = await dbService.FindGroupAsync(day, time, groupId, ct, groupName: groupName);

        if (group == null)
        {
            var hint = groupId.HasValue ? $"id {groupId}" : $"{dayStr} {timeStr}";
            return new HandlerResult(false, $"❌ Группа <b>{hint}</b> не найдена.");
        }

        // ── Already in group? ───────────────────────────────────────────
        if (await dbService.IsClientInGroupAsync(clientId, group.Id, ct))
            return new HandlerResult(false,
                $"ℹ️ <b>{clientFullName}</b> уже записан в группу <b>{group.Name}</b>.");

        // ── Group full? ───────────────────────────────────────────────────
        if (group.CurrentCount >= group.MaxStudents)
            return new HandlerResult(false,
                $"🚫 Группа <b>{group.Name}</b> заполнена " +
                $"({group.CurrentCount}/{group.MaxStudents} учеников).");

        // ── DB: insert group_students ─────────────────────────────────────
        await dbService.AddClientToGroupAsync(clientId, group.Id, ct);

        logger.LogInformation("Added client={ClientId} ({Name}) to group={GroupId} ({GroupName})",
            clientId, clientFullName, group.Id, group.Name);

        return new HandlerResult(true,
            $"✅ <b>{clientFullName}</b> записан в группу <b>{group.Name}</b>\n" +
            $"👥 Мест занято: {group.CurrentCount + 1}/{group.MaxStudents}");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static DayOfWeek? ParseDay(string? raw) => DayOfWeekMapper.MapToDayOfWeek(raw);

    private static TimeSpan? ParseTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return TimeSpan.TryParse(raw.Trim(), out var ts) ? ts : null;
    }
}
