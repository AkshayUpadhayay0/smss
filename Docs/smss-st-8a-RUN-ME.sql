-- =====================================================================================================================
-- RUN-ME: Student status and leaving, stage 8a = RECORD LEAVING  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/student-year-promotion-design.md section 7 and the Stage 8 decisions of 26 Nov 2026 (tb_students.status_id is NOT changed when a student leaves; "left" is the outcome of the enrolment).
--
-- WHAT IT DOES (additive only; no existing row is changed)
--   1. tb_student_enrollments gets five NULLABLE columns, all NULL on every existing row:
--        leaving_date              date           the day the student left (LEFT / TRANSFERRED_OUT recorded by "Record leaving")
--        leaving_reason            varchar(500)   why
--        outcome_by                bigint         the user who recorded it (FK tb_users)
--        leaving_override_reason   varchar(500)   the reason given when unpaid dues were overridden (at least 10 characters), else NULL
--        leaving_outstanding       numeric(12,2)  the unpaid amount due on or before the leaving date at that moment
--   2. chk_tb_enrollments_leaving_shape   the five columns are all NULL, or leaving_date + leaving_reason + outcome_by are set AND the outcome is LEFT or TRANSFERRED_OUT
--      chk_tb_enrollments_leaving_override an override reason, when present, has at least 10 characters
--   3. tb_student_leaving_log   one audit row per "Record leaving" and per correction: school, student, the enrolment id (no foreign key: the row must outlive the enrolment), the action, the date, the reason,
--      the outstanding amount, whether dues were overridden and why, who and when. Foreign keys to the school, the student and the user.
--
-- PRE-CHECK (violators): none possible. The new columns are NULL on every existing row, the two new checks hold for all-NULL rows, and the log table is new and empty. The script prints how many enrolments
--   exist and how many already carry an outcome (promotion pass-out / leave), and verifies that every pre-existing column of every enrolment is identical afterwards (fingerprint).
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-8a.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   DEPLOY ORDER: run this BEFORE the API build that contains Stage 8a (its entity maps the new columns). Needs the 4b-1 script (the outcome columns).
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK (only while nobody has been recorded as leaving)
--   DROP TABLE tb_student_leaving_log;
--   ALTER TABLE tb_student_enrollments DROP CONSTRAINT chk_tb_enrollments_leaving_override, DROP CONSTRAINT chk_tb_enrollments_leaving_shape, DROP CONSTRAINT fk_tb_enrollments_outcome_by,
--     DROP COLUMN leaving_outstanding, DROP COLUMN leaving_override_reason, DROP COLUMN outcome_by, DROP COLUMN leaving_reason, DROP COLUMN leaving_date;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st8a_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st8a_before (enrolments bigint, with_outcome bigint, fingerprint text) ON COMMIT DROP;

DO $st8a$
DECLARE
    v_mig boolean;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    IF to_regclass('public.tb_student_enrollments') IS NULL OR to_regclass('public.tb_students') IS NULL OR to_regclass('public.tb_users') IS NULL OR to_regclass('public.tb_schools') IS NULL THEN
        RAISE EXCEPTION 'A table the leaving hangs on is missing (enrolments, students, users or schools). Nothing was changed.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_enrollments' AND column_name = 'outcome')
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_enrollments' AND column_name = 'outcome_at') THEN
        RAISE EXCEPTION 'tb_student_enrollments.outcome / outcome_at (stage 4b-1) are missing. Run the 4b-1 script first. Nothing was changed.';
    END IF;

    v_mig := NOT (
        (SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_enrollments'
            AND column_name IN ('leaving_date', 'leaving_reason', 'outcome_by', 'leaving_override_reason', 'leaving_outstanding')) = 5
        AND to_regclass('public.tb_student_leaving_log') IS NOT NULL
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_enrollments_leaving_shape')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_enrollments_leaving_override')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_enrollments_outcome_by'));
    INSERT INTO _st8a_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    INSERT INTO _st8a_before
    SELECT count(*), count(*) FILTER (WHERE outcome IS NOT NULL),
           coalesce(md5(string_agg(concat_ws('|', enrollment_id, school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id, outcome, outcome_at,
                                             created_at, updated_at), E'\n' ORDER BY enrollment_id)), '')
      FROM public.tb_student_enrollments;
    RAISE NOTICE 'PRE-CHECK: % enrolments exist, % already carry an outcome; no violators are possible (the new columns are NULL on every row).',
        (SELECT enrolments FROM _st8a_before), (SELECT with_outcome FROM _st8a_before);
END
$st8a$;

-- ------------------------------------------------------------------------------------------------ 1. the enrolment columns
ALTER TABLE public.tb_student_enrollments ADD COLUMN IF NOT EXISTS leaving_date            date          NULL;
ALTER TABLE public.tb_student_enrollments ADD COLUMN IF NOT EXISTS leaving_reason          varchar(500)  NULL;
ALTER TABLE public.tb_student_enrollments ADD COLUMN IF NOT EXISTS outcome_by              bigint        NULL;
ALTER TABLE public.tb_student_enrollments ADD COLUMN IF NOT EXISTS leaving_override_reason varchar(500)  NULL;
ALTER TABLE public.tb_student_enrollments ADD COLUMN IF NOT EXISTS leaving_outstanding     numeric(12,2) NULL;

-- ------------------------------------------------------------------------------------------------ 2. checks and the foreign key
DO $st8ac$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_enrollments_outcome_by') THEN
        ALTER TABLE public.tb_student_enrollments ADD CONSTRAINT fk_tb_enrollments_outcome_by FOREIGN KEY (outcome_by) REFERENCES public.tb_users (user_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_enrollments_leaving_shape') THEN
        ALTER TABLE public.tb_student_enrollments ADD CONSTRAINT chk_tb_enrollments_leaving_shape CHECK (
            (leaving_date IS NULL AND leaving_reason IS NULL AND outcome_by IS NULL AND leaving_override_reason IS NULL AND leaving_outstanding IS NULL)
            OR (leaving_date IS NOT NULL AND leaving_reason IS NOT NULL AND length(btrim(leaving_reason)) > 0 AND outcome_by IS NOT NULL AND outcome IN ('LEFT', 'TRANSFERRED_OUT')));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_enrollments_leaving_override') THEN
        ALTER TABLE public.tb_student_enrollments ADD CONSTRAINT chk_tb_enrollments_leaving_override CHECK (leaving_override_reason IS NULL OR length(btrim(leaving_override_reason)) >= 10);
    END IF;
END
$st8ac$;

-- ------------------------------------------------------------------------------------------------ 3. the audit log
CREATE TABLE IF NOT EXISTS public.tb_student_leaving_log (
    log_id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    school_id           varchar(100)  NOT NULL,
    student_id          varchar(20)   NOT NULL,
    enrollment_id       bigint        NOT NULL,                      -- no foreign key on purpose: the audit row outlives the enrolment
    action              varchar(20)   NOT NULL,                      -- LEFT | TRANSFERRED_OUT | CORRECTION
    leaving_date        date          NULL,
    reason              varchar(500)  NOT NULL,
    outstanding_amount  numeric(12,2) NOT NULL DEFAULT 0,
    override_applied    boolean       NOT NULL DEFAULT false,
    override_reason     varchar(500)  NULL,
    outcome_cleared     varchar(20)   NULL,                          -- for a CORRECTION: the outcome that was cleared
    acted_by            bigint        NOT NULL,
    acted_at            timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_tb_student_leaving_log_school  FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_student_leaving_log_student FOREIGN KEY (student_id, school_id) REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_student_leaving_log_user    FOREIGN KEY (acted_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_student_leaving_log_action CHECK (action IN ('LEFT', 'TRANSFERRED_OUT', 'CORRECTION')),
    CONSTRAINT chk_tb_student_leaving_log_reason CHECK (length(btrim(reason)) > 0 AND (action <> 'CORRECTION' OR length(btrim(reason)) >= 10)),
    CONSTRAINT chk_tb_student_leaving_log_override CHECK ((NOT override_applied AND override_reason IS NULL) OR (override_applied AND override_reason IS NOT NULL AND length(btrim(override_reason)) >= 10)),
    CONSTRAINT chk_tb_student_leaving_log_cleared CHECK ((action = 'CORRECTION') = (outcome_cleared IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS idx_tb_student_leaving_log_school  ON public.tb_student_leaving_log (school_id, acted_at DESC);
CREATE INDEX IF NOT EXISTS idx_tb_student_leaving_log_student ON public.tb_student_leaving_log (school_id, student_id);

-- ------------------------------------------------------------------------------------------------ verification
DO $st8av$
DECLARE
    b _st8a_before;
    f text;
BEGIN
    IF NOT (SELECT migrating FROM _st8a_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st8a_before;
    SELECT coalesce(md5(string_agg(concat_ws('|', enrollment_id, school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id, outcome, outcome_at,
                                             created_at, updated_at), E'\n' ORDER BY enrollment_id)), '')
      INTO f FROM public.tb_student_enrollments;
    IF (SELECT count(*) FROM public.tb_student_enrollments) <> b.enrolments OR f <> b.fingerprint THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an enrolment changed. Everything is rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM public.tb_student_enrollments WHERE leaving_date IS NOT NULL OR leaving_reason IS NOT NULL OR outcome_by IS NOT NULL OR leaving_override_reason IS NOT NULL OR leaving_outstanding IS NOT NULL)
       OR (SELECT count(*) FROM public.tb_student_leaving_log) <> 0 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a leaving value or log row exists after migrating. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_student_leaving_log'::regclass AND contype = 'f') <> 3
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_student_leaving_log'::regclass AND contype = 'c') <> 4
       OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_enrollments_leaving_shape') OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_enrollments_leaving_override')
       OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_enrollments_outcome_by') THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a constraint is missing. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % enrolments unchanged (fingerprint identical, % with an outcome as before); 5 new columns NULL everywhere; tb_student_leaving_log exists, empty, with its 3 foreign keys and 4 checks.', b.enrolments, b.with_outcome;
END
$st8av$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_student_leaving_log) AS leaving_log_rows;
