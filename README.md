# ChessCrm.Bot — Telegram-бот для управления шахматной школой

AI-ассистент для менеджера: запись учеников, абонементы, аналитика — через обычный текст в Telegram.

---

## Стек

| Компонент | Технология |
|-----------|-----------|
| Язык | C# / .NET 10 |
| Telegram | Telegram.Bot 22.x |
| AI — все вызовы | Claude API (Haiku) |
| База данных | PostgreSQL 16 |
| Доступ к БД | Raw Npgsql (без ORM) |
| Google Sheets | Google.Apis.Sheets.v4 (зеркало данных) |
| Аналитика | Metabase (Docker, read-only к PostgreSQL) |
| Логирование | Serilog |

---

## Структура проекта

```
chess-crm/
├── ChessCrm.Bot/             # Telegram-бот
│   ├── Configuration/        # AppConfig.cs — чтение .env
│   ├── Handlers/             # NewStudentHandler, SubscriptionHandler, ...
│   ├── Prompts/              # IntentPrompts.cs, ChessPrompts.cs
│   ├── Services/             # TelegramBotService, DatabaseService, AiService, ...
│   ├── Program.cs            # DI-контейнер, запуск
│   └── .env.example          # Шаблон переменных окружения
├── ChessCrm.MigrationTool/   # Одноразовый импорт данных (отдельный проект)
├── Tests/                    # Интеграционные тесты
├── docs/                     # Документация
│   ├── admin-guide.md        # Инструкция для менеджера
│   └── parent-guide.md       # Инструкция для родителей
├── scripts/
│   └── reset_database.sql    # Пересоздание БД с нуля
├── metabase/
│   └── docker-compose.yml    # Metabase для просмотра данных
└── README.md
```

---

## Требования

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 16
- Telegram Bot Token ([@BotFather](https://t.me/BotFather))
- Anthropic API Key ([console.anthropic.com](https://console.anthropic.com))
- Google Service Account с доступом к Sheets API
- Docker Desktop (опционально, для Metabase)

---

## Шаг 1 — Клонирование репозитория

```bash
git clone https://github.com/bogdan-popov/chess-crm.git
cd chess-crm
```

---

## Шаг 2 — База данных

### Mac
```bash
brew install postgresql@16
brew services start postgresql@16
psql postgres -c "CREATE DATABASE chess_crm;"
```

### Создание схемы
```bash
psql chess_crm < scripts/reset_database.sql
```

Скрипт создаёт все таблицы (`clients`, `groups`, `group_students`, `subscriptions`, `user_roles`) и включает `pg_trgm`.

---

## Шаг 3 — Google Sheets API

1. Открой [Google Cloud Console](https://console.cloud.google.com/) → создай проект
2. Включи **Google Sheets API**
3. Создай **Service Account** → скачай JSON ключ
4. Переименуй файл в `google-credentials.json`, положи в `ChessCrm.Bot/`
5. Создай Google Таблицу с четырьмя листами: **Ученики**, **Абонементы**, **Инвайты**, **Группы**
6. Поделись таблицей с email сервисного аккаунта (права редактора)
7. Скопируй ID таблицы из URL: `docs.google.com/spreadsheets/d/`**`ВОТ_ЭТО`**`/edit`

---

## Шаг 4 — Настройка .env

```bash
cd ChessCrm.Bot
cp .env.example .env
```

Открой `.env` и заполни:

```env
TELEGRAM_BOT_TOKEN=1234567890:ABC-токен_от_BotFather
DB_CONNECTION_STRING=Host=localhost;Port=5432;Database=chess_crm;Username=skandar;Password=
ANTHROPIC_API_KEY=sk-ant-...
GOOGLE_CREDENTIALS_PATH=google-credentials.json
GOOGLE_SPREADSHEET_ID_CRM=ID_твоей_таблицы
```

---

## Шаг 5 — Добавить первого администратора

После запуска бота добавь себя как администратора напрямую в БД:

```sql
INSERT INTO user_roles (tg_id, role)
VALUES (твой_telegram_id, 'admin');
```

Свой Telegram ID узнай через [@userinfobot](https://t.me/userinfobot).

Все остальные пользователи без роли получат сообщение «Нет доступа».

---

## Шаг 6 — Запуск бота

```bash
dotnet run --project ChessCrm.Bot
```

В консоли появится:
```
[HH:mm:ss INF] Бот @ИмяБота запущен.
```

---

## Шаг 7 — Metabase (опционально)

Для просмотра данных и дашбордов:

```bash
cd metabase
docker compose up -d
```

Интерфейс: http://localhost:3000  
Подключение к БД: `host.docker.internal:5432`, база `chess_crm`

Metabase только читает данные — не пишет.

---

## Роли пользователей

| Роль | Права | Как получить |
|------|-------|-------------|
| `admin` | Все команды: запись, аналитика, инвайты | Вручную через INSERT в user_roles |
| `parent` | Только свой ребёнок: расписание, данные, абонемент | Через инвайт-ссылку от администратора |

---

## Что умеет бот

Подробное описание команд — в [`docs/admin-guide.md`](docs/admin-guide.md).

Краткий список:
- **Новый ученик** — `Новый ученик Попов Богдан, 2019, мама Иванова 7771234567, вт18`
- **Абонемент** — `Айгерим оплатила 15000 за апрель Никите`
- **Записать в группу** — `Записать Попова в пн16`
- **Убрать из группы** — `Убери Попова из пн16`
- **Перенос** — `Перенеси Попова из пн16 в ср18`
- **Редактирование** — `Измени телефон Попова на 7779998877`
- **Инвайт для родителя** — `Инвайт для Попова`
- **Аналитика** — `Кто не платил в этом месяце?`, `Сколько учеников у Квитко?`

---

## Архитектура

```
Менеджер → Telegram → Claude API (намерение + параметры) → C# бэкенд → PostgreSQL
                                                                  ↓
                                                        Google Sheets (зеркало)
```

PostgreSQL — источник правды.  
Google Sheets — append-only зеркало для быстрого просмотра менеджером.  
Бот **никогда не удаляет** записи из базы.

---

## Возможные проблемы

**`relation "clients" does not exist`**  
→ Схема не создана. Запусти `psql chess_crm < scripts/reset_database.sql`

**`Переменная окружения 'X' не найдена`**  
→ Не заполнен `.env`. Проверь по `.env.example`

**`Нет доступа. Используйте персональную ссылку...`**  
→ Твой `tg_id` не добавлен в `user_roles` как `admin`

**`Google: File not found: google-credentials.json`**  
→ Файл ключа не положен в `ChessCrm.Bot/`

**`Telegram: Unauthorized`**  
→ Неверный Bot Token в `.env`
