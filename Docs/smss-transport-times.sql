-- =====================================================================================================================
-- SMSS Transport, STAGE D: approximate pickup and drop times per driver-and-area link
--
-- RUN ORDER (important): 1) Docs/smss-transport.sql (Stage 1), 2) Docs/smss-transport-live.sql (Stage 2),
--                        3) Docs/smss-student-transport.sql (Stage 3, student side), 4) THIS script.
-- It refuses to run (with a clear message) when Stage 1 is missing. Safe to run twice: every statement is guarded.
-- (It only needs tb_driver_areas, so it may run before or after Stage 3; the order above is the one used everywhere.)
--
-- Adds to public.tb_driver_areas (only if missing):
--   morning_pickup_time   time without time zone, NULL   approximate time the bus reaches this area in the morning
--   afternoon_drop_time   time without time zone, NULL   approximate time the bus reaches this area in the afternoon
--   ck_tb_driver_areas_times_order  CHECK: when BOTH are set, pickup is earlier than drop (a NULL on either side passes)
--
-- School local time. No time zone, no seconds are used: the API reads and writes "HH:mm". Both are optional.
-- Existing link rows keep NULL in both columns: nothing is backfilled and nothing is rewritten.
-- No new table, no stops, no per-stop or per-student times (these are one approximate time per driver-and-area link).
--
-- Before running on the real database take a backup, e.g.:
--   pg_dump -h localhost -U postgres -Fc -f "E:\SecurityBackup\smss-db-before-transport-times.dump" smss-db
-- (Docs/smss-db*.sql and *.dump are git-ignored; keep backups outside the repository.)
-- Roll back (only if nothing depends on it):
--   ALTER TABLE public.tb_driver_areas DROP CONSTRAINT ck_tb_driver_areas_times_order,
--                                      DROP COLUMN morning_pickup_time, DROP COLUMN afternoon_drop_time;
-- =====================================================================================================================

DO $$
BEGIN
    IF to_regclass('public.tb_driver_areas') IS NULL THEN
        RAISE EXCEPTION 'Stage 1 is missing: run Docs/smss-transport.sql (then Docs/smss-transport-live.sql) first, then this script.';
    END IF;
END $$;

ALTER TABLE public.tb_driver_areas ADD COLUMN IF NOT EXISTS morning_pickup_time time without time zone NULL;
ALTER TABLE public.tb_driver_areas ADD COLUMN IF NOT EXISTS afternoon_drop_time time without time zone NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_driver_areas'::regclass AND conname = 'ck_tb_driver_areas_times_order') THEN
        ALTER TABLE public.tb_driver_areas
            ADD CONSTRAINT ck_tb_driver_areas_times_order
            CHECK (morning_pickup_time IS NULL OR afternoon_drop_time IS NULL OR morning_pickup_time < afternoon_drop_time);
    END IF;
END $$;
