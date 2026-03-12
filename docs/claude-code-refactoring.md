# Claude Code Task — Рефакторинг ChessCrm.Bot

_Дата: март 2026 | Статус: готово к выполнению_

Проект: `/Users/skandar/chess-automation/chess-crm`

---

## Контекст

ChessCrm.Bot — Telegram-бот для управления шахматной школой на C# / .NET 10.
Стек: Claude API (Haiku), PostgreSQL 16, raw Npgsql, Google Sheets (зеркало).
Уже завершены спринты 0–4 (8 интентов, роли, инвайты, родительский функционал).

Проводится рефакторинг перед выходом на живые данные.

---

## Задачи

### 1. Проверить model string в AiService.cs и IntentService.cs

**Файл:** `ChessCrm.Bot/Services/AiService.cs`
**Файл:** `ChessCrm.Bot/Services/IntentService.cs`

Текущее значение в коде:
```csharp
private const string SqlModel  = "claude-haiku-4-5";
private const string FormatModel = "claude-haiku-4-5";
// и в IntentService:
model = "claude-haiku-4-5"
```

**Задача:** Проверить через Anthropic API docs или тестовый вызов, какой точный string использовать для Claude Haiku 4.5. Вероятные варианты:
- `claude-haiku-4-5`
- `claude-haiku-4-5-20251001`

Если нужна дата-версия — обновить во всех местах. Использовать единую константу в `AppConfig` или `AiService`.

---

### 2. Привести ParentOnboardingHandler к единому возвращаемому типу

**Файл:** `ChessCrm.Bot/Handlers/ParentOnboardingHandler.cs`

**Текущее:**
```csharp
public async Task<(bool Success, string Message)> HandleAsync(...)
```

**Нужно:** Заменить на `HandlerResult` — как у всех остальных хендлеров:
```csharp
public async Task<HandlerResult> HandleAsync(...)
```

**Обновить вызов в TelegramBotService.cs:**
```csharp
// было
var (success, responseMsg) = await parentOnboardingHandler.HandleAsync(token, userId, tgUsername, ct);
await bot.SendMessage(chatId, responseMsg, cancellationToken: ct);

// стало
var result = await parentOnboardingHandler.HandleAsync(token, userId, tgUsername, ct);
await bot.SendMessage(chatId, result.Message, cancellationToken: ct);
```

---

### 3. Проверить что google-credentials.json в .gitignore

**Файл:** `.gitignore`

Убедиться что строки есть:
```
google-credentials.json
*.json.key
```

Файл `google-credentials.json` лежит в корне репозитория — это секретный ключ, он не должен попасть в git.

---

### 4. Добавить FindClientByNameAsync поиск по неактивным для edit_student

**Файл:** `ChessCrm.Bot/Services/DatabaseService.cs`

**Проблема:** `FindClientByNameAsync` ищет только `is_active = true`. Если администратор деактивировал ученика и хочет его снова активировать через `edit_student is_active=true` — бот его не найдёт.

**Решение:** Добавить параметр `bool includeInactive = false`:
```csharp
public async Task<List<ClientMatch>> FindClientByNameAsync(
    string name, CancellationToken ct = default, bool includeInactive = false)
```

Добавить в WHERE: `AND (is_active = true OR @includeInactive)` или строить SQL динамически.

В `TelegramBotService.BuildEditStudentPreviewAsync` вызывать с `includeInactive: true`.

---

### 5. Вынести ParsePeriod из TelegramBotService в SubscriptionHandler

**Файл:** `ChessCrm.Bot/Services/TelegramBotService.cs`
**Файл:** `ChessCrm.Bot/Handlers/SubscriptionHandler.cs`

`ParsePeriod` — приватный метод в `TelegramBotService`, логически принадлежит `SubscriptionHandler`.

**Задача:**
- Сделать `ParsePeriod` публичным статическим методом в `SubscriptionHandler`
- В `TelegramBotService` вызывать `SubscriptionHandler.ParsePeriod(periodStr)`
- Удалить приватный `ParsePeriod` из `TelegramBotService`

---

### 6. Проверить тесты после рефакторинга

**Папка:** `Tests/`

После всех изменений запустить:
```bash
dotnet test
```

Убедиться что все тесты проходят. Если появились новые падения — исправить.

---

## Что НЕ менять

- Архитектуру хендлеров и сервисов — она правильная
- Промпты в `IntentPrompts.cs` и `ChessPrompts.cs` — не трогать без отдельного задания
- Нейминг методов `BuildXxxPreviewAsync` — консистентен, оставить
- `PendingActionService` — работает корректно
- `DayOfWeekMapper` — работает корректно
- `GuardReadOnly` — работает корректно

---

## Проверка после выполнения

```bash
# Сборка без ошибок
dotnet build

# Тесты
dotnet test

# Запуск
cd ChessCrm.Bot && dotnet run
```

Ожидаемый вывод:
```
[HH:mm:ss INF] Бот @ИмяБота запущен.
```
