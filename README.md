# ChessCRM — Аналитический Telegram-бот для шахматной школы

Проект состоит из двух частей:
- **ChessCrm.MigrationTool** — одноразовый инструмент миграции данных из Google Sheets в PostgreSQL
- **ChessCrm.Bot** — Telegram-бот с ИИ-аналитикой на базе LM Studio

---

## Требования

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [PostgreSQL 15+](https://www.postgresql.org/download/)
- [LM Studio](https://lmstudio.ai/)
- Google Service Account с доступом к Sheets API
- Telegram Bot Token ([@BotFather](https://t.me/BotFather))

---

## Шаг 1 — Клонирование репозитория
```bash
git clone https://github.com/bogdan-popov/chess-crm.git
cd chess-crm
```

---

## Шаг 2 — Создание базы данных PostgreSQL

### Windows
1. Установи PostgreSQL с [официального сайта](https://www.postgresql.org/download/windows/)
2. Открой **pgAdmin** или **SQL Shell (psql)**
3. Выполни:
```sql
CREATE DATABASE chess_crm;
```

### Mac
```bash
brew install postgresql@15
brew services start postgresql@15
psql postgres -c "CREATE DATABASE chess_crm;"
```

---

## Шаг 3 — Настройка Google Sheets API

1. Открой [Google Cloud Console](https://console.cloud.google.com/)
2. Создай проект → Включи **Google Sheets API**
3. Создай **Service Account** → скачай JSON ключ
4. Переименуй файл в `google-credentials.json`
5. Положи его в папку `ChessCrm.MigrationTool/`
6. В каждой Google-таблице нажми **"Поделиться"** и добавь email сервисного аккаунта с правами редактора

---

## Шаг 4 — Настройка LM Studio

### Установка
**Windows / Mac**: скачай с [lmstudio.ai](https://lmstudio.ai/) и установи

### Загрузка моделей
1. Открой LM Studio → вкладка **Search (🔍)**
2. Найди и скачай **`sqlcoder-7b-2`** (~5GB) или **`openai/gpt-oss-20b`** (~12GB) — генерация SQL
3. Найди и скачай **`gemma-3-4b-it`** (~3GB) — форматирование ответов

### Запуск сервера
1. Перейди на вкладку **Developer (↔️)**
2. В выпадающем списке выбери модель **sqlcoder-7b-2** → нажми **Load**
3. Нажми **Start Server** — сервер запустится на `http://localhost:1234`
4. Запомни точное название модели из правой панели — оно нужно для `.env`

> **Рекомендации по железу:**
> - sqlcoder-7b-2: минимум 8GB RAM (лучше 16GB)
> - gemma-3-4b-it: минимум 6GB RAM
> - Если RAM не хватает на обе — в `.env` можно указать одну модель для обоих параметров

---

## Шаг 5 — Миграция данных (запускается один раз)

### Настройка .env
```bash
# Windows (PowerShell)
cd ChessCrm.MigrationTool
copy .env.example .env

# Mac
cd ChessCrm.MigrationTool
cp .env.example .env
```

Открой `.env` и заполни:
```
DB_CONNECTION_STRING=Host=localhost;Port=5432;Database=chess_crm;Username=postgres;Password=твой_пароль
GOOGLE_CREDENTIALS_PATH=google-credentials.json
GOOGLE_APPLICATION_NAME=ChessCrmMigration
GOOGLE_SPREADSHEET_ID_SCHEDULE=ID_таблицы_расписания
GOOGLE_SPREADSHEET_ID_PAYMENTS=ID_таблицы_платежей
GOOGLE_SPREADSHEET_ID_ATTENDANCE=ID_таблицы_учеников
```

> ID таблицы — часть URL таблицы: `docs.google.com/spreadsheets/d/`**`ВОТ_ЭТО`**`/edit`

### Запуск
```bash
dotnet run --project ChessCrm.MigrationTool
```

При запуске автоматически выполнится:
1. Применение миграций EF Core (создание таблиц)
2. Создание групп
3. Миграция клиентов из Google Sheets
4. Миграция платежей из Google Sheets
5. Запись учеников в группы
6. Генерация тестовых занятий на март 2026

В конце должно появиться:
```
Миграция полностью завершена! База данных готова к работе.
```

> **Повторный запуск безопасен** — каждый шаг проверяет наличие данных и пропускает уже мигрированное.

---

## Шаг 6 — Запуск Telegram-бота

### Получи Bot Token
1. Напиши [@BotFather](https://t.me/BotFather) в Telegram
2. Отправь `/newbot` → следуй инструкциям
3. Скопируй полученный токен

### Узнай свой Telegram User ID
Напиши боту [@userinfobot](https://t.me/userinfobot) — он пришлёт твой ID

### Настройка .env
```bash
# Windows (PowerShell)
cd ChessCrm.Bot
copy .env.example .env

# Mac
cd ChessCrm.Bot
cp .env.example .env
```

Открой `.env` и заполни:
```
TELEGRAM_BOT_TOKEN=1234567890:ABC-токен_от_BotFather
ALLOWED_TELEGRAM_USER_IDS=твой_telegram_id (опционально, ограничивает доступ к боту)

DB_CONNECTION_STRING=Host=localhost;Port=5432;Database=chess_crm;Username=postgres;Password=твой_пароль

SQL_MODEL_API_URL=http://localhost:1234/v1/chat/completions
SQL_MODEL_NAME=sqlcoder-7b-2

CHAT_MODEL_API_URL=http://localhost:1234/v1/chat/completions
CHAT_MODEL_NAME=gemma-3-4b-it
```

> Точные названия моделей скопируй из LM Studio — правая панель в разделе Loaded Models

### Запуск
```bash
dotnet run --project ChessCrm.Bot
```

В консоли должно появиться:
```
[HH:mm:ss INF] Бот @ИмяБота запущен.
```

---

## Шаг 7 — Тестирование бота

Открой своего бота в Telegram и отправь `/start`

**Тестовые вопросы:**
```
Сколько всего учеников в базе?
Кто занимается у тренера Квитко Н.К.?
Список учеников на занятие 9 марта в 16:30
Сколько занятий на неделе ведёт тренер Бурцев И.Л.?
Кто должен денег?
Какие ученики ни разу не платили?
Общая сумма платежей за март 2026
```

---

## Структура проекта
```
chess-crm/
├── ChessCrm.MigrationTool/   # Одноразовая миграция из Google Sheets
│   ├── Configuration/
│   ├── Database/
│   │   ├── Migrations/
│   │   └── Seeders/
│   ├── Entities/
│   ├── Enums/
│   ├── Services/
│   └── .env.example
├── ChessCrm.Bot/             # Telegram-бот аналитики
│   ├── Configuration/
│   ├── Prompts/
│   ├── Services/
│   └── .env.example
├── .gitignore
└── README.md
```

---

## Возможные проблемы

**`relation "clients" does not exist`**
→ Таблицы не созданы. Убедись что `MigrateAsync()` отработал без ошибок при запуске MigrationTool

**`Переменная окружения 'X' не найдена`**
→ Не создан `.env` или не заполнены все поля. Проверь по `.env.example`

**`Google: File not found: google-credentials.json`**
→ Файл ключа не положен в папку `ChessCrm.MigrationTool/`

**`LM Studio connection refused`**
→ Не запущен сервер. Открой LM Studio → Developer → Start Server

**`Telegram: Unauthorized`**
→ Неверный Bot Token в `.env`