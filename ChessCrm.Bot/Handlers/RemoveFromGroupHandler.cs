using ChessCrm.Bot.Services;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Handlers;

/// <summary>
/// Removes a student from a group (sets left_at = today in group_students).
/// Does NOT touch Google Sheets.
/// </summary>
public class RemoveFromGroupHandler(
    DatabaseService dbService,
    ILogger<RemoveFromGroupHandler> logger)
{
    public async Task<HandlerResult> HandleAsync(
        int clientId, string clientFullName,
        int groupId, string groupName,
        CancellationToken ct)
    {
        var removed = await dbService.RemoveClientFromGroupAsync(clientId, groupId, ct);

        if (!removed)
            return new HandlerResult(false,
                $"❌ Не удалось убрать <b>{clientFullName}</b> из группы <b>{groupName}</b>.");

        logger.LogInformation("Removed client={ClientId} ({Name}) from group={GroupId} ({GroupName})",
            clientId, clientFullName, groupId, groupName);

        return new HandlerResult(true,
            $"✅ <b>{clientFullName}</b> убран из группы <b>{groupName}</b>");
    }
}
