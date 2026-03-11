using ChessCrm.Bot.Services;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Handlers;

/// <summary>
/// Generates an invite token and deep link for a parent to onboard via /start TOKEN.
/// Overwrites any previous token for the client.
/// </summary>
public class InviteHandler(
    DatabaseService dbService,
    GoogleSheetsService sheetsService,
    ILogger<InviteHandler> logger)
{
    public async Task<HandlerResult> HandleAsync(
        int clientId, string clientFullName, string botUsername, CancellationToken ct)
    {
        var token = Guid.NewGuid().ToString("N");
        var expiresAt = DateTimeOffset.UtcNow.AddHours(24);

        var saved = await dbService.CreateInviteTokenAsync(clientId, token, expiresAt, ct);
        if (!saved)
            return new HandlerResult(false,
                $"❌ Не удалось создать инвайт для <b>{clientFullName}</b>.");

        var deepLink = $"https://t.me/{botUsername}?start={token}";

        logger.LogInformation("InviteHandler: clientId={ClientId} ({Name}), link={Link}",
            clientId, clientFullName, deepLink);

        // Google Sheets (non-critical)
        try
        {
            await sheetsService.WriteInviteAsync(clientId, clientFullName, token, deepLink, expiresAt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sheets [Инвайты]: ошибка записи, данные не потеряны");
        }

        return new HandlerResult(true,
            $"✅ Инвайт для <b>{clientFullName}</b>\n\n" +
            $"🔗 Ссылка: <code>{deepLink}</code>\n\n" +
            $"⏳ Действительна 24 часа.");
    }
}
