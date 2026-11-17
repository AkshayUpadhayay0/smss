-- =====================================================================================================================
-- SMSS  -  Fee line removal log                                                              (RUN-ME, idempotent)
--
-- WHAT IT DOES
--   Removing a fee line from a fee structure deletes the UNPAID dues of the students already on it (and switches off items that have a payment). The API now asks for a confirmation first
--   (it shows the counts and amounts, past and future) and writes ONE row per confirmed removal to the new table tb_fee_line_removal_log: school, structure, fee head (name copied, the line is
--   gone afterwards), who (created_by), when, items deactivated / removed, unpaid dues deleted (past / future, count and amount), paid dues kept.
--   This file only creates that table. It changes no existing table and no existing row.
--
-- BEFORE YOU RUN IT
--   1. Run Docs/smss-fee-effective-from-RUN-ME.sql first (it must already be applied; this file does not depend on it, but the API build that needs both expects both).
--   2. Take a pg_dump. Run in the Query Tool of the right database; it prints "Connected to database ..." first (ROLLBACK if it is not the one you meant).
--   Deploy the API build that maps this table AFTER running this file, not before (a removal would fail on a missing table).
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and says "ALREADY MIGRATED: nothing changed".
-- ROLLBACK
--   DROP TABLE public.tb_fee_line_removal_log;   (nothing else refers to it)
-- =====================================================================================================================

BEGIN;

DO $pre$
DECLARE t text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';
    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_users', 'tb_fee_structures', 'tb_fee_types'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist. This is not the database this script is meant for. Nothing was changed.', t;
        END IF;
    END LOOP;
    RAISE NOTICE 'Pre-checks passed.';
END
$pre$;

CREATE TEMP TABLE _rl_state (migrating boolean NOT NULL) ON COMMIT DROP;
INSERT INTO _rl_state SELECT to_regclass('public.tb_fee_line_removal_log') IS NULL;

CREATE TABLE IF NOT EXISTS public.tb_fee_line_removal_log (
    removal_log_id       BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    school_id            VARCHAR(100)  NOT NULL REFERENCES public.tb_schools (school_id),
    fee_structure_id     BIGINT        NOT NULL REFERENCES public.tb_fee_structures (fee_structure_id),
    fee_type_id          BIGINT        NOT NULL,                                       -- no FK: the line is gone, the fee type itself may be removed later
    fee_type_name        VARCHAR(150)  NOT NULL,
    items_deactivated    INTEGER       NOT NULL DEFAULT 0,                             -- student items with a payment: kept but switched off
    items_removed        INTEGER       NOT NULL DEFAULT 0,                             -- student items with no payment: removed with their dues
    past_dues_deleted    INTEGER       NOT NULL DEFAULT 0,                             -- unpaid dues dated before the removal day
    past_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    future_dues_deleted  INTEGER       NOT NULL DEFAULT 0,                             -- unpaid dues dated on or after the removal day
    future_amount        NUMERIC(14,2) NOT NULL DEFAULT 0,
    paid_dues_kept       INTEGER       NOT NULL DEFAULT 0,                             -- dues with a payment: never touched
    created_by           BIGINT        NOT NULL REFERENCES public.tb_users (user_id),
    created_at           TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT chk_tb_fee_line_removal_log_counts CHECK (items_deactivated >= 0 AND items_removed >= 0 AND past_dues_deleted >= 0 AND future_dues_deleted >= 0 AND paid_dues_kept >= 0
                                                         AND past_amount >= 0 AND future_amount >= 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_fee_line_removal_log_school ON public.tb_fee_line_removal_log (school_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_tb_fee_line_removal_log_structure ON public.tb_fee_line_removal_log (fee_structure_id);

-- ---------------------------------------------------------------------------------------------------------------------
-- VERIFICATION (same transaction). Any failure RAISES, which rolls everything back.
-- ---------------------------------------------------------------------------------------------------------------------
DO $verify$
DECLARE
    v_cols text;
BEGIN
    SELECT string_agg(c, ', ') INTO v_cols FROM unnest(ARRAY['removal_log_id', 'school_id', 'fee_structure_id', 'fee_type_id', 'fee_type_name', 'items_deactivated', 'items_removed',
                                                            'past_dues_deleted', 'past_amount', 'future_dues_deleted', 'future_amount', 'paid_dues_kept', 'created_by', 'created_at']) c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_line_removal_log' AND column_name = c AND is_nullable = 'NO');
    IF v_cols IS NOT NULL THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: tb_fee_line_removal_log is missing, or has nullable, column(s): %. Rolled back.', v_cols;
    END IF;

    IF (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_fee_line_removal_log'::regclass AND contype = 'f') < 3 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: tb_fee_line_removal_log is missing a foreign key (school, structure, user). Rolled back.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_fee_line_removal_log'::regclass AND conname = 'chk_tb_fee_line_removal_log_counts') THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: the counts check constraint is missing. Rolled back.';
    END IF;

    IF (SELECT migrating FROM _rl_state) THEN
        IF (SELECT count(*) FROM public.tb_fee_line_removal_log) <> 0 THEN
            RAISE EXCEPTION 'VERIFICATION FAILED: the new log table is not empty right after creation. Rolled back.';
        END IF;
        RAISE NOTICE 'VERIFIED: tb_fee_line_removal_log created (empty). No existing table or row was changed.';
    ELSE
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed. (structure verified; % log row(s) so far)', (SELECT count(*) FROM public.tb_fee_line_removal_log);
    END IF;
END
$verify$;

COMMIT;

SELECT count(*) AS removal_log_rows FROM public.tb_fee_line_removal_log;
