-- =====================================================================================================================
-- RUN-ME: Student year promotion, stage 4b-4 = THE CORRECTION OF ONE PROMOTION  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: the decision change of 25 Nov 2026 (a single-student correction instead of "no undo"), see Docs/student-year-promotion-design.md section 6.
--
-- WHY A MIGRATION: a correction removes the student's NEW-year enrolment, which a promotion item still points at (tb_promotion_items.target_enrollment_id, a foreign key), and it must leave an audit row of
-- who corrected what and why. Both need a small change. Everything is ADDITIVE or WEAKENING on tables that are still EMPTY on every database that ran 4b-1 and has not promoted yet; no existing row of
-- any table is changed. NOTHING IS CORRECTED BY THIS SCRIPT.
--
-- WHAT IT DOES
--   1. tb_promotion_items.reversed_at  timestamptz NULL   set when the promotion of that student was corrected; the new enrolment no longer exists then, so target_enrollment_id is NULL
--   2. chk_tb_promotion_items_target   re-created: a finished PROMOTE/REPEAT item has a target enrolment OR is marked reversed (everything else exactly as before)
--      chk_tb_promotion_items_reversed new: only a finished (not skipped) item can be reversed
--   3. uq_tb_promotion_items_school_item  UNIQUE (school_id, item_id): lets the audit point at an item of the SAME school (item_id is already the primary key, so it can never be violated)
--   4. tb_promotion_corrections  one row per correction: school, student, the batch and item corrected, the old enrolment, the id and a snapshot of what was removed (enrolment, fee items, dues, amounts),
--      the outcome that was cleared, the reason (at least 10 characters), who and when. school_id on the row, composite foreign keys, indexed by school.
--
-- PRE-CHECK (violators): none possible. The script checks that 4b-1 is in place and prints how many promotion batches and items exist (0 on a database that has not promoted yet); the re-created check is
--   WEAKER than the old one, and the new check and key hold for any existing row (reversed_at is NULL on every row; item_id is unique).
--   Verification (raises, everything rolls back, on any mismatch): batch and item row counts and a fingerprint of every pre-existing item column are identical before and after; the new objects exist.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-4b4.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   DEPLOY ORDER: run this BEFORE the API build that contains the correction (the build reads tb_promotion_corrections and writes tb_promotion_items.reversed_at).
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK (only while no correction has been made)
--   DROP TABLE tb_promotion_corrections;
--   ALTER TABLE tb_promotion_items DROP CONSTRAINT chk_tb_promotion_items_reversed, DROP CONSTRAINT uq_tb_promotion_items_school_item, DROP CONSTRAINT chk_tb_promotion_items_target, DROP COLUMN reversed_at;
--   then add chk_tb_promotion_items_target back with the definition in smss-st-4b1-RUN-ME.sql.
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st4b4_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st4b4_before (batches bigint, items bigint, fingerprint text) ON COMMIT DROP;

DO $st4b4$
DECLARE
    v_mig boolean;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    IF to_regclass('public.tb_promotion_items') IS NULL OR to_regclass('public.tb_promotion_batches') IS NULL OR to_regclass('public.tb_students') IS NULL OR to_regclass('public.tb_users') IS NULL THEN
        RAISE EXCEPTION 'tb_promotion_batches / tb_promotion_items (stage 4b-1) are missing. Run the 4b-1 script first. Nothing was changed.';
    END IF;

    v_mig := NOT (
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_promotion_items' AND column_name = 'reversed_at')
        AND to_regclass('public.tb_promotion_corrections') IS NOT NULL
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_promotion_items_reversed')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_promotion_items_school_item'));
    INSERT INTO _st4b4_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    INSERT INTO _st4b4_before
    SELECT (SELECT count(*) FROM public.tb_promotion_batches), count(*),
           coalesce(md5(string_agg(concat_ws('|', item_id, batch_id, school_id, student_id, source_enrollment_id, target_enrollment_id, decision, from_class_id, from_section_id, to_class_id, to_section_id, roll_number,
                                             outstanding_amount, override_applied, override_reason, skip_reason, created_at), E'\n' ORDER BY item_id)), '')
      FROM public.tb_promotion_items;
    RAISE NOTICE 'PRE-CHECK: % promotion batches and % items exist; no violators are possible (the new column is NULL, the re-created check is weaker, item_id is unique).',
        (SELECT batches FROM _st4b4_before), (SELECT items FROM _st4b4_before);
END
$st4b4$;

-- ------------------------------------------------------------------------------------------------ 1-3. promotion items
ALTER TABLE public.tb_promotion_items ADD COLUMN IF NOT EXISTS reversed_at timestamptz NULL;

DO $st4b4c$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_promotion_items_school_item') THEN
        ALTER TABLE public.tb_promotion_items ADD CONSTRAINT uq_tb_promotion_items_school_item UNIQUE (school_id, item_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_promotion_items_reversed') THEN
        -- the target check is re-created in the same step (its old text has no room for a reversed item)
        ALTER TABLE public.tb_promotion_items DROP CONSTRAINT IF EXISTS chk_tb_promotion_items_target;
        ALTER TABLE public.tb_promotion_items ADD CONSTRAINT chk_tb_promotion_items_target CHECK (
            (skip_reason IS NULL AND ((decision IN ('PROMOTE', 'REPEAT')) = (to_class_id IS NOT NULL AND to_section_id IS NOT NULL AND (target_enrollment_id IS NOT NULL OR reversed_at IS NOT NULL)))
                                  AND (decision IN ('PROMOTE', 'REPEAT') OR (target_enrollment_id IS NULL AND to_class_id IS NULL AND to_section_id IS NULL AND roll_number IS NULL)))
            OR (skip_reason IS NOT NULL AND length(btrim(skip_reason)) > 0 AND target_enrollment_id IS NULL AND NOT override_applied));
        ALTER TABLE public.tb_promotion_items ADD CONSTRAINT chk_tb_promotion_items_reversed CHECK (reversed_at IS NULL OR skip_reason IS NULL);
    END IF;
END
$st4b4c$;

-- ------------------------------------------------------------------------------------------------ 4. one row per correction
CREATE TABLE IF NOT EXISTS public.tb_promotion_corrections (
    correction_id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    school_id             varchar(100) NOT NULL,
    student_id            varchar(20)  NOT NULL,
    batch_id              bigint       NOT NULL,
    item_id               bigint       NOT NULL,
    source_enrollment_id  bigint       NOT NULL,                       -- the OLD enrolment whose outcome was cleared (it still exists)
    removed_enrollment_id bigint       NULL,                           -- the NEW enrolment that was removed (no foreign key: it no longer exists); NULL after a pass out / leave
    decision              varchar(10)  NOT NULL,
    outcome_cleared       varchar(20)  NOT NULL,
    removed               jsonb        NOT NULL DEFAULT '{}'::jsonb,   -- what was removed: the enrolment (class, section, roll), fee items, dues and their total, the fee structure assignment
    reason                varchar(500) NOT NULL,
    corrected_by          bigint       NOT NULL,
    corrected_at          timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_tb_promotion_corrections_school  FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_promotion_corrections_batch   FOREIGN KEY (school_id, batch_id) REFERENCES public.tb_promotion_batches (school_id, batch_id),
    CONSTRAINT fk_tb_promotion_corrections_item    FOREIGN KEY (school_id, item_id) REFERENCES public.tb_promotion_items (school_id, item_id),
    CONSTRAINT fk_tb_promotion_corrections_student FOREIGN KEY (student_id, school_id) REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_promotion_corrections_source  FOREIGN KEY (school_id, source_enrollment_id) REFERENCES public.tb_student_enrollments (school_id, enrollment_id),
    CONSTRAINT fk_tb_promotion_corrections_user    FOREIGN KEY (corrected_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_promotion_corrections_decision CHECK (decision IN ('PROMOTE', 'REPEAT', 'PASS_OUT', 'LEAVE')),
    CONSTRAINT chk_tb_promotion_corrections_outcome  CHECK (outcome_cleared IN ('PROMOTED', 'REPEATED', 'PASSED_OUT', 'LEFT')),
    CONSTRAINT chk_tb_promotion_corrections_reason   CHECK (length(btrim(reason)) >= 10)
);
CREATE INDEX IF NOT EXISTS idx_tb_promotion_corrections_school ON public.tb_promotion_corrections (school_id, corrected_at DESC);
CREATE INDEX IF NOT EXISTS idx_tb_promotion_corrections_student ON public.tb_promotion_corrections (school_id, student_id);

-- ------------------------------------------------------------------------------------------------ verification
DO $st4b4v$
DECLARE
    b _st4b4_before;
    f text;
BEGIN
    IF NOT (SELECT migrating FROM _st4b4_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st4b4_before;
    SELECT coalesce(md5(string_agg(concat_ws('|', item_id, batch_id, school_id, student_id, source_enrollment_id, target_enrollment_id, decision, from_class_id, from_section_id, to_class_id, to_section_id, roll_number,
                                             outstanding_amount, override_applied, override_reason, skip_reason, created_at), E'\n' ORDER BY item_id)), '')
      INTO f FROM public.tb_promotion_items;
    IF (SELECT count(*) FROM public.tb_promotion_batches) <> b.batches OR (SELECT count(*) FROM public.tb_promotion_items) <> b.items OR f <> b.fingerprint THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a promotion row changed. Everything is rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM public.tb_promotion_items WHERE reversed_at IS NOT NULL) OR (SELECT count(*) FROM public.tb_promotion_corrections) <> 0 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a correction exists after migrating. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_promotion_corrections'::regclass AND contype = 'f') <> 6
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_promotion_corrections'::regclass AND contype = 'c') <> 3
       OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_promotion_items_target') OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_promotion_items_reversed') THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a constraint is missing. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % promotion batches and % items unchanged (fingerprint identical); tb_promotion_corrections exists, empty, with its 6 foreign keys and 3 checks; the item checks are in place.', b.batches, b.items;
END
$st4b4v$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_promotion_corrections) AS corrections;
