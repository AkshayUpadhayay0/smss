-- =====================================================================================================================
-- RUN-ME: Student year promotion, stage 4b-1 = THE SCHEMA FOR PROMOTION  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/student-year-promotion-design.md and the Stage 4a/4b decisions approved on 25 Nov 2026.
--
-- WHY A MIGRATION: promotion adds a NEW enrolment in the new year and records the old enrolment's outcome. That needs (1) an outcome on the enrolment and (2) an audit of every promotion run and of
-- every student in it. Everything here is ADDITIVE: no existing row of any table is changed, no existing column is altered. NOTHING IS PROMOTED BY THIS SCRIPT.
--
-- WHAT IT DOES
--   1. tb_student_enrollments.outcome     varchar(20) NULL   PROMOTED, REPEATED, PASSED_OUT, TRANSFERRED_OUT or LEFT (check). NULL = no outcome yet (every existing row).
--      tb_student_enrollments.outcome_at  timestamptz NULL   when the outcome was recorded. Both are set together or not at all (check).
--      The OLD enrolment is never set Inactive and no other column of it is changed: the outcome is the only thing promotion writes on it.
--      Plus a unique key (school_id, enrollment_id) so the audit rows can point at an enrolment of the SAME school (enrollment_id is already the primary key, so it can never be violated).
--   2. tb_promotion_batches   one row per promotion RUN: school, source and target year, scope (one class = all its sections, or one section), the promotion date (also the dues cutoff:
--      dues with due_date <= this date count), who and when, the counts, the override reason (one per run, at least 10 characters) and whether the "target class has no fee structure" warning was acknowledged.
--   3. tb_promotion_items     one row per student of a run: decision (PROMOTE, REPEAT, PASS_OUT, LEAVE), from/to class and section, the new roll number, the source and target enrolment,
--      the outstanding amount AT THAT MOMENT, whether the dues block was overridden (and the reason), and, for a student left out of the run, the skip reason.
--   school_id is on every row, every foreign key is composite with school_id (a row can never point at another school's year, class, section, student or enrolment), and every table is indexed by school.
--
-- ABOUT CLOSED YEARS: the database does not stop writes into a closed year (the rule lives in the API services). Promotion may write the OUTCOME on an enrolment of a CLOSED source year, and nothing else
-- may; that exception is enforced in the promotion service (4b-3), never by loosening anything here. A closed or non-later TARGET year is always refused by the service.
--
-- PRE-CHECK (violators): none of the new constraints can be violated by existing data (the new columns are NULL on every existing row, the new tables are empty, enrollment_id is already unique).
--   The script still checks that tb_student_enrollments has no outcome columns yet when it starts to migrate, and prints the row count it will leave untouched.
--   Verification (raises, everything rolls back, on any mismatch): the enrolment row count and a fingerprint of every pre-existing column are identical before and after, every outcome is NULL,
--   and the three structures exist with their checks.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-4b1.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   DEPLOY ORDER: run this BEFORE the API build that contains the promotion preview/run (it reads these tables).
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK (only while no promotion has been run)
--   DROP TABLE tb_promotion_items; DROP TABLE tb_promotion_batches;
--   ALTER TABLE tb_student_enrollments DROP CONSTRAINT chk_tb_enrollments_outcome, DROP CONSTRAINT chk_tb_enrollments_outcome_pair, DROP CONSTRAINT uq_tb_enrollments_school_enrollment,
--                                      DROP COLUMN outcome, DROP COLUMN outcome_at;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st4b1_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st4b1_before (enrolments bigint, fingerprint text) ON COMMIT DROP;

DO $st4b1$
DECLARE
    v_mig boolean;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    IF to_regclass('public.tb_student_enrollments') IS NULL OR to_regclass('public.tb_academic_years') IS NULL OR to_regclass('public.tb_classes') IS NULL
       OR to_regclass('public.tb_sections') IS NULL OR to_regclass('public.tb_students') IS NULL OR to_regclass('public.tb_users') IS NULL OR to_regclass('public.tb_schools') IS NULL THEN
        RAISE EXCEPTION 'A table promotion hangs on is missing (enrolments, years, classes, sections, students, users or schools). Nothing was changed.';
    END IF;

    v_mig := NOT (
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_enrollments' AND column_name = 'outcome')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_enrollments' AND column_name = 'outcome_at')
        AND to_regclass('public.tb_promotion_batches') IS NOT NULL AND to_regclass('public.tb_promotion_items') IS NOT NULL
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_enrollments_school_enrollment')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_enrollments_outcome')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_enrollments_outcome_pair'));
    INSERT INTO _st4b1_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    -- fingerprint of everything that exists today (the new columns are not in it)
    INSERT INTO _st4b1_before
    SELECT count(*), coalesce(md5(string_agg(concat_ws('|', enrollment_id, school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id, created_at, updated_at), E'\n' ORDER BY enrollment_id)), '')
      FROM public.tb_student_enrollments;
    RAISE NOTICE 'PRE-CHECK: % enrolment rows will be left untouched; no violators are possible (new nullable columns, new empty tables).', (SELECT enrolments FROM _st4b1_before);
END
$st4b1$;

-- ------------------------------------------------------------------------------------------------ 1. the outcome on the enrolment
ALTER TABLE public.tb_student_enrollments ADD COLUMN IF NOT EXISTS outcome    varchar(20) NULL;
ALTER TABLE public.tb_student_enrollments ADD COLUMN IF NOT EXISTS outcome_at timestamptz NULL;

DO $st4b1c$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_enrollments_outcome') THEN
        ALTER TABLE public.tb_student_enrollments ADD CONSTRAINT chk_tb_enrollments_outcome
            CHECK (outcome IS NULL OR outcome IN ('PROMOTED', 'REPEATED', 'PASSED_OUT', 'TRANSFERRED_OUT', 'LEFT'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_enrollments_outcome_pair') THEN
        ALTER TABLE public.tb_student_enrollments ADD CONSTRAINT chk_tb_enrollments_outcome_pair
            CHECK ((outcome IS NULL) = (outcome_at IS NULL));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_enrollments_school_enrollment') THEN
        ALTER TABLE public.tb_student_enrollments ADD CONSTRAINT uq_tb_enrollments_school_enrollment UNIQUE (school_id, enrollment_id);
    END IF;
END
$st4b1c$;

-- ------------------------------------------------------------------------------------------------ 2. one row per promotion run
CREATE TABLE IF NOT EXISTS public.tb_promotion_batches (
    batch_id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    school_id                 varchar(100) NOT NULL,
    source_year_id            bigint       NOT NULL,
    target_year_id            bigint       NOT NULL,
    scope                     varchar(10)  NOT NULL,
    class_id                  bigint       NOT NULL,
    section_id                bigint       NULL,
    promotion_date            date         NOT NULL,                 -- the date of the run AND the dues cutoff: dues with due_date <= this date count
    created_by                bigint       NOT NULL,
    created_at                timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    students_total            integer      NOT NULL DEFAULT 0,
    promoted_count            integer      NOT NULL DEFAULT 0,
    repeated_count            integer      NOT NULL DEFAULT 0,
    passed_out_count          integer      NOT NULL DEFAULT 0,
    left_count                integer      NOT NULL DEFAULT 0,
    skipped_count             integer      NOT NULL DEFAULT 0,
    override_count            integer      NOT NULL DEFAULT 0,
    override_reason           varchar(500) NULL,
    no_fee_structure_acknowledged boolean  NOT NULL DEFAULT false,   -- the target class had no fee structure and the admin ticked "continue anyway"
    CONSTRAINT uq_tb_promotion_batches_school_batch UNIQUE (school_id, batch_id),
    CONSTRAINT fk_tb_promotion_batches_school  FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_promotion_batches_source  FOREIGN KEY (school_id, source_year_id) REFERENCES public.tb_academic_years (school_id, academic_year_id),
    CONSTRAINT fk_tb_promotion_batches_target  FOREIGN KEY (school_id, target_year_id) REFERENCES public.tb_academic_years (school_id, academic_year_id),
    CONSTRAINT fk_tb_promotion_batches_class   FOREIGN KEY (school_id, class_id)       REFERENCES public.tb_classes (school_id, class_id),
    CONSTRAINT fk_tb_promotion_batches_section FOREIGN KEY (school_id, section_id)     REFERENCES public.tb_sections (school_id, section_id),
    CONSTRAINT fk_tb_promotion_batches_user    FOREIGN KEY (created_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_promotion_batches_years  CHECK (source_year_id <> target_year_id),
    CONSTRAINT chk_tb_promotion_batches_scope  CHECK (scope IN ('CLASS', 'SECTION') AND ((scope = 'SECTION') = (section_id IS NOT NULL))),
    CONSTRAINT chk_tb_promotion_batches_counts CHECK (students_total >= 0 AND promoted_count >= 0 AND repeated_count >= 0 AND passed_out_count >= 0 AND left_count >= 0 AND skipped_count >= 0 AND override_count >= 0),
    CONSTRAINT chk_tb_promotion_batches_reason CHECK ((override_reason IS NULL OR length(btrim(override_reason)) >= 10) AND (override_count = 0 OR override_reason IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS idx_tb_promotion_batches_school ON public.tb_promotion_batches (school_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_tb_promotion_batches_target ON public.tb_promotion_batches (school_id, target_year_id);

-- ------------------------------------------------------------------------------------------------ 3. one row per student of a run
CREATE TABLE IF NOT EXISTS public.tb_promotion_items (
    item_id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    batch_id              bigint       NOT NULL,
    school_id             varchar(100) NOT NULL,
    student_id            varchar(20)  NOT NULL,
    source_enrollment_id  bigint       NOT NULL,
    target_enrollment_id  bigint       NULL,
    decision              varchar(10)  NOT NULL,
    from_class_id         bigint       NOT NULL,
    from_section_id       bigint       NOT NULL,
    to_class_id           bigint       NULL,
    to_section_id         bigint       NULL,
    roll_number           varchar(30)  NULL,                          -- the roll number given in the NEW section
    outstanding_amount    numeric(12,2) NOT NULL DEFAULT 0,           -- unpaid dues (due_date <= promotion date) of the source year at that moment
    override_applied      boolean      NOT NULL DEFAULT false,
    override_reason       varchar(500) NULL,                          -- the run's reason, stored on every overridden student
    skip_reason           varchar(200) NULL,                          -- set when the student was left out of the run
    created_at            timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_tb_promotion_items_batch_student UNIQUE (batch_id, student_id),
    CONSTRAINT fk_tb_promotion_items_batch    FOREIGN KEY (school_id, batch_id) REFERENCES public.tb_promotion_batches (school_id, batch_id),
    CONSTRAINT fk_tb_promotion_items_student  FOREIGN KEY (student_id, school_id) REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_promotion_items_source   FOREIGN KEY (school_id, source_enrollment_id) REFERENCES public.tb_student_enrollments (school_id, enrollment_id),
    CONSTRAINT fk_tb_promotion_items_target   FOREIGN KEY (school_id, target_enrollment_id) REFERENCES public.tb_student_enrollments (school_id, enrollment_id),
    CONSTRAINT fk_tb_promotion_items_from_cls FOREIGN KEY (school_id, from_class_id)   REFERENCES public.tb_classes (school_id, class_id),
    CONSTRAINT fk_tb_promotion_items_from_sec FOREIGN KEY (school_id, from_section_id) REFERENCES public.tb_sections (school_id, section_id),
    CONSTRAINT fk_tb_promotion_items_to_cls   FOREIGN KEY (school_id, to_class_id)     REFERENCES public.tb_classes (school_id, class_id),
    CONSTRAINT fk_tb_promotion_items_to_sec   FOREIGN KEY (school_id, to_section_id)   REFERENCES public.tb_sections (school_id, section_id),
    CONSTRAINT chk_tb_promotion_items_decision CHECK (decision IN ('PROMOTE', 'REPEAT', 'PASS_OUT', 'LEAVE')),
    CONSTRAINT chk_tb_promotion_items_amount   CHECK (outstanding_amount >= 0),
    -- a student who was done has a new enrolment exactly when the decision gives one; a student who was left out has none and a reason
    CONSTRAINT chk_tb_promotion_items_target   CHECK (
        (skip_reason IS NULL AND ((decision IN ('PROMOTE', 'REPEAT')) = (target_enrollment_id IS NOT NULL AND to_class_id IS NOT NULL AND to_section_id IS NOT NULL))
                              AND (decision IN ('PROMOTE', 'REPEAT') OR (target_enrollment_id IS NULL AND to_class_id IS NULL AND to_section_id IS NULL AND roll_number IS NULL)))
        OR (skip_reason IS NOT NULL AND length(btrim(skip_reason)) > 0 AND target_enrollment_id IS NULL AND NOT override_applied)),
    CONSTRAINT chk_tb_promotion_items_override CHECK (override_applied = (override_reason IS NOT NULL) AND (NOT override_applied OR (outstanding_amount > 0 AND length(btrim(override_reason)) >= 10)))
);
CREATE INDEX IF NOT EXISTS idx_tb_promotion_items_school_student ON public.tb_promotion_items (school_id, student_id);
CREATE INDEX IF NOT EXISTS idx_tb_promotion_items_batch          ON public.tb_promotion_items (batch_id);

-- ------------------------------------------------------------------------------------------------ verification
DO $st4b1v$
DECLARE
    b _st4b1_before;
    f text;
BEGIN
    IF NOT (SELECT migrating FROM _st4b1_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st4b1_before;
    SELECT coalesce(md5(string_agg(concat_ws('|', enrollment_id, school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id, created_at, updated_at), E'\n' ORDER BY enrollment_id)), '')
      INTO f FROM public.tb_student_enrollments;
    IF (SELECT count(*) FROM public.tb_student_enrollments) <> b.enrolments OR f <> b.fingerprint THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an enrolment row changed. Everything is rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM public.tb_student_enrollments WHERE outcome IS NOT NULL OR outcome_at IS NOT NULL) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an existing enrolment got an outcome. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM public.tb_promotion_batches) <> 0 OR (SELECT count(*) FROM public.tb_promotion_items) <> 0 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: the new tables are not empty. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE conrelid IN ('public.tb_promotion_batches'::regclass, 'public.tb_promotion_items'::regclass) AND contype = 'f') <> 14
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid IN ('public.tb_promotion_batches'::regclass, 'public.tb_promotion_items'::regclass) AND contype = 'c') <> 8 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a foreign key or check of the new tables is missing. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % enrolment rows unchanged (fingerprint identical, every outcome NULL); tb_promotion_batches and tb_promotion_items exist, empty, with their 14 foreign keys and 8 checks.', b.enrolments;
END
$st4b1v$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_promotion_batches) AS batches, (SELECT count(*) FROM public.tb_promotion_items) AS items;
