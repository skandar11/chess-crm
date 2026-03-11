-- ============================================================
-- Chess CRM v2.0 — Fresh Database Setup
-- Date: 11.03.2026
-- ============================================================
-- USAGE:
--   1. Connect to postgres:  psql -U postgres
--   2. Run this script:      \i reset_database.sql
--   3. Or from terminal:     psql -U postgres -f reset_database.sql
-- ============================================================

-- Step 1: Drop and recreate database (connect to 'postgres' first)
DROP DATABASE IF EXISTS chess_crm;
CREATE DATABASE chess_crm;

-- Step 2: Connect to chess_crm
\connect chess_crm

-- Step 3: Enable pg_trgm for fuzzy search
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- ============================================================
-- TABLES
-- ============================================================

CREATE TABLE clients (
    id                  serial          PRIMARY KEY,
    full_name           text            NOT NULL,
    birth_date          date,
    parent_name         text,
    parent_phone        text,
    parent_name_2       text,
    parent_phone_2      text,
    parent_tg_id        bigint,
    parent_tg_username  text,
    invite_token        text,
    invite_expires_at   timestamptz,
    invite_activated_at timestamptz,
    is_active           bool            NOT NULL DEFAULT true,
    level               text,
    subscriptions_count int             NOT NULL DEFAULT 0,
    notes               text,
    created_at          timestamptz     NOT NULL DEFAULT now()
);

CREATE TABLE groups (
    id            serial          PRIMARY KEY,
    name          text            NOT NULL,
    day_of_week   text[]          NOT NULL,
    time_start    time            NOT NULL,
    coach         text,
    level         text            NOT NULL,
    max_students  int             NOT NULL DEFAULT 8,
    is_active     bool            NOT NULL DEFAULT true,
    created_at    timestamptz     NOT NULL DEFAULT now()
);

CREATE TABLE group_students (
    id          serial          PRIMARY KEY,
    client_id   int             NOT NULL REFERENCES clients(id),
    group_id    int             NOT NULL REFERENCES groups(id),
    joined_at   date            NOT NULL DEFAULT CURRENT_DATE,
    left_at     date,
    created_at  timestamptz     NOT NULL DEFAULT now(),
    UNIQUE (client_id, group_id, joined_at)
);

CREATE TABLE subscriptions (
    id              serial          PRIMARY KEY,
    client_id       int             NOT NULL REFERENCES clients(id),
    month           date            NOT NULL,
    total_lessons   int             NOT NULL DEFAULT 8,
    price           numeric(10,2)   NOT NULL,
    recipient       text,
    created_at      timestamptz     NOT NULL DEFAULT now(),
    UNIQUE (client_id, month)
);

CREATE TABLE user_roles (
    tg_id       bigint          PRIMARY KEY,
    role        text            NOT NULL,
    client_id   int             REFERENCES clients(id),
    created_at  timestamptz     NOT NULL DEFAULT now()
);

-- ============================================================
-- INDEXES
-- ============================================================

CREATE INDEX idx_clients_full_name_trgm ON clients USING GIN (full_name gin_trgm_ops);
CREATE INDEX idx_group_students_client ON group_students (client_id) WHERE left_at IS NULL;
CREATE INDEX idx_subscriptions_client_month ON subscriptions (client_id, month);
