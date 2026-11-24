-- =====================================================================================================================
-- RUN-ME: Year continuity, stage 1b = PER-YEAR CONCESSIONS and the RECEIPT FEE-TYPE NAME SNAPSHOT  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/student-year-promotion-design.md (section 3 decision 5, section 5 items 8 and 9), approved 24 Nov 2026
-- Needs: Docs/smss-yc-1a-RUN-ME.sql is NOT required by this script, but both belong to the same release.
--
-- WHAT IT DOES
--   1. tb_student_fee_concessions.academic_year_id (new, NOT NULL, FK to tb_academic_years): a concession applies only to the year it was given for.
--      BACKFILL: the year of the student's enrolment. Pre-check (lists the offenders and raises BEFORE anything changes): a concession row whose student has no enrolment, or
--      enrolments in more than one year, because the right year cannot be guessed.
--      The unique key (student_id, concession_id) becomes (student_id, concession_id, academic_year_id).
--   2. tb_fee_payment_allocations.fee_type_name_snapshot (new, NOT NULL, not blank): the fee type's name at the time of payment, so a later rename never changes an old receipt.
--      BACKFILL: the name the fee type has today (the best that can be known for payments already made).
--   3. Verification (raises, so everything rolls back, on any mismatch): the numbers of concession rows and allocation rows are identical before and after, every concession has a year
--      that equals its student's enrolment year, every allocation has a snapshot equal to its fee type's name, and both objects exist.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-yc-1b.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   3. Stop the API (or at least do not take payments) while it runs.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   Restore the pg_dump, or:
--     ALTER TABLE tb_student_fee_concessions DROP CONSTRAINT uq_tb_student_fee_concessions_student_concession_year;
--     ALTER TABLE tb_student_fee_concessions ADD CONSTRAINT uq_tb_student_fee_concessions_student_concession UNIQUE (student_id, concession_id);   -- only valid while each pair is unique
--     ALTER TABLE tb_student_fee_concessions DROP COLUMN academic_year_id;
--     ALTER TABLE tb_fee_payment_allocations DROP COLUMN fee_type_name_snapshot;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _yc1b_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _yc1b_before (concessions bigint, allocations bigint) ON COMMIT DROP;

DO $yc1b$
DECLARE
    v_mig boolean;
    t     text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_students', 'tb_student_enrollments', 'tb_academic_years', 'tb_student_fee_concessions', 'tb_fee_payment_allocations', 'tb_student_fee_dues', 'tb_student_fee_items', 'tb_fee_types'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist. Nothing was changed.', t;
        END IF;
    END LOOP;

    v_mig := NOT (
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_fee_concessions' AND column_name = 'academic_year_id' AND is_nullable = 'NO')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_student_fee_concessions_student_concession_year' AND conrelid = 'public.tb_student_fee_concessions'::regclass)
        AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_student_fee_concessions_student_concession' AND conrelid = 'public.tb_student_fee_concessions'::regclass)
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payment_allocations' AND column_name = 'fee_type_name_snapshot' AND is_nullable = 'NO'));
    INSERT INTO _yc1b_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;
    INSERT INTO _yc1b_before SELECT (SELECT count(*) FROM public.tb_student_fee_concessions), (SELECT count(*) FROM public.tb_fee_payment_allocations);
    RAISE NOTICE 'Pre-checks will run now.';
END
$yc1b$;

-- pre-check (lists offenders and stops; nothing has been changed yet). Only meaningful while the column does not exist yet.
DO $yc1bpre$
DECLARE
    v_list text;
BEGIN
    IF NOT (SELECT migrating FROM _yc1b_state) THEN RETURN; END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_fee_concessions' AND column_name = 'academic_year_id') THEN RETURN; END IF;

    SELECT string_agg(format('concession row %s (student %s, concession %s): %s enrolment(s)', c.student_fee_concession_id, c.student_id, c.concession_id, n.n), '; ' ORDER BY c.student_fee_concession_id) INTO v_list
    FROM public.tb_student_fee_concessions c
    CROSS JOIN LATERAL (SELECT count(DISTINCT e.academic_year_id) AS n FROM public.tb_student_enrollments e WHERE e.student_id = c.student_id) n
    WHERE n.n <> 1;
    IF v_list IS NOT NULL THEN
        RAISE EXCEPTION 'PRE-CHECK FAILED (nothing was changed): the year of these concessions cannot be told (a student with no enrolment, or with enrolments in several years): %', v_list;
    END IF;
    RAISE NOTICE 'Pre-checks passed: every concession belongs to a student with exactly one enrolment year.';
END
$yc1bpre$;

-- 1. per-year concessions
ALTER TABLE public.tb_student_fee_concessions ADD COLUMN IF NOT EXISTS academic_year_id bigint NULL;

DO $yc1bc$
BEGIN
    IF NOT (SELECT migrating FROM _yc1b_state) THEN RETURN; END IF;
    UPDATE public.tb_student_fee_concessions c
       SET academic_year_id = (SELECT min(e.academic_year_id) FROM public.tb_student_enrollments e WHERE e.student_id = c.student_id)
     WHERE c.academic_year_id IS NULL;
    ALTER TABLE public.tb_student_fee_concessions ALTER COLUMN academic_year_id SET NOT NULL;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_student_fee_concessions_year' AND conrelid = 'public.tb_student_fee_concessions'::regclass) THEN
        ALTER TABLE public.tb_student_fee_concessions ADD CONSTRAINT fk_tb_student_fee_concessions_year FOREIGN KEY (academic_year_id) REFERENCES public.tb_academic_years (academic_year_id);
    END IF;
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_student_fee_concessions_student_concession' AND conrelid = 'public.tb_student_fee_concessions'::regclass) THEN
        ALTER TABLE public.tb_student_fee_concessions DROP CONSTRAINT uq_tb_student_fee_concessions_student_concession;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_student_fee_concessions_student_concession_year' AND conrelid = 'public.tb_student_fee_concessions'::regclass) THEN
        ALTER TABLE public.tb_student_fee_concessions ADD CONSTRAINT uq_tb_student_fee_concessions_student_concession_year UNIQUE (student_id, concession_id, academic_year_id);
    END IF;
END
$yc1bc$;
CREATE INDEX IF NOT EXISTS ix_tb_student_fee_concessions_student_year ON public.tb_student_fee_concessions (student_id, academic_year_id);

-- 2. fee type name snapshot on every payment line
ALTER TABLE public.tb_fee_payment_allocations ADD COLUMN IF NOT EXISTS fee_type_name_snapshot varchar(150) NULL;

DO $yc1bs$
BEGIN
    IF NOT (SELECT migrating FROM _yc1b_state) THEN RETURN; END IF;
    UPDATE public.tb_fee_payment_allocations a
       SET fee_type_name_snapshot = t.fee_type_name
      FROM public.tb_student_fee_dues d
      JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id
      JOIN public.tb_fee_types t ON t.fee_type_id = i.fee_type_id
     WHERE d.fee_due_id = a.fee_due_id AND a.fee_type_name_snapshot IS NULL;
    ALTER TABLE public.tb_fee_payment_allocations ALTER COLUMN fee_type_name_snapshot SET NOT NULL;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_fee_payment_allocations_fee_type_name' AND conrelid = 'public.tb_fee_payment_allocations'::regclass) THEN
        ALTER TABLE public.tb_fee_payment_allocations ADD CONSTRAINT chk_tb_fee_payment_allocations_fee_type_name CHECK (length(btrim(fee_type_name_snapshot)) > 0);
    END IF;
END
$yc1bs$;

-- 3. verification
DO $yc1bv$
DECLARE
    v_b _yc1b_before; v_c bigint; v_a bigint; v_bad bigint;
BEGIN
    IF NOT (SELECT migrating FROM _yc1b_state) THEN RETURN; END IF;
    SELECT * INTO v_b FROM _yc1b_before;
    SELECT count(*) INTO v_c FROM public.tb_student_fee_concessions;
    SELECT count(*) INTO v_a FROM public.tb_fee_payment_allocations;
    IF v_c <> v_b.concessions OR v_a <> v_b.allocations THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: row counts changed (concessions % -> %, allocations % -> %). Everything is rolled back.', v_b.concessions, v_c, v_b.allocations, v_a;
    END IF;
    SELECT count(*) INTO v_bad FROM public.tb_student_fee_concessions c
     WHERE NOT EXISTS (SELECT 1 FROM public.tb_student_enrollments e WHERE e.student_id = c.student_id AND e.academic_year_id = c.academic_year_id);
    IF v_bad <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % concession(s) have a year that is not their student''s enrolment year. Everything is rolled back.', v_bad; END IF;
    SELECT count(*) INTO v_bad FROM public.tb_fee_payment_allocations a
      JOIN public.tb_student_fee_dues d ON d.fee_due_id = a.fee_due_id
      JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id
      JOIN public.tb_fee_types t ON t.fee_type_id = i.fee_type_id
     WHERE a.fee_type_name_snapshot IS DISTINCT FROM t.fee_type_name;
    IF v_bad <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % allocation snapshot(s) differ from the fee type name. Everything is rolled back.', v_bad; END IF;
    RAISE NOTICE 'VERIFIED: % concession rows and % payment lines before and after; every concession has its student''s enrolment year; every payment line has its fee type name.', v_c, v_a;
END
$yc1bv$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_student_fee_concessions) AS concessions,
       (SELECT count(DISTINCT academic_year_id) FROM public.tb_student_fee_concessions) AS concession_years,
       (SELECT count(*) FROM public.tb_fee_payment_allocations) AS payment_lines,
       (SELECT count(*) FROM public.tb_fee_payment_allocations WHERE fee_type_name_snapshot IS NOT NULL) AS payment_lines_with_name;
