# Sprint Plan — Шахматная школа #1
_Версия 2.0 | 11.03.2026 | alpha v3_

---

## Участники и роли

| Кто | Роль |
|-----|------|
| **Владелец** | Бизнес-логика, ревью, деплой, контакт с менеджером |
| **Claude** | Генерирует код, промпты, SQL; помогает в ревью и дебаге |

---

## Два независимых приложения

| Приложение | Назначение | Статус |
|-----------|-----------|--------|
| **ChessCrm.Bot** | Telegram-бот (AI + запись данных + Sheets) | Активная разработка |
| **Скрипт миграции** | Одноразовый импорт из старых Sheets/CSV -> PostgreSQL | Отдельный разработчик |

---

## ✅ Спринт 0 — Завершён

**Цель:** миграция данных и готовый репозиторий

| Задача | Статус |
|--------|--------|
| Скрипт миграции Sheets -> PostgreSQL | ✅ |
| Миграция клиентов, расписания, групп | ✅ |
| Аналитический Telegram-бот (read-only) | ✅ |
| Авто-retry SQL с передачей ошибки PostgreSQL | ✅ |
| Guard ReadOnly — защита от мутирующих запросов | ✅ |
| Белый список пользователей бота | ✅ |
| Репозиторий на GitHub, README | ✅ |

---

## ✅ Спринт 1 — Завершён

**Цель:** перевод на Claude API, первая команда записи

| Задача | Статус |
|--------|--------|
| Переход с LM Studio на Claude API (Haiku) | ✅ |
| Промпты переписаны на английский | ✅ |
| IntentService — классификация намерений | ✅ |
| Роутинг в TelegramBotService по намерению | ✅ |
| SubscriptionHandler — оформить абонемент | ✅ |
| DatabaseService — FindClientByName, HasSubscriptionForMonth, CreateSubscription | ✅ |

---

## ✅ Спринт 2 — Завершён

**Цель:** основные команды записи, подтверждение, синхронизация с Sheets

| Задача | Статус |
|--------|--------|
| NewStudentHandler | ✅ |
| GoogleSheetsService.WriteNewClientToJournalAsync | ✅ |
| AddToGroupHandler — запись в группу (только PostgreSQL) | ✅ |
| PendingActionService — превью + кнопки OK/Отмена | ✅ |
| ResolveGroupLineAsync — валидация группы до превью | ✅ |
| Нормализация падежей в промпте намерения | ✅ |
| Фильтр точных совпадений в FindClientByNameAsync | ✅ |

---

## ✅ Упрощение бота — Завершено

**Цель:** сократить до 3 команд записи + аналитика, одна Google Sheets таблица

| Задача | Статус |
|--------|--------|
| MigrationTool вынесен в отдельный проект | ✅ |
| DatabaseService обновлён под v2 схему | ✅ |
| Упрощение до одной Google Sheets таблицы (CRM) | ✅ |
| SubscriptionHandler (абонемент в БД + Sheets) | ✅ |
| AddToGroupHandler — запись в группу, только БД, без Sheets | ✅ |
| IntentPrompts — 4 интента (analytics, sell_subscription, new_student, add_to_group) | ✅ |
| Документация обновлена | ✅ |

---

## ✅ Валидация new_student — Завершено

**Цель:** проверки до кнопки ОК, чтобы дубли/ошибки не попадали в БД

| Задача | Статус |
|--------|--------|
| CheckDuplicateClientAsync — exact (LOWER) + fuzzy (pg_trgm) | ✅ |
| Exact дубль имени -> блокирует превью | ✅ |
| Fuzzy дубль имени -> warning в превью | ✅ |
| Дата рождения в будущем -> блокирует | ✅ |
| Возраст < 3 -> warning | ✅ |
| FormatPhone -> 8 (XXX) XXX-XX-XX | ✅ |
| FindClientsByPhoneAsync — info о совпадении телефона | ✅ |
| ✏️ edit -> ре-валидация автоматически | ✅ |

---

## ✅ Чистая схема v2.0 — Завершено

**Цель:** убрать payments, упростить subscriptions, добавить новые поля

| Задача | Статус |
|--------|--------|
| Таблица payments удалена из схемы и кода | ✅ |
| subscriptions: убран group_id, добавлен recipient | ✅ |
| clients: добавлены level, subscriptions_count, parent_name_2, parent_phone_2 | ✅ |
| UNIQUE subscriptions = (client_id, month) | ✅ |
| Создан scripts/reset_database.sql | ✅ |
| Обновлены все промпты под v2.0 | ✅ |
| Обновлена документация | ✅ |

---

## ✅ Metabase — Завершено

**Цель:** локальный просмотр данных PostgreSQL без разработки фронта

| Задача | Статус |
|--------|--------|
| Docker Desktop установлен | ✅ |
| docker-compose.yml для Metabase | ✅ |
| Подключение к chess_crm через host.docker.internal | ✅ |

---

## ✅ EditStudentHandler — Завершено

**Цель:** редактирование данных существующего ученика через естественный язык (только PostgreSQL, без Sheets)

| Задача | Статус |
|--------|--------|
| IntentPrompts — добавлен интент edit_student (5 интентов) | ✅ |
| DatabaseService — GetClientFieldsAsync, UpdateClientFieldsAsync (whitelist 9 полей) | ✅ |
| EditStudentHandler — парсинг типов, обновление БД | ✅ |
| TelegramBotService — роутинг edit_student, превью old→new, валидация | ✅ |
| Program.cs — регистрация EditStudentHandler в DI | ✅ |
| Интеграционный тест (Tests/) | ✅ |

---

## ✅ Групповые операции (alpha v3) — Завершено

**Цель:** переименование enroll → add_to_group, новые интенты remove_from_group и transfer_group

| Задача | Статус |
|--------|--------|
| Переименование: EnrollHandler → AddToGroupHandler, EnrollClientInGroupAsync → AddClientToGroupAsync | ✅ |
| Переименование: интент "enroll" → "add_to_group" во всех промптах и роутинге | ✅ |
| IntentPrompts — добавлены интенты remove_from_group и transfer_group (7 интентов) | ✅ |
| DatabaseService — RemoveClientFromGroupAsync | ✅ |
| RemoveFromGroupHandler — убрать ученика из группы, превью + подтверждение | ✅ |
| TransferGroupHandler — перенос: remove + add, обработка частичного сбоя | ✅ |
| TelegramBotService — роутинг, BuildRemoveFromGroupPreviewAsync, BuildTransferGroupPreviewAsync | ✅ |
| Program.cs — регистрация RemoveFromGroupHandler и TransferGroupHandler в DI | ✅ |

---

## 🔜 Спринт 3 — Миграция данных + Тестирование

### Блок 0 — Миграция данных (отдельный проект)

| Задача | Детали |
|--------|--------|
| Импорт из старой CRM (CSV) | Одноразовый, запускается руками |
| Импорт из старых Google Sheets | Объединение с дедупликацией |
| Проставить is_active | Активные = записаны в текущую группу |
| Создать таблицу user_roles | DDL вручную |
| pg_trgm | `CREATE EXTENSION pg_trgm` + GIN индекс на clients.full_name |

### Блок 1 — Тестирование 7 команд + аналитика

| Задача | Детали |
|--------|--------|
| Тестирование new_student | Создание ученика -> БД + Sheets |
| Тестирование add_to_group | Запись в группу -> БД |
| Тестирование sell_subscription | Абонемент -> БД + Sheets |
| Тестирование edit_student | Редактирование ученика -> БД |
| Тестирование remove_from_group | Убрать из группы -> БД |
| Тестирование transfer_group | Перенос из группы в группу -> БД |
| Тестирование аналитики | Вопросы по данным -> SQL -> ответ |
| Доработка промпта | По итогам теста |

---

## ✅ Спринт 4 — Роли + Инвайты — Завершено

**Цель:** роли пользователей, генерация инвайтов, онбординг родителей

| Задача | Статус |
|--------|--------|
| Проверка роли по tg_id на каждый входящий апдейт (user_roles) | ✅ |
| ALLOWED_TELEGRAM_USER_IDS удалён — заменён user_roles | ✅ |
| InviteHandler — генерация инвайт-ссылки через AI intent (generate_invite) | ✅ |
| ParentOnboardingHandler — `/start TOKEN` -> привязка tg_id -> роль parent | ✅ |
| DatabaseService — GetUserRoleAsync, CreateInviteTokenAsync, ActivateInviteAsync, GetClientInfoForParentAsync | ✅ |
| GoogleSheetsService — WriteInviteAsync (лист Инвайты) | ✅ |
| IntentPrompts — добавлен интент generate_invite (8 интентов) | ✅ |
| Родитель получает stub "Функционал в разработке" | ✅ |

---

## ✅ Спринт 5 — Родительские команды — Завершено

**Цель:** кнопочное меню для родителей — расписание, данные ребёнка, абонемент

| Задача | Статус |
|--------|--------|
| Родительское меню (3 inline-кнопки) вместо stub | ✅ |
| DatabaseService — GetChildScheduleAsync, GetChildInfoAsync, GetChildSubscriptionAsync + 3 records | ✅ |
| AiService.FormatParentResponseAsync — AI-форматирование данных для родителя | ✅ |
| ChessPrompts.GetParentResponsePrompt — промпт форматирования | ✅ |
| ParentCommandsHandler — 3 метода (расписание, данные, абонемент) | ✅ |
| TelegramBotService — SendParentMenu, HandleParentCallbackAsync, повторная отправка меню после ответа | ✅ |
| Program.cs — регистрация ParentCommandsHandler в DI | ✅ |

---

## 🔜 Спринт 6 — Деплой

| Задача | Детали |
|--------|--------|
| Docker-compose | Подготовка конфига |
| Деплой на Vdsina | Владелец — сам |
| Деплой Metabase на Vdsina рядом с ботом | docker-compose с ботом + Metabase |
| Smoke test на проде | Владелец |

---

## Где Claude наиболее полезен

| Задача | Почему эффективно |
|--------|-------------------|
| Промпты для SQL-генерации | Знает схему БД проекта |
| Промпты для intent detection + нормализации | Знает паттерны проекта |
| Генерация Handler-ов по описанию | Знает архитектуру и соглашения кода |
| Дебаг по stack trace + логам | Вставляй лог — разберём |
| Обновление документации | Актуализирует по факту сделанного |

## Что всегда остаётся за владельцем

| Задача | Почему |
|--------|--------|
| Деплой и SSH | Секреты и учётные данные |
| `.env` с ключами | То же |
| Проверка данных после миграции | Только ты знаешь что правда |
| Контакт с менеджером и родителями | Человеческий процесс |
| Рассылка инвайт-ссылок | Личный контакт |
| Удаление записей | Бот не удаляет, только ты |
