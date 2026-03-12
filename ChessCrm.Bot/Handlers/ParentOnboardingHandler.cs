using ChessCrm.Bot.Services;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Handlers;

/// <summary>
/// Handles /start TOKEN deep link — activates the invite and creates the parent role.
/// NOT routed through IntentService.
/// </summary>
public class ParentOnboardingHandler(
    DatabaseService dbService,
    ILogger<ParentOnboardingHandler> logger)
{
    public async Task<HandlerResult> HandleAsync(
        string token, long tgId, string? tgUsername, CancellationToken ct)
    {
        var (success, clientFullName, error) = await dbService.ActivateInviteAsync(token, tgId, tgUsername, ct);

        if (!success)
        {
            var errorMessage = error switch
            {
                "not_found" => "Ссылка недействительна. Обратитесь к администратору.",
                "expired" => "Срок ссылки истёк. Попросите администратора сгенерировать новую.",
                "already_activated" => "Эта ссылка уже была использована.",
                _ => "Произошла ошибка. Обратитесь к администратору."
            };

            logger.LogWarning("ParentOnboarding failed: token={Token}, error={Error}", token, error);
            return new HandlerResult(false, errorMessage);
        }

        logger.LogInformation("ParentOnboarding success: tgId={TgId}, client={Client}", tgId, clientFullName);

        return new HandlerResult(true,
            $"Привет! Я нашёл вашего ребёнка — {clientFullName}.\n\n" +
            "Скоро здесь появятся команды для просмотра расписания и абонемента.");
    }
}
