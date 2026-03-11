using ChessCrm.Bot.Services;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Handlers;

/// <summary>
/// Handles "edit student" command — updates existing client fields in PostgreSQL.
/// Does NOT touch Google Sheets.
/// </summary>
public class EditStudentHandler(
    DatabaseService dbService,
    ILogger<EditStudentHandler> logger)
{
    /// <summary>
    /// Updates client fields in the database.
    /// </summary>
    /// <param name="clientId">Resolved client ID.</param>
    /// <param name="clientFullName">Client full name (for logging/messages).</param>
    /// <param name="fields">Field name → new value (as string from Claude).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<HandlerResult> HandleAsync(
        int clientId, string clientFullName,
        Dictionary<string, string> fields,
        CancellationToken ct)
    {
        if (fields.Count == 0)
            return new HandlerResult(false, "❓ Не указаны поля для изменения.");

        // ── Parse string values to proper DB types ─────────────────────
        var typedFields = new Dictionary<string, object?>();

        foreach (var (key, value) in fields)
        {
            switch (key)
            {
                case "birth_date":
                    if (DateOnly.TryParseExact(value.Trim(), "dd.MM.yyyy",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var bd))
                    {
                        typedFields[key] = bd;
                    }
                    else
                    {
                        return new HandlerResult(false,
                            $"❌ Не удалось распознать дату: <b>{value}</b>. Формат: ДД.ММ.ГГГГ");
                    }
                    break;

                case "is_active":
                    if (bool.TryParse(value.Trim(), out var isActive))
                        typedFields[key] = isActive;
                    else
                        return new HandlerResult(false,
                            $"❌ Неверное значение is_active: <b>{value}</b>. Ожидается true/false.");
                    break;

                case "subscriptions_count":
                    if (int.TryParse(value.Trim(), out var sc) && sc >= 0)
                        typedFields[key] = sc;
                    else
                        return new HandlerResult(false,
                            $"❌ Неверное значение subscriptions_count: <b>{value}</b>. Ожидается число >= 0.");
                    break;

                default:
                    typedFields[key] = value.Trim();
                    break;
            }
        }

        // ── Update DB ──────────────────────────────────────────────────
        var updated = await dbService.UpdateClientFieldsAsync(clientId, typedFields, ct);

        if (!updated)
            return new HandlerResult(false, $"❌ Не удалось обновить данные ученика <b>{clientFullName}</b>.");

        logger.LogInformation("Обновлён клиент #{Id} ({Name}): [{Fields}]",
            clientId, clientFullName, string.Join(", ", fields.Keys));

        // ── Build confirmation ─────────────────────────────────────────
        var lines = new List<string>
        {
            $"✅ <b>Данные обновлены</b>\n",
            $"👤 <b>{clientFullName}</b> (id: {clientId})\n"
        };

        foreach (var (key, value) in fields)
            lines.Add($"  {FieldDisplayName(key)}: <b>{value}</b>");

        return new HandlerResult(true, string.Join("\n", lines));
    }

    internal static string FieldDisplayName(string fieldName) => fieldName switch
    {
        "birth_date"          => "Дата рождения",
        "parent_name"         => "Родитель",
        "parent_phone"        => "Телефон родителя",
        "parent_name_2"       => "Родитель 2",
        "parent_phone_2"      => "Телефон родителя 2",
        "level"               => "Уровень",
        "notes"               => "Заметка",
        "is_active"           => "Статус",
        "subscriptions_count" => "Кол-во абонементов",
        _                     => fieldName
    };
}
