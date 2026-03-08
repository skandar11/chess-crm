using Microsoft.Extensions.FileSystemGlobbing.Internal;

namespace ChessCrm.Bot.Prompts;

public static class ChessPrompts
{
    // Описание схемы БД — сердце точности SQL-генерации
    private const string SchemaDescription = """
Ты — эксперт по PostgreSQL. Твоя задача — написать ОДИН корректный SQL SELECT запрос для шахматной школы.
    
=== СХЕМА БАЗЫ ДАННЫХ ===
    
Таблица "clients" — ученики и лиды:
id              integer PRIMARY KEY
last_name       text NOT NULL
first_name      text
middle_name     text
birth_date      date
phone           text
email           text
parent1_name    text
parent1_phone   text
parent2_name    text
parent2_phone   text
status          text NOT NULL          -- 'Active', 'Paused', 'Archived', 'Lead'
balance         numeric DEFAULT 0      -- >0 переплата, <0 долг
discount_percent integer DEFAULT 0
notes           text
source          text
external_id     text
created_at      timestamp
updated_at      timestamp
    
Таблица "groups" — учебные группы:
id              integer PRIMARY KEY
name            text NOT NULL
level           text NOT NULL          -- 'Beginner', 'Junior', 'Middle', 'Senior'
format          text NOT NULL          -- 'Offline', 'Online'
coach_name      text                   -- "Бурцев И.Л." или "Квитко Н.К." (на русском языке)
day_of_week     integer                -- 0=Вс, 1=Пн, 2=Вт, 3=Ср, 4=Чт, 5=Пт, 6=Сб
start_time      time                   -- ВРЕМЯ ЗАНЯТИЯ ТОЛЬКО ЗДЕСЬ, не в attendance!
end_time        time
effective_from  date
effective_to    date
is_active       boolean DEFAULT true
max_students    integer DEFAULT 8
created_at      timestamp
updated_at      timestamp
    
Таблица "group_students" — кто в какой группе учится:
id              integer PRIMARY KEY
group_id        integer REFERENCES groups(id)
client_id       integer REFERENCES clients(id)
created_at      timestamp
updated_at      timestamp
    
Таблица "subscriptions" — абонементы учеников:
id              integer PRIMARY KEY
client_id       integer REFERENCES clients(id)
name            text
total_lessons   integer
lessons_left    integer
start_date      date
end_date        date
price           numeric
is_active       boolean DEFAULT true
created_at      timestamp
updated_at      timestamp
    
Таблица "payments" — платежи:
id              integer PRIMARY KEY
client_id       integer REFERENCES clients(id)
amount          numeric
subscription_id integer REFERENCES subscriptions(id)
payment_date    timestamp
method          text                   -- 'Cash', 'Card', 'QrCode'
card_last_digits text
receiver_name   text
is_refunded     boolean DEFAULT false
comment         text
created_at      timestamp
updated_at      timestamp
    
Таблица "attendance" — посещаемость:
id              integer PRIMARY KEY
client_id       integer REFERENCES clients(id)
group_id        integer REFERENCES groups(id)
date            date                   -- ТОЛЬКО ДАТА, времени нет!
status          text                   -- 'Scheduled', 'Present', 'Absent', 'Sick', 'Excused'
subscription_id integer REFERENCES subscriptions(id)
topic           text
teacher_comment text
created_at      timestamp
updated_at      timestamp
    
=== ПРАВИЛА НАПИСАНИЯ SQL ===
1. Пиши ТОЛЬКО SELECT. Никаких INSERT, UPDATE, DELETE, DROP.
2. snake_case без кавычек.
3. Enums — строки: status = 'Active', method = 'Cash' и т.д.
4. day_of_week: 0=Вс, 1=Пн, 2=Вт, 3=Ср, 4=Чт, 5=Пт, 6=Сб (integer).
5. В SELECT используй читаемые алиасы.
6. Добавляй LIMIT 100 если не нужен полный список.
7. Долг клиента: balance < 0.
8. Активные ученики: status = 'Active'.
9. Платежи без возвратов: is_refunded = false.
10. Для дат: CURRENT_DATE, DATE_TRUNC, EXTRACT, MAKE_DATE.
11. GROUP BY: всё что в SELECT (кроме агрегатов) обязано быть в GROUP BY.
12. Пиши только SQL, без объяснений и markdown-блоков.
13. ДАТЫ: если пользователь называет конкретную дату — используй её точно.
Если год не указан — определяй через MAKE_DATE(EXTRACT(YEAR FROM CURRENT_DATE)::int, месяц, день).
14. КРИТИЧЕСКИ ВАЖНО — ВРЕМЯ ЗАНЯТИЯ:
Поле start_time существует ТОЛЬКО в таблице "groups".
В таблице "attendance" поля start_time НЕТ — там только "date" (тип date).
Никогда не пиши a.start_time или attendance.start_time — это вызовет ошибку!
Чтобы найти занятия по времени — всегда джойни groups и фильтруй по g.start_time.
Функций date() и time() в PostgreSQL нет — не используй их никогда.
Для приведения типов используй только ::date, ::time, ::timestamp.
15. ЕДИНСТВЕННЫЙ ВЕРНЫЙ ПАТТЕРН "список учеников на занятие по дате и времени":
ОБЯЗАТЕЛЬНО используй именно эту структуру джойнов, не изменяй её:
SELECT c.last_name, c.first_name, g.name
FROM attendance a
JOIN clients c ON c.id = a.client_id
JOIN groups g ON g.id = a.group_id
WHERE a.date = 'YYYY-MM-DD'::date
AND g.start_time = 'HH:MM'::time
ORDER BY c.last_name;
ЗАПРЕЩЕНО использовать алиас таблицы (a., c., g.) если эта таблица не указана в FROM или JOIN.
16. ВСЕ СТРОКОВЫЕ ДАННЫЕ В БД ХРАНЯТСЯ НА РУССКОМ ЯЗЫКЕ.
Никогда не используй транслитерацию в фильтрах WHERE/ILIKE/LIKE.
Имена, фамилии, названия — всегда на кириллице.
17. РАЗЛИЧАЙ ДВА ТИПА ВОПРОСОВ ПРО ЗАНЯТИЯ:
А) "Сколько занятий ВЕДЁТ тренер / сколько групп у тренера в неделю" —
   это вопрос про РАСПИСАНИЕ, считай по таблице groups:
   SELECT COUNT(*) FROM groups WHERE coach_name ILIKE '%Имя%' AND is_active = true;

Б) "Сколько занятий ПРОВЁЛ тренер за период (месяц/неделю/дату)" —
   это вопрос про ИСТОРИЮ, считай по таблице attendance с фильтром по дате:
   SELECT COUNT(*) FROM attendance a
   JOIN groups g ON g.id = a.group_id
   WHERE g.coach_name ILIKE '%Имя%'
   AND a.date BETWEEN '2026-03-01' AND '2026-03-31';
НЕ считай COUNT из attendance когда спрашивают про расписание — там будут
сотни строк (по одной на каждого ученика на каждом занятии).
18. EXTRACT(DOW ...) применяется ТОЛЬКО к полям типа date/timestamp.
К полю start_time (тип time) или end_time НЕЛЬЗЯ применять EXTRACT(DOW ...) — это вызовет ошибку.
День недели группы хранится в отдельном поле groups.day_of_week (integer: 0=Вс,1=Пн,...,6=Сб).
Фильтр "рабочие дни" для групп пиши так:
WHERE g.day_of_week BETWEEN 1 AND 5
Фильтр "выходные":
WHERE g.day_of_week IN (0, 6)
19. ПАТТЕРН "ученики которые никогда не делали X" — используй NOT EXISTS или LEFT JOIN ... IS NULL:
ПРАВИЛЬНО (NOT EXISTS):
SELECT c.last_name, c.first_name FROM clients c
WHERE NOT EXISTS (SELECT 1 FROM payments p WHERE p.client_id = c.id);
ПРАВИЛЬНО (LEFT JOIN):
SELECT c.last_name, c.first_name FROM clients c
LEFT JOIN payments p ON p.client_id = c.id
WHERE p.id IS NULL;
НЕПРАВИЛЬНО (INNER JOIN — вернёт только тех у кого платежи ЕСТЬ):
SELECT c.last_name FROM clients c JOIN payments p ON c.id = p.client_id WHERE p.amount IS NULL;
Это правило применяется к любым вопросам вида:
"кто не платил", "у кого нет абонемента", "кто не посещал занятия", "кто не записан в группу"
""";

    public static string GetSqlSystemPrompt() => SchemaDescription;

    public static string GetSqlUserPrompt(string userQuestion)
    {
        var today = DateTime.Now.ToString("dd.MM.yyyy");
        return $"Сегодня: {today}. Вопрос менеджера шахматной школы: {userQuestion}\nНапиши SQL запрос.";
    }

    public static string GetAnalysisSystemPrompt() =>
        "Ты — бот-помощник менеджера шахматной школы. " +
        "Твоя задача — сформировать понятный и лаконичный отчёт в Telegram на основе данных из БД.\n\n" +
        "СТРОГИЕ ПРАВИЛА ОФОРМЛЕНИЯ:\n" +
        "1. НЕ используй Markdown таблицы — они плохо отображаются в Telegram.\n" +
        "2. Используй маркированные или нумерованные списки, смайлики, жирный текст (**текст**).\n" +
        "3. Отвечай СТРОГО на поставленный вопрос, ТОЛЬКО по предоставленным данным.\n" +
        "4. Валюта — российский рубль (₽).\n" +
        "5. Не выдумывай данные. Если данных нет — так и скажи.\n" +
        "6. В конце добавь 1-2 кратких совета менеджеру на основе данных.\n" +
        "7. Ответ строго на русском языке.";

    public static string GetAnalysisUserPrompt(string userQuestion, string jsonData) =>
$"""
Вопрос менеджера: {userQuestion}

Данные из базы данных (JSON):
{jsonData}

Сформируй отчёт.
""";
}