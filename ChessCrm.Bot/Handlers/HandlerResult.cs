namespace ChessCrm.Bot.Handlers;

/// <summary>Результат выполнения команды записи.</summary>
/// <param name="Success">True если операция прошла успешно.</param>
/// <param name="Message">Текст ответа боту.</param>
/// <param name="NeedsConfirmation">True если нужно дополнительное уточнение от менеджера.</param>
public record HandlerResult(bool Success, string Message, bool NeedsConfirmation = false);
