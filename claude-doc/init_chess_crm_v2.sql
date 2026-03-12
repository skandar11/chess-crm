-- =====================================================
-- chess_crm v2.0 — Схема БД
-- =====================================================

-- Включаем pg_trgm для fuzzy-поиска
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- =====================================================
-- Таблица: clients
-- =====================================================
CREATE TABLE clients (
    id                  SERIAL PRIMARY KEY,
    full_name           TEXT NOT NULL,
    birth_date          DATE,
    parent_name         TEXT,
    parent_phone        TEXT,
    parent_name_2       TEXT,
    parent_phone_2      TEXT,
    parent_tg_id        BIGINT,
    parent_tg_username  TEXT,
    invite_token        TEXT,
    invite_expires_at   TIMESTAMPTZ,
    invite_activated_at TIMESTAMPTZ,
    is_active           BOOLEAN NOT NULL DEFAULT TRUE,
    level               TEXT,
    subscriptions_count INT NOT NULL DEFAULT 0,
    notes               TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =====================================================
-- Таблица: groups
-- =====================================================
CREATE TABLE groups (
    id            SERIAL PRIMARY KEY,
    name          TEXT NOT NULL,
    day_of_week   TEXT[] NOT NULL,
    time_start    TIME NOT NULL,
    coach         TEXT,
    level         TEXT NOT NULL,
    max_students  INT NOT NULL DEFAULT 8,
    is_active     BOOLEAN NOT NULL DEFAULT TRUE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =====================================================
-- Таблица: group_students
-- =====================================================
CREATE TABLE group_students (
    id          SERIAL PRIMARY KEY,
    client_id   INT NOT NULL REFERENCES clients(id),
    group_id    INT NOT NULL REFERENCES groups(id),
    joined_at   DATE NOT NULL DEFAULT CURRENT_DATE,
    left_at     DATE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    UNIQUE (client_id, group_id, joined_at)
);

-- =====================================================
-- Таблица: subscriptions
-- =====================================================
CREATE TABLE subscriptions (
    id              SERIAL PRIMARY KEY,
    client_id       INT NOT NULL REFERENCES clients(id),
    month           DATE NOT NULL,
    total_lessons   INT NOT NULL DEFAULT 8,
    price           NUMERIC(10,2) NOT NULL,
    recipient       TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    
    UNIQUE (client_id, month)
);

-- =====================================================
-- Таблица: user_roles
-- =====================================================
CREATE TABLE user_roles (
    tg_id       BIGINT PRIMARY KEY,
    role        TEXT NOT NULL,
    client_id   INT REFERENCES clients(id),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- =====================================================
-- Индексы
-- =====================================================

-- Fuzzy-поиск по именам
CREATE INDEX idx_clients_full_name_trgm 
    ON clients USING GIN (full_name gin_trgm_ops);

-- Активные записи в группы
CREATE INDEX idx_group_students_client 
    ON group_students (client_id) 
    WHERE left_at IS NULL;

-- Абонементы по клиенту и месяцу
CREATE INDEX idx_subscriptions_client_month 
    ON subscriptions (client_id, month);

-- =====================================================
-- Готово
-- =====================================================
