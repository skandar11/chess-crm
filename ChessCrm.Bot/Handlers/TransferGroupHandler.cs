using ChessCrm.Bot.Services;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Handlers;

/// <summary>
/// Transfers a student from one group to another (remove + add in one operation).
/// Does NOT touch Google Sheets.
/// </summary>
public class TransferGroupHandler(
    DatabaseService dbService,
    ILogger<TransferGroupHandler> logger)
{
    public async Task<HandlerResult> HandleAsync(
        int clientId, string clientFullName,
        int fromGroupId, string fromGroupName,
        int toGroupId, string toGroupName,
        CancellationToken ct)
    {
        // ── 1. Remove from source group ─────────────────────────────────
        var removed = await dbService.RemoveClientFromGroupAsync(clientId, fromGroupId, ct);

        if (!removed)
            return new HandlerResult(false,
                $"❌ Не удалось убрать <b>{clientFullName}</b> из группы <b>{fromGroupName}</b>.");

        // ── 2. Add to target group ──────────────────────────────────────
        try
        {
            await dbService.AddClientToGroupAsync(clientId, toGroupId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Transfer failed at add step: client={ClientId} removed from group={FromGroupId} but could not add to group={ToGroupId}",
                clientId, fromGroupId, toGroupId);

            return new HandlerResult(false,
                $"⚠️ <b>{clientFullName}</b> убран из группы <b>{fromGroupName}</b>, " +
                $"но <b>не удалось</b> записать в группу <b>{toGroupName}</b>.\n" +
                $"Ошибка: {ex.Message}\n\n" +
                $"Запиши в новую группу вручную.");
        }

        logger.LogInformation(
            "Transferred client={ClientId} ({Name}) from group={FromGroupId} ({FromName}) to group={ToGroupId} ({ToName})",
            clientId, clientFullName, fromGroupId, fromGroupName, toGroupId, toGroupName);

        return new HandlerResult(true,
            $"✅ <b>{clientFullName}</b> перенесён\n" +
            $"  Из группы: <b>{fromGroupName}</b>\n" +
            $"  В группу: <b>{toGroupName}</b>");
    }
}
