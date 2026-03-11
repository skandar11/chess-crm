using System.Collections.Concurrent;
using ChessCrm.Bot.Handlers;

namespace ChessCrm.Bot.Services;

/// <summary>
/// Хранит отложенное действие для конкретного чата.
/// Менеджер видит превью → нажимает ✅, ❌ или ✏️ → действие выполняется, отменяется или дополняется.
/// </summary>
public class PendingActionService
{
    private readonly ConcurrentDictionary<long, PendingAction> _pending = new();

    // Чаты, ожидающие ввода дополнительных данных (после нажатия ✏️)
    private readonly ConcurrentDictionary<long, PendingEditState> _editStates = new();

    public void Set(long chatId, PendingAction action) =>
        _pending[chatId] = action;

    /// <summary>Забирает действие (удаляет из хранилища). Null если нет.</summary>
    public PendingAction? Take(long chatId)
    {
        _pending.TryRemove(chatId, out var action);
        return action;
    }

    /// <summary>Возвращает действие без удаления.</summary>
    public PendingAction? Peek(long chatId) =>
        _pending.TryGetValue(chatId, out var action) ? action : null;

    public bool Has(long chatId) => _pending.ContainsKey(chatId);

    // ── Edit state ────────────────────────────────────────────────────────

    public void SetWaitingForEdit(long chatId, string intent, Dictionary<string, string?> currentParams) =>
        _editStates[chatId] = new PendingEditState(intent, currentParams);

    public PendingEditState? TakeEditState(long chatId)
    {
        _editStates.TryRemove(chatId, out var state);
        return state;
    }

    public bool IsWaitingForEdit(long chatId) => _editStates.ContainsKey(chatId);
}

public record PendingAction(
    string Preview,
    string Intent,
    Dictionary<string, string?> Params,
    Func<CancellationToken, Task<HandlerResult>> Execute
);

public record PendingEditState(
    string Intent,
    Dictionary<string, string?> Params
);
