# Technical Reference — ChessCrm.Bot
_Версия 2.2 | 11.03.2026 | alpha v5 — parent commands_

Только реализованный код. Запланированные функции здесь не описываются.

---

## Структура проекта

```
ChessCrm.Bot/
├── Configuration/
│   └── AppConfig.cs              — чтение .env переменных
├── Handlers/
│   ├── HandlerResult.cs          — возвращаемый тип хэндлеров (bool Success, string Message)
│   ├── NewStudentHandler.cs      — создание нового клиента
│   ├── EditStudentHandler.cs     — редактирование данных ученика (только PostgreSQL)
│   ├── AddToGroupHandler.cs       — запись клиента в группу
│   ├── RemoveFromGroupHandler.cs    — убрать клиента из группы
│   ├── TransferGroupHandler.cs      — перенос клиента между группами
│   ├── SubscriptionHandler.cs       — продажа абонемента
│   ├── InviteHandler.cs             — генерация инвайт-ссылки для родителя
│   ├── ParentOnboardingHandler.cs   — онбординг родителя через /start TOKEN
│   └── ParentCommandsHandler.cs    — родительские команды (расписание, данные, абонемент)
├── Prompts/
│   ├── IntentPrompts.cs          — промпты для классификации намерений и допданных
│   └── ChessPrompts.cs           — промпты для SQL-генерации и форматирования ответа
├── Services/
│   ├── TelegramBotService.cs     — точка входа всех апдейтов, роутинг, превью, callback
│   ├── IntentService.cs          — вызов Claude API для классификации намерений
│   ├── AiService.cs              — вызов Claude API для SQL-генерации и форматирования
│   ├── DatabaseService.cs        — все SQL-запросы к PostgreSQL
│   ├── GoogleSheetsService.cs    — запись в Google Sheets (зеркало)
│   └── PendingActionService.cs   — хранение отложенных действий (превью -> подтверждение)
└── Program.cs                    — DI-контейнер, регистрация сервисов, запуск бота
```

---

## Конфигурация

**`AppConfig`** читает из `.env` при старте:

| Переменная | Описание |
|---|---|
| `DB_CONNECTION_STRING` | PostgreSQL connection string |
| `TELEGRAM_BOT_TOKEN` | Токен бота |
| `ANTHROPIC_API_KEY` | Claude API ключ |
| `GOOGLE_CREDENTIALS_PATH` | Путь к google-credentials.json (default: google-credentials.json) |
| `GOOGLE_SPREADSHEET_ID_CRM` | ID таблицы CRM (Ученики, Абонементы, Инвайты) |

`ALLOWED_TELEGRAM_USER_IDS` удалён — заменён системой ролей через таблицу `user_roles`.

---

## Основной поток обработки сообщения

```
Telegram update
    |
TelegramBotService.HandleUpdateAsync()
    ├── /start TOKEN → ParentOnboardingHandler (без проверки роли — это и есть онбординг)
    ├── Проверка роли через user_roles:
    │       ├── role = "admin" → продолжение (весь функционал)
    │       ├── role = "parent" → кнопочное меню (📅 Расписание / 📋 Данные ребёнка / 💳 Абонемент)
    │       └── не найден → "Нет доступа. Используйте персональную ссылку от администратора."
    ├── Если IsWaitingForEdit -> HandleEditInputAsync()
    ├── Callback (confirm / cancel / edit) -> HandleCallbackAsync()
    └── Текстовое сообщение (admin):
            |
        IntentService.ClassifyAsync()          <- Claude API -> JSON с intent + params
            |
        Роутинг по intent:
            analytics         -> AiService (SQL генерация + форматирование)
            new_student       -> BuildNewStudentPreviewAsync() -> PendingAction -> превью + кнопки
            sell_subscription -> BuildSubscriptionPreview() -> PendingAction -> превью + кнопки
            add_to_group      -> BuildAddToGroupPreviewAsync() -> PendingAction -> превью + кнопки
            edit_student      -> BuildEditStudentPreviewAsync() -> PendingAction -> превью + кнопки (без ✏️)
            remove_from_group -> BuildRemoveFromGroupPreviewAsync() -> PendingAction -> превью + кнопки
            transfer_group    -> BuildTransferGroupPreviewAsync() -> PendingAction -> превью + кнопки
            generate_invite   -> BuildInvitePreviewAsync() -> PendingAction -> превью + кнопки
            unknown           -> сообщение об ошибке
```

**Паттерн превью -> подтверждение:**
1. Бот строит текстовый превью с данными операции + валидация
2. Кладёт `PendingAction` в `PendingActionService` (по chatId)
3. Отправляет превью с кнопками OK / Отмена / ✏️
4. При OK — вызывает `action.Execute(ct)` -> хэндлер
5. При Отмена — удаляет action, сообщает об отмене
6. При ✏️ — переводит чат в режим ожидания допданных, затем пересборка превью

---

## Handlers

### NewStudentHandler

**Назначение:** создать нового клиента в PostgreSQL и записать в лист Ученики.

**Входные params (из IntentService):**

| Ключ | Обязателен | Описание |
|---|---|---|
| `last_name` | ✅ | Фамилия (нормализована к именительному) |
| `first_name` | — | Имя |
| `birth_date` | — | Дата рождения в формате DD.MM.YYYY |
| `parent_name` | — | ФИО родителя 1 |
| `parent_phone` | — | Телефон родителя 1 |
| `parent_name_2` | — | ФИО родителя 2 |
| `parent_phone_2` | — | Телефон родителя 2 |
| `level` | — | Уровень ученика |
| `group_1_day` / `group_1_time` | — | Первая группа (день + время) |
| `group_2_day` / `group_2_time` | — | Вторая группа (опционально) |

**Шаги выполнения:**
1. Проверяет наличие `last_name`
2. Собирает `full_name` = `last_name + " " + first_name`
3. Парсит `birth_date` через `DateOnly.TryParseExact("dd.MM.yyyy")`
4. `DatabaseService.CreateClientAsync()` -> INSERT в `clients` (с level, parent_name_2, parent_phone_2)
5. `GoogleSheetsService.WriteNewClientToJournalAsync()` -> лист Ученики
6. `AddToGroupHandler` для каждой группы из params
7. Возвращает сводное сообщение

---

### AddToGroupHandler

**Назначение:** записать существующего клиента в группу.

**Входные params:** `student_name`, `group_1_day` + `group_1_time` или `group_id`.

**Шаги:**
1. Fuzzy-поиск клиента по имени
2. Поиск группы по дню+времени или group_id
3. Проверка: группа существует и is_active
4. Проверка: есть свободные места (current < max_students)
5. Проверка: клиент не записан в эту группу уже (left_at IS NULL)
6. INSERT в `group_students`

---

### RemoveFromGroupHandler

**Назначение:** убрать клиента из группы (установить left_at = сегодня). Только PostgreSQL, без Google Sheets.

**Входные данные (из TelegramBotService):**

| Параметр | Тип | Описание |
|---|---|---|
| `clientId` | int | ID клиента |
| `clientFullName` | string | ФИО для лога/ответа |
| `groupId` | int | ID группы |
| `groupName` | string | Название группы для ответа |

**Шаги:**
1. `DatabaseService.RemoveClientFromGroupAsync(clientId, groupId)` — UPDATE group_students SET left_at = CURRENT_DATE
2. Возвращает подтверждение

**Превью (BuildRemoveFromGroupPreviewAsync):**
- Ищет клиента по имени (0→ошибка, 2+→список, 1→продолжение)
- Ищет группу по day+time
- Проверяет: ученик записан в эту группу?
- Кнопки: OK / Отмена (без ✏️)

---

### TransferGroupHandler

**Назначение:** перенести клиента из одной группы в другую (remove + add). Только PostgreSQL, без Google Sheets.

**Входные данные (из TelegramBotService):**

| Параметр | Тип | Описание |
|---|---|---|
| `clientId` | int | ID клиента |
| `clientFullName` | string | ФИО для лога/ответа |
| `fromGroupId` | int | ID исходной группы |
| `fromGroupName` | string | Название исходной группы |
| `toGroupId` | int | ID целевой группы |
| `toGroupName` | string | Название целевой группы |

**Шаги:**
1. `DatabaseService.RemoveClientFromGroupAsync(clientId, fromGroupId)` — убрать из исходной
2. `DatabaseService.AddClientToGroupAsync(clientId, toGroupId)` — добавить в целевую
3. Если add провалился после remove — логирует ошибку, информирует пользователя о частичном результате

**Превью (BuildTransferGroupPreviewAsync):**
- Ищет клиента по имени
- Если исходная группа не указана: GetClientActiveGroupsAsync → 1 группа = auto-fill, 0 = ошибка, 2+ = уточнение
- Если указана: ищет группу, проверяет запись
- Ищет целевую группу
- Проверка: есть места, ученик не в целевой
- Кнопки: OK / Отмена (без ✏️)

---

### EditStudentHandler

**Назначение:** обновить данные существующего клиента в PostgreSQL (без Google Sheets).

**Входные данные (из TelegramBotService):**

| Параметр | Тип | Описание |
|---|---|---|
| `clientId` | int | ID клиента (найден через FindClientByNameAsync) |
| `clientFullName` | string | ФИО для лога/ответа |
| `fields` | Dictionary<string, string> | Поле → новое значение |

**Редактируемые поля (9):** birth_date, parent_name, parent_phone, parent_name_2, parent_phone_2, level, notes, is_active, subscriptions_count. full_name НЕ редактируется.

**Шаги:**
1. Парсит string-значения в типы: birth_date→DateOnly, is_active→bool, subscriptions_count→int, остальные→string
2. Вызывает `DatabaseService.UpdateClientFieldsAsync(clientId, typedFields, ct)`
3. Возвращает подтверждение со списком изменённых полей

**Превью (BuildEditStudentPreviewAsync):**
- Ищет клиента по имени (0→ошибка, 2+→список, 1→продолжение)
- Получает текущие значения через `GetClientFieldsAsync`
- Валидация: birth_date (будущее→блок, возраст<3→warning), телефон (FormatPhone + дубль), subscriptions_count (>=0)
- Формат: "old → new" для каждого поля
- Кнопки: OK / Отмена (без ✏️)

---

### SubscriptionHandler

**Назначение:** продать абонемент одной операцией.

**Входные params:** `student_name`, `amount`, `period` (месяц и год), `recipient`.

**Шаги:**
1. Fuzzy-поиск клиента
2. Разбор периода ("апрель 2026" -> DateOnly 2026-04-01)
3. Проверка дубля: абонемент за этот месяц уже есть?
4. INSERT в `subscriptions` (с recipient)
5. UPDATE `clients SET subscriptions_count = subscriptions_count + 1`
6. Запись в лист Абонементы (GoogleSheetsService)

---

### InviteHandler

**Назначение:** сгенерировать инвайт-ссылку для родителя. Записывает токен в БД и в лист Инвайты.

**Входные данные (из TelegramBotService):**

| Параметр | Тип | Описание |
|---|---|---|
| `clientId` | int | ID ученика |
| `clientFullName` | string | ФИО ученика |
| `botUsername` | string | Username бота для deep link |

**Шаги:**
1. Генерация UUID-токена (`Guid.NewGuid().ToString("N")`)
2. Установка срока: 24 часа
3. `DatabaseService.CreateInviteTokenAsync()` — UPDATE clients SET invite_token, invite_expires_at (перезаписывает предыдущий)
4. Построение deep link: `https://t.me/{botUsername}?start={token}`
5. `GoogleSheetsService.WriteInviteAsync()` — лист Инвайты (non-critical)
6. Возврат ссылки в чат

**Превью (BuildInvitePreviewAsync):**
- Ищет клиента по имени (0→ошибка, 2+→список, 1→продолжение)
- Кнопки: OK / Отмена

---

### ParentOnboardingHandler

**Назначение:** активировать инвайт-ссылку и привязать Telegram-аккаунт родителя к ученику. НЕ проходит через IntentService — детектируется напрямую по `/start TOKEN`.

**Входные данные (из TelegramBotService):**

| Параметр | Тип | Описание |
|---|---|---|
| `token` | string | Токен из deep link |
| `tgId` | long | Telegram ID родителя |
| `tgUsername` | string? | Username родителя |

**Шаги:**
1. `DatabaseService.ActivateInviteAsync(token, tgId, tgUsername)` — транзакция:
   - Найти клиента по токену
   - Проверить: токен существует, не истёк, не активирован
   - UPDATE clients SET parent_tg_id, parent_tg_username, invite_activated_at
   - INSERT INTO user_roles (tg_id, 'parent', client_id) ON CONFLICT UPDATE
2. Ответ: приветствие с именем ребёнка или сообщение об ошибке

**Ошибки:**
- Токен не найден: "Ссылка недействительна. Обратитесь к администратору."
- Токен истёк: "Срок ссылки истёк. Попросите администратора сгенерировать новую."
- Уже активирован: "Эта ссылка уже была использована."

---

### ParentCommandsHandler

**Назначение:** обработка родительских кнопочных команд. Один файл, три метода. НЕ проходит через IntentService — детектируется через inline callback data.

**Методы:**

| Метод | Описание |
|---|---|
| `HandleScheduleAsync(clientId, ct)` | Группы ребёнка с расписанием → JSON → AiService → форматированный ответ |
| `HandleInfoAsync(clientId, ct)` | Данные ребёнка (ФИО, дата рождения, уровень, родители, телефоны) → JSON → AiService |
| `HandleSubscriptionAsync(clientId, ct)` | Последний абонемент (месяц, кол-во занятий, цена, получатель) или null → JSON → AiService |

Все три метода: DatabaseService → JSON → AiService.FormatParentResponseAsync → HandlerResult.

---

## Services

### IntentService

**`ClassifyAsync(userMessage, ct)`**
- Вызывает Claude API (Haiku) с системным промптом из `IntentPrompts.GetIntentSystemPrompt()`
- Возвращает `IntentResult(string Intent, Dictionary<string, string?> Params)`
- При ошибке API — fallback на `analytics`

**`ClassifyAdditionalDataAsync(userMessage, intent, currentParams, ct)`**
- Вызывается когда менеджер нажал ✏️ и вводит допданные
- Передаёт текущие params черновика как контекст
- Модель извлекает только новые/изменённые поля
- Возвращает `Dictionary<string, string?>` с новыми полями для мёрджа

Оба метода убирают ```json фенсы перед парсингом JSON.

---

### DatabaseService

Все методы используют `Npgsql`, открывают соединение через `await using`, параметризованные запросы.

| Метод | Описание |
|---|---|
| `ExecuteQueryAsync(sql)` | Выполняет SELECT, возвращает JSON-строку. GuardReadOnly блокирует мутации |
| `FindClientByNameAsync(name)` | ILIKE по частям имени + фильтр точных совпадений |
| `CheckDuplicateClientAsync(fullName)` | Exact (LOWER) + fuzzy (pg_trgm) проверка дубля имени |
| `FindClientsByPhoneAsync(phoneDigits)` | Поиск клиентов по цифрам телефона (REGEXP_REPLACE) |
| `CreateClientAsync(record)` | INSERT в clients (с level, parent_name_2, parent_phone_2), возвращает id |
| `FindGroupAsync(day, time, groupId, groupName)` | Поиск группы: каскад groupName → groupId → day+time → day+hour (fallback) |
| `DayOfWeekMapper.MapToEnglish(input)` | Маппинг русских дней недели → английские (fuzzy prefix matching) |
| `DayOfWeekMapper.MapToDayOfWeek(input)` | Маппинг русских дней недели → DayOfWeek enum (fuzzy prefix matching) |
| `IsClientInGroupAsync(clientId, groupId)` | Проверка дубля в group_students |
| `AddClientToGroupAsync(clientId, groupId)` | INSERT в group_students |
| `RemoveClientFromGroupAsync(clientId, groupId)` | UPDATE group_students SET left_at = CURRENT_DATE |
| `HasSubscriptionForMonthAsync(clientId, month)` | Проверка дубля абонемента (без groupId) |
| `CreateSubscriptionAsync(record)` | INSERT в subscriptions (с recipient), инкрементирует subscriptions_count |
| `GetClientActiveGroupsAsync(clientId)` | Активные группы клиента |
| `GetClientFieldsAsync(clientId, fieldNames)` | Чтение указанных полей клиента (whitelist) |
| `UpdateClientFieldsAsync(clientId, fields)` | UPDATE указанных полей (whitelist, параметризованный SQL) |
| `GetUserRoleAsync(tgId)` | SELECT role, client_id FROM user_roles — null если не найден |
| `CreateInviteTokenAsync(clientId, token, expiresAt)` | UPDATE clients SET invite_token, invite_expires_at (перезаписывает) |
| `ActivateInviteAsync(token, tgId, tgUsername)` | Транзакция: найти клиента по токену → валидация → привязка tg → роль parent |
| `GetClientInfoForParentAsync(clientId)` | SELECT full_name FROM clients — для приветствия родителя |
| `GetChildScheduleAsync(clientId)` | Активные группы ребёнка с расписанием (для родительского меню) |
| `GetChildInfoAsync(clientId)` | Данные ребёнка: ФИО, дата рождения, уровень, родители, телефоны, subscriptions_count |
| `GetChildSubscriptionAsync(clientId)` | Последний абонемент (месяц, занятия, цена, получатель) или null |

**Records:**
- `ClientMatch(int Id, string FullName)` — результат поиска клиента
- `DuplicateMatch(int Id, string FullName, bool IsExact)` — результат проверки дубля имени
- `NewClientRecord` — данные клиента (FullName, BirthDate, ParentName, ParentPhone, ParentName2, ParentPhone2, Level)
- `SubscriptionRecord` — данные абонемента (ClientId, Month, TotalLessons, Price, Recipient)
- `GroupInfo` — данные группы (Id, Name, CurrentCount, MaxStudents)
- `ChildScheduleRecord` — расписание (GroupName, DayOfWeek[], TimeStart, Coach, Level)
- `ChildInfoRecord` — данные ребёнка (FullName, BirthDate, Level, ParentName, ParentPhone, ParentName2, ParentPhone2, SubscriptionsCount)
- `ChildSubscriptionRecord` — абонемент (Month, TotalLessons, Price, Recipient)

**GuardReadOnly** — статический метод, бросает исключение если SQL содержит INSERT/UPDATE/DELETE/DROP/etc.

---

### PendingActionService

Хранит состояние в памяти процесса.

**`PendingAction`** — запись:
- `Preview` — текст для показа менеджеру
- `Intent` — тип намерения
- `Params` — словарь параметров
- `Execute` — `Func<CancellationToken, Task<HandlerResult>>`

**`PendingEditState`** — запись с `Intent` и `Params`, хранится пока чат ждёт ввода допданных.

Методы: `Set`, `Take`, `Peek`, `Has`, `SetWaitingForEdit`, `TakeEditState`, `IsWaitingForEdit`.

---

### AiService

Три группы методов:

**Analytics (SQL-флоу):**
- **`GenerateSqlAsync(question, schemaDescription)`** — генерирует SQL по схеме БД.
- **`AnalyzeDataAsync(question, jsonData)`** — форматирует JSON-результат в ответ на русском.
- Оба с авто-retry при SQL ошибке: ошибка PostgreSQL передаётся обратно в модель, максимум 2 попытки.

**Parent (кнопочные команды):**
- **`FormatParentResponseAsync(dataType, rawData, ct)`** — форматирует данные ребёнка в тёплый Telegram-ответ на русском. dataType: "schedule" | "child_info" | "subscription". rawData: JSON из БД.

---

### GoogleSheetsService

Все методы возвращают `bool`. При ошибке Sheets бот не падает, пишет предупреждение в Telegram.

| Метод | Лист | Описание |
|---|---|---|
| `WriteNewClientToJournalAsync(clientId, fullName, birthDate, level, parentName, parentPhone)` | Ученики | Append A-H |
| `WriteSubscriptionAsync(subscriptionId, fullName, monthName, recipient, price, saleDate)` | Абонементы | Append A-F |
| `WriteInviteAsync(clientId, clientFullName, token, deepLink, expiresAt)` | Инвайты | Append A-E |

Spreadsheet ID берётся из `.env`: `GOOGLE_SPREADSHEET_ID_CRM`.

---

## Prompts

### IntentPrompts

**`GetIntentSystemPrompt()`** — системный промпт для классификации. Определяет 8 намерений (analytics, sell_subscription, new_student, add_to_group, edit_student, remove_from_group, transfer_group, generate_invite) + unknown. Правила нормализации имён, формат birth_date, формат групп. Для sell_subscription — извлечение recipient по ключевым словам (Никита/Квитко, Нурсултан/Бурцев). Для new_student — level, parent_name_2, parent_phone_2. Для edit_student — student_name + fields объект с 9 редактируемыми полями. Для remove_from_group — student_name, group_day, group_time. Для transfer_group — student_name, from_group_day/from_group_time, to_group_day/to_group_time.

**`GetAdditionalDataSystemPrompt(intent, currentParamsJson)`** — промпт для извлечения допданных. Передаёт текущий черновик в контекст. Правило: имя после "мама/папа/родитель" = parent_name, не student.

### ChessPrompts

**`GetSqlSystemPrompt()`** — промпт для SQL-генерации. Содержит описание схемы БД v2.0 (5 таблиц: clients, groups, group_students, subscriptions, user_roles). Включает правила для debt-запросов через subscriptions (не payments).

**`GetAnalysisSystemPrompt()`** — промпт для форматирования ответа на русском.

**`GetParentResponsePrompt()`** — промпт для форматирования родительских ответов. Русский язык, тёплый тон, формат дней недели, дат, цен.

**`GetParentResponseUserPrompt(dataType, rawData)`** — пользовательский промпт с типом данных и JSON.

---

## Flows

### Добавление нового клиента (new_student)

```
Менеджер: "новый ученик Попов Богдан 04.11.2019 вт18 начинающий"
    |
IntentService.ClassifyAsync()
    -> intent=new_student
    -> params={last_name:"Попов", first_name:"Богдан", birth_date:"04.11.2019",
               group_1_day:"вторник", group_1_time:"18:00", level:"начинающий"}
    |
TelegramBotService: BuildNewStudentPreviewAsync(params)
    Валидация (до показа кнопок):
    1. Дубль имени: CheckDuplicateClientAsync(fullName)
    2. Дата рождения: парсинг DD.MM.YYYY
    3. Уровень: отображается в превью
    4. Телефон: FormatPhone + FindClientsByPhoneAsync
    5. Родитель 2: отображается если указан
    6. Группы: ResolveGroupLineAsync
    |
PendingActionService.Set(chatId, PendingAction)
    |
Telegram: превью + кнопки OK / Отмена / ✏️
    |
NewStudentHandler.HandleAsync(params)
    1. CreateClientAsync() (с level, parent_name_2, parent_phone_2)
    2. WriteNewClientToJournalAsync()
    3. AddToGroupHandler для каждой группы
```

### Продажа абонемента (sell_subscription)

```
Менеджер: "Айгерим оплатила 15000 за апрель Никите"
    |
IntentService.ClassifyAsync()
    -> intent=sell_subscription
    -> params={student_name:"Айгерим", amount:"15000", period:"апрель 2026", recipient:"Никита Квитко"}
    |
TelegramBotService: BuildSubscriptionPreview(params)
    |
Превью + кнопки OK / Отмена
    |
SubscriptionHandler.HandleAsync(params)
    1. FindClientByNameAsync()
    2. HasSubscriptionForMonthAsync(clientId, month)
    3. CreateSubscriptionAsync() (с recipient, инкремент subscriptions_count)
    4. WriteSubscriptionAsync()
```

### Редактирование ученика (edit_student)

```
Менеджер: "Измени телефон Попова Богдана на 7779998877"
    |
IntentService.ClassifyAsync()
    -> intent=edit_student
    -> params={student_name:"Попов Богдан", fields:"{\"parent_phone\":\"7779998877\"}"}
    |
TelegramBotService: BuildEditStudentPreviewAsync(params)
    1. Десериализация fields из JSON-строки
    2. FindClientByNameAsync(student_name)
    3. GetClientFieldsAsync(clientId, fieldNames) — текущие значения
    4. Валидация каждого поля
    5. Превью: "Телефон родителя: 8 (777) 000-11-22 → 8 (777) 999-88-77"
    |
PendingActionService.Set(chatId, PendingAction)
    |
Telegram: превью + кнопки OK / Отмена
    |
EditStudentHandler.HandleAsync(clientId, fullName, fields)
    1. Парсинг типов (DateOnly, bool, int, string)
    2. UpdateClientFieldsAsync()
```

### Убрать из группы (remove_from_group)

```
Менеджер: "убери Богдана из группы пн 16:00"
    |
IntentService.ClassifyAsync()
    -> intent=remove_from_group
    -> params={student_name:"Попов Богдан", group_day:"понедельник", group_time:"16:00"}
    |
TelegramBotService: BuildRemoveFromGroupPreviewAsync(params)
    1. FindClientByNameAsync(student_name)
    2. FindGroupAsync(day, time, groupId, groupName) — каскад: name → id → day+time → day+hour
    3. IsClientInGroupAsync(clientId, groupId) — ученик в группе?
    |
PendingActionService.Set(chatId, PendingAction)
    |
Telegram: превью + кнопки OK / Отмена
    |
RemoveFromGroupHandler.HandleAsync(clientId, clientName, groupId, groupName)
    1. RemoveClientFromGroupAsync()
```

### Перенос в другую группу (transfer_group)

```
Менеджер: "перенеси Богдана из пн16 в ср18"
    |
IntentService.ClassifyAsync()
    -> intent=transfer_group
    -> params={student_name:"Попов Богдан", from_group_name:"пн16", to_group_name:"ср18",
               from_group_day:"понедельник", from_group_time:"16:00",
               to_group_day:"среда", to_group_time:"18:00"}
    |
TelegramBotService: BuildTransferGroupPreviewAsync(params)
    1. FindClientByNameAsync(student_name)
    2. Исходная группа: указана → FindGroupAsync(name → id → day+time → day+hour), не указана → GetClientActiveGroupsAsync
    3. IsClientInGroupAsync(clientId, fromGroupId) — ученик в исходной?
    4. FindGroupAsync(toDay, toTime, groupName) — целевая (каскад)
    5. Проверка: есть места, ученик не в целевой
    |
PendingActionService.Set(chatId, PendingAction)
    |
Telegram: превью + кнопки OK / Отмена
    |
TransferGroupHandler.HandleAsync(clientId, clientName, fromGroupId, fromGroupName, toGroupId, toGroupName)
    1. RemoveClientFromGroupAsync() — убрать из исходной
    2. AddClientToGroupAsync() — добавить в целевую
```

### Генерация инвайта (generate_invite)

```
Админ: "инвайт для Иванова"
    |
IntentService.ClassifyAsync()
    -> intent=generate_invite
    -> params={student_name:"Иванов Артём"}
    |
TelegramBotService: BuildInvitePreviewAsync(params)
    1. FindClientByNameAsync(student_name) — 0/1/2+ логика
    |
PendingActionService.Set(chatId, PendingAction)
    |
Telegram: превью + кнопки OK / Отмена
    |
InviteHandler.HandleAsync(clientId, clientFullName, botUsername)
    1. Guid.NewGuid() -> токен
    2. CreateInviteTokenAsync() -> clients.invite_token, invite_expires_at
    3. Deep link: https://t.me/{botUsername}?start={token}
    4. WriteInviteAsync() -> Sheets «Инвайты» (non-critical)
    5. Возвращает ссылку в чат
```

### Онбординг родителя (/start TOKEN)

```
Родитель переходит по t.me/bot?start=abc123...
    |
TelegramBotService: детектирует /start TOKEN (до проверки роли)
    |
ParentOnboardingHandler.HandleAsync(token, tgId, tgUsername)
    |
DatabaseService.ActivateInviteAsync(token, tgId, tgUsername) — транзакция:
    1. SELECT client по invite_token
    2. Проверка: существует? не истёк? не активирован?
    3. UPDATE clients SET parent_tg_id, parent_tg_username, invite_activated_at
    4. INSERT INTO user_roles (tg_id, 'parent', client_id) ON CONFLICT UPDATE
    |
Успех: "Привет! Я нашёл вашего ребёнка — {имя}."
Ошибка: сообщение по типу (not_found / expired / already_activated)
```

---

### Родительские команды (parent_* callbacks)

```
Родитель отправляет любое текстовое сообщение
    |
TelegramBotService: SendParentMenu()
    -> InlineKeyboardMarkup: 📅 Расписание / 📋 Данные ребёнка / 💳 Абонемент
    |
Родитель нажимает кнопку (callback_data: parent_schedule | parent_info | parent_subscription)
    |
HandleParentCallbackAsync(tgId, data)
    ├── GetUserRoleAsync(tgId) → проверка роли parent, извлечение clientId
    ├── parent_schedule     → ParentCommandsHandler.HandleScheduleAsync(clientId)
    ├── parent_info         → ParentCommandsHandler.HandleInfoAsync(clientId)
    └── parent_subscription → ParentCommandsHandler.HandleSubscriptionAsync(clientId)
    |
Каждый метод:
    1. DatabaseService.GetChild*Async(clientId) → данные из БД
    2. Serialize to JSON
    3. AiService.FormatParentResponseAsync(dataType, json) → тёплый ответ на русском
    |
Ответ в Telegram + повторная отправка меню кнопок
```

---

### Добавление данных к черновику (✏️ edit flow)

```
Менеджер нажимает ✏️
    |
PendingActionService.SetWaitingForEdit(chatId, intent, params)
    |
Менеджер: "мама Иванова Светлана 7771234567"
    |
IntentService.ClassifyAdditionalDataAsync(input, intent, currentParams)
    -> {"parent_name":"Иванова Светлана","parent_phone":"7771234567"}
    |
Мёрдж: mergedParams = currentParams + newFields
    |
Пересборка превью -> новый PendingAction
```

---

## Принятые решения

| Решение | Причина |
|---|---|
| PendingAction в памяти | Один инстанс, stateful, простота. При рестарте — теряется |
| Sheets после БД | При недоступности Sheets данные не теряются |
| GuardReadOnly через regex | Простая защита от AI-мутаций без ORM |
| Claude Haiku для всех вызовов | Скорость + стоимость |
| Промпты на английском | Лучшее качество извлечения, стабильнее JSON |
| Валидация на этапе превью | Блокировка дублей/ошибок до нажатия OK |
| FormatPhone нормализует телефон | `8 (XXX) XXX-XX-XX` — единообразие |
| pg_trgm fallback | Fuzzy-поиск работает если расширение есть, молча пропускается если нет |
| Абонемент = оплата | Нет отдельной таблицы payments, subscriptions.recipient хранит получателя |

---

## Локальная инфраструктура

| Компонент | Как запущен | Адрес |
|-----------|------------|-------|
| PostgreSQL | Homebrew (brew services) | localhost:5432 |
| ChessCrm.Bot | dotnet run / IDE | — |
| Metabase | Docker (docker compose up -d) | http://localhost:3000 |

Metabase docker-compose.yml: `~/chess-automation/metabase/docker-compose.yml`
