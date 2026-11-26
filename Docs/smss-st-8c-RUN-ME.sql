-- =====================================================================================================================
-- RUN-ME: Student status and leaving, stage 8c = MID-YEAR SECTION CHANGE (same class, same year)  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: the Stage 8 decisions of 26 Nov 2026 (option 1: the single enrolment is kept and updated in place; every change leaves a history row).
--
-- WHAT IT DOES (one NEW table; no existing table or row is touched)
--   tb_student_section_changes   one row per change: the school, the student, the enrolment and the academic year it happened in, the section and roll number the student left, the section and roll number they
--   went to, the effective date, the reason (at least 10 characters), who and when. A row is never deleted or updated; moving back is simply another row. The enrolment id has no foreign key on purpose:
--   the history must outlive the enrolment. Foreign keys: the school, the student, the two sections, the user.
--
-- PRE-CHECK (violators): none possible (a new, empty table). The script checks that the 8a/yc-1a pieces exist and verifies that every existing enrolment is identical afterwards (fingerprint).
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-8c.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   DEPLOY ORDER: before the API build that contains Stage 8c.
--
-- RE-RUNNING IT: safe; a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
-- HOW TO ROLL BACK (only while nobody has changed a section): DROP TABLE tb_student_section_changes;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st8c_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st8c_before (enrolments bigint, fingerprint text) ON COMMIT DROP;

DO $st8c$
DECLARE
    v_mig boolean;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';
    IF to_regclass('public.tb_student_enrollments') IS NULL OR to_regclass('public.tb_students') IS NULL OR to_regclass('public.tb_sections') IS NULL OR to_regclass('public.tb_users') IS NULL
       OR to_regclass('public.tb_schools') IS NULL OR to_regclass('public.tb_academic_years') IS NULL THEN
        RAISE EXCEPTION 'A table the section change hangs on is missing. Nothing was changed.';
    END IF;

    v_mig := to_regclass('public.tb_student_section_changes') IS NULL;
    INSERT INTO _st8c_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    INSERT INTO _st8c_before
    SELECT count(*), coalesce(md5(string_agg(concat_ws('|', enrollment_id, school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id, outcome, outcome_at,
                                                       leaving_date, leaving_reason, outcome_by, created_at, updated_at), E'\n' ORDER BY enrollment_id)), '')
      FROM public.tb_student_enrollments;
    RAISE NOTICE 'PRE-CHECK: % enrolments exist; no violators are possible (a new, empty table).', (SELECT enrolments FROM _st8c_before);
END
$st8c$;

CREATE TABLE IF NOT EXISTS public.tb_student_section_changes (
    change_id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    school_id         varchar(100) NOT NULL,
    student_id        varchar(20)  NOT NULL,
    enrollment_id     bigint       NOT NULL,                     -- no foreign key on purpose: the history outlives the enrolment
    academic_year_id  bigint       NOT NULL,
    from_section_id   bigint       NOT NULL,
    to_section_id     bigint       NOT NULL,
    from_roll         varchar(30)  NULL,
    to_roll           varchar(30)  NULL,
    effective_date    date         NOT NULL,
    reason            varchar(500) NOT NULL,
    changed_by        bigint       NOT NULL,
    changed_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_tb_section_changes_school  FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_section_changes_student FOREIGN KEY (student_id, school_id) REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_section_changes_from    FOREIGN KEY (school_id, from_section_id) REFERENCES public.tb_sections (school_id, section_id),
    CONSTRAINT fk_tb_section_changes_to      FOREIGN KEY (school_id, to_section_id) REFERENCES public.tb_sections (school_id, section_id),
    CONSTRAINT fk_tb_section_changes_user    FOREIGN KEY (changed_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_section_changes_differ CHECK (from_section_id <> to_section_id),
    CONSTRAINT chk_tb_section_changes_reason CHECK (length(btrim(reason)) >= 10)
);
CREATE INDEX IF NOT EXISTS idx_tb_section_changes_student ON public.tb_student_section_changes (school_id, student_id, changed_at DESC);
CREATE INDEX IF NOT EXISTS idx_tb_section_changes_school  ON public.tb_student_section_changes (school_id, changed_at DESC);

DO $st8cv$
DECLARE
    b _st8c_before;
    f text;
BEGIN
    IF NOT (SELECT migrating FROM _st8c_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st8c_before;
    SELECT coalesce(md5(string_agg(concat_ws('|', enrollment_id, school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id, outcome, outcome_at,
                                             leaving_date, leaving_reason, outcome_by, created_at, updated_at), E'\n' ORDER BY enrollment_id)), '')
      INTO f FROM public.tb_student_enrollments;
    IF (SELECT count(*) FROM public.tb_student_enrollments) <> b.enrolments OR f <> b.fingerprint THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an enrolment changed. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM public.tb_student_section_changes) <> 0
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_student_section_changes'::regclass AND contype = 'f') <> 5
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_student_section_changes'::regclass AND contype = 'c') <> 2 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: the table is not as designed. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % enrolments unchanged (fingerprint identical); tb_student_section_changes exists, empty, with its 5 foreign keys and 2 checks.', b.enrolments;
END
$st8cv$;

COMMIT;

SELECT count(*) AS section_changes FROM public.tb_student_section_changes;
