using System.Text.Json;
using ChessCrm.Bot.Services;
using Microsoft.Extensions.Logging;

namespace ChessCrm.Bot.Handlers;

public class ParentCommandsHandler(
    DatabaseService dbService,
    AiService aiService,
    ILogger<ParentCommandsHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<HandlerResult> HandleScheduleAsync(int clientId, CancellationToken ct)
    {
        var groups = await dbService.GetChildScheduleAsync(clientId, ct);
        var json = JsonSerializer.Serialize(groups, JsonOpts);

        logger.LogInformation("ParentSchedule: clientId={ClientId}, groups={Count}", clientId, groups.Count);

        var formatted = await aiService.FormatParentResponseAsync("schedule", json, ct);
        return new HandlerResult(true, formatted);
    }

    public async Task<HandlerResult> HandleInfoAsync(int clientId, CancellationToken ct)
    {
        var info = await dbService.GetChildInfoAsync(clientId, ct);
        var json = info != null ? JsonSerializer.Serialize(info, JsonOpts) : "null";

        logger.LogInformation("ParentInfo: clientId={ClientId}, found={Found}", clientId, info != null);

        var formatted = await aiService.FormatParentResponseAsync("child_info", json, ct);
        return new HandlerResult(true, formatted);
    }

    public async Task<HandlerResult> HandleSubscriptionAsync(int clientId, CancellationToken ct)
    {
        var sub = await dbService.GetChildSubscriptionAsync(clientId, ct);
        var json = sub != null ? JsonSerializer.Serialize(sub, JsonOpts) : "null";

        logger.LogInformation("ParentSubscription: clientId={ClientId}, found={Found}", clientId, sub != null);

        var formatted = await aiService.FormatParentResponseAsync("subscription", json, ct);
        return new HandlerResult(true, formatted);
    }
}
