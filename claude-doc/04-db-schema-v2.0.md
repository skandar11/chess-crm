# Схема БД v2.0 — chess_crm
_Версия 2.0 | 11.03.2026_

Чистая схема v2.0. Таблица payments удалена — абонемент = факт оплаты.

---

## clients

```sql
clients
  id                  serial          PK
  full_name           text            NOT NULL        -- поиск через pg_trgm
  birth_date          date
  parent_name         text                            -- ФИО родителя 1
  parent_phone        text
  parent_name_2       text                            -- ФИО родителя 2
  parent_phone_2      text
  parent_tg_id        bigint
  parent_tg_username  text
  invite_token        text
  invite_expires_at   timestamptz                     -- 24ч с момента генерации
  invite_activated_at timestamptz
  is_active           bool            NOT NULL DEFAULT true
  level               text                            -- уровень ученика
  subscriptions_count int             NOT NULL DEFAULT 0  -- инкрементируется при продаже
  notes               text
  created_at          timestamptz     NOT NULL DEFAULT now()
```

**Поиск:** pg_trgm + GIN индекс на `full_name`.

---

## groups

```sql
groups
  id            serial      PK
  name          text        NOT NULL        -- "Пн/Ср 16:30 Начинающие"
  day_of_week   text[]      NOT NULL        -- ['monday', 'wednesday']
  time_start    time        NOT NULL
  coach         text                        -- Квитко Н.К. | Бурцев И.Л.
  level         text        NOT NULL        -- 'beginner' | 'intermediate' | 'advanced'
  max_students  int         NOT NULL DEFAULT 8
  is_active     bool        NOT NULL DEFAULT true
  created_at    timestamptz NOT NULL DEFAULT now()
```

Расписание выводится из `day_of_week + time_start`.

---

## group_students

```sql
group_students
  id          serial  PK
  client_id   int     NOT NULL    FK clients
  group_id    int     NOT NULL    FK groups
  joined_at   date    NOT NULL    DEFAULT CURRENT_DATE
  left_at     date                            -- NULL = активен в группе
  created_at  timestamptz NOT NULL DEFAULT now()

  UNIQUE (client_id, group_id, joined_at)
```

`left_at = NULL` -> ученик активен в группе.
Текущие группы ученика: `WHERE left_at IS NULL` — может быть несколько строк.

---

## subscriptions

```sql
subscriptions
  id              serial          PK
  client_id       int             NOT NULL    FK clients
  month           date            NOT NULL    -- первый день месяца: 2026-04-01
  total_lessons   int             NOT NULL DEFAULT 8
  price           numeric(10,2)   NOT NULL
  recipient       text                        -- кому заплатили (Никита Квитко / Нурсултан Бурцев)
  created_at      timestamptz     NOT NULL DEFAULT now()

  UNIQUE (client_id, month)                   -- один абонемент на ученика на месяц
```

Абонемент = факт оплаты. Отдельная таблица payments не нужна.

---

## user_roles

```sql
user_roles
  tg_id       bigint      PK
  role        text        NOT NULL    -- 'admin' | 'parent'
  client_id   int                     FK clients   -- только для родителя
  created_at  timestamptz NOT NULL DEFAULT now()
```

---

## Индексы

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX idx_clients_full_name_trgm ON clients USING GIN (full_name gin_trgm_ops);
CREATE INDEX idx_group_students_client ON group_students (client_id) WHERE left_at IS NULL;
CREATE INDEX idx_subscriptions_client_month ON subscriptions (client_id, month);
```

---

## Миграция

**Отдельный проект, отдельный разработчик.** ChessCrm.Bot работает с готовой базой и не занимается миграцией.

Скрипт создания чистой БД: `scripts/reset_database.sql`.
