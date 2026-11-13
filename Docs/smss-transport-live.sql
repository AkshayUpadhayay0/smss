-- =====================================================================================================================
-- SMSS Transport, STAGE 2: driver duty sessions and live location
--
-- RUN ORDER (important): Docs/smss-transport.sql (Stage 1: areas, vehicles, drivers, the DRIVER role) MUST be run first,
-- then this script. This script refuses to run (with a clear message) if Stage 1 is missing.
-- Safe to run twice: every statement is IF NOT EXISTS. It creates nothing in any existing table and changes no data.
--
-- Creates:
--   public.tb_driver_duty_sessions  one row per duty session (started_at, ended_at, vehicle snapshot, end_reason, hashed tracking token).
--                                   At most ONE open session per driver (partial unique index); several sessions a day are fine.
--   public.tb_driver_live_location  exactly ONE row per driver: the latest fix only, NO history. Deleted when duty starts and when it ends.
--
-- Status (LIVE / STALE / OFFLINE) is NOT stored: it is computed when read, from received_at (server clock).
-- No coordinates are ever written to tb_driver_duty_sessions.
--
-- Before running on the real database take a backup, e.g.:
--   pg_dump -h localhost -U postgres -Fc -f "E:\SecurityBackup\smss-db-before-transport-live.dump" smss-db
-- (Docs/smss-db*.sql and *.dump are git-ignored; keep backups outside the repository.)
-- Roll back (only if nothing depends on them):  DROP TABLE public.tb_driver_live_location; DROP TABLE public.tb_driver_duty_sessions;
-- =====================================================================================================================

DO $$
BEGIN
    IF to_regclass('public.tb_drivers') IS NULL OR to_regclass('public.tb_vehicles') IS NULL THEN
        RAISE EXCEPTION 'Stage 1 is missing: run Docs/smss-transport.sql first, then this script.';
    END IF;
END $$;

-- ---------------------------------------------------------------------------------------------------------------------
-- Duty sessions
-- ---------------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tb_driver_duty_sessions
(
    session_id           bigint        GENERATED ALWAYS AS IDENTITY,
    school_id            varchar(100)  NOT NULL,
    driver_id            varchar(20)   NOT NULL,
    vehicle_id           bigint        NOT NULL,                  -- the vehicle at the moment duty started (a snapshot: reassigning the driver later does not rewrite history)
    started_at           timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ended_at             timestamptz,
    end_reason           varchar(20),                             -- DRIVER_ENDED | AUTO_TIMEOUT | DRIVER_DEACTIVATED | SESSION_EXPIRED
    tracking_token_hash  char(64)      NOT NULL,                  -- SHA-256 (hex) of the tracking token; the token itself is never stored
    created_at           timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_driver_duty_sessions_pkey PRIMARY KEY (session_id),
    CONSTRAINT uq_tb_driver_duty_sessions_school_session UNIQUE (school_id, session_id),   -- target of the composite foreign key below
    CONSTRAINT fk_tb_driver_duty_sessions_driver  FOREIGN KEY (school_id, driver_id)  REFERENCES public.tb_drivers  (school_id, driver_id),
    CONSTRAINT fk_tb_driver_duty_sessions_vehicle FOREIGN KEY (school_id, vehicle_id) REFERENCES public.tb_vehicles (school_id, vehicle_id),
    CONSTRAINT chk_tb_driver_duty_sessions_ended  CHECK ((ended_at IS NULL) = (end_reason IS NULL) AND (ended_at IS NULL OR ended_at >= started_at)),
    CONSTRAINT chk_tb_driver_duty_sessions_reason CHECK (end_reason IS NULL OR end_reason IN ('DRIVER_ENDED', 'AUTO_TIMEOUT', 'DRIVER_DEACTIVATED', 'SESSION_EXPIRED')),
    CONSTRAINT chk_tb_driver_duty_sessions_token  CHECK (tracking_token_hash ~ '^[0-9a-f]{64}$')
);

-- one OPEN session per driver, enforced by the database (two starts at once: one wins, the other is told)
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_driver_duty_one_open_per_driver ON public.tb_driver_duty_sessions (driver_id) WHERE ended_at IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_driver_duty_token_hash          ON public.tb_driver_duty_sessions (tracking_token_hash);
CREATE INDEX        IF NOT EXISTS idx_tb_driver_duty_school_started     ON public.tb_driver_duty_sessions (school_id, started_at DESC);
CREATE INDEX        IF NOT EXISTS idx_tb_driver_duty_vehicle            ON public.tb_driver_duty_sessions (vehicle_id);

-- ---------------------------------------------------------------------------------------------------------------------
-- Latest location: one row per driver, updated in place
-- The write is  INSERT ... ON CONFLICT (driver_id) DO UPDATE ... WHERE live.session_id = EXCLUDED.session_id AND live.recorded_at < EXCLUDED.recorded_at
-- so an out-of-order (older) fix never overwrites a newer one.
-- ---------------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tb_driver_live_location
(
    driver_id    varchar(20)       NOT NULL,                      -- ONE row per driver
    school_id    varchar(100)      NOT NULL,
    session_id   bigint            NOT NULL,
    vehicle_id   bigint            NOT NULL,
    latitude     double precision  NOT NULL,
    longitude    double precision  NOT NULL,
    accuracy_m   real,                                            -- the three below are optional (a device may not report them)
    speed_mps    real,
    heading_deg  real,
    recorded_at  timestamptz       NOT NULL,                      -- the DEVICE clock: used only to order fixes, never to decide LIVE / STALE
    received_at  timestamptz       NOT NULL DEFAULT CURRENT_TIMESTAMP,   -- the SERVER clock: LIVE / STALE / OFFLINE are computed from this
    CONSTRAINT tb_driver_live_location_pkey PRIMARY KEY (driver_id),
    CONSTRAINT fk_tb_driver_live_location_driver  FOREIGN KEY (school_id, driver_id)  REFERENCES public.tb_drivers (school_id, driver_id),
    CONSTRAINT fk_tb_driver_live_location_session FOREIGN KEY (school_id, session_id) REFERENCES public.tb_driver_duty_sessions (school_id, session_id),
    CONSTRAINT chk_tb_driver_live_location_lat CHECK (latitude  BETWEEN -90  AND 90),
    CONSTRAINT chk_tb_driver_live_location_lng CHECK (longitude BETWEEN -180 AND 180),
    CONSTRAINT chk_tb_driver_live_location_acc CHECK (accuracy_m  IS NULL OR accuracy_m  >= 0),
    CONSTRAINT chk_tb_driver_live_location_spd CHECK (speed_mps   IS NULL OR speed_mps   >= 0),
    CONSTRAINT chk_tb_driver_live_location_hdg CHECK (heading_deg IS NULL OR (heading_deg >= 0 AND heading_deg < 360))
);

CREATE INDEX IF NOT EXISTS idx_tb_driver_live_location_school  ON public.tb_driver_live_location (school_id);
CREATE INDEX IF NOT EXISTS idx_tb_driver_live_location_session ON public.tb_driver_live_location (school_id, session_id);
