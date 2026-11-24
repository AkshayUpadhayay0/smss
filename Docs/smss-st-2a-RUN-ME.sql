-- =====================================================================================================================
-- RUN-ME: Student and admission changes, stage 2a = the LAST NAME IS OPTIONAL  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/admission-docs-latefee-registration-design.md (section 1), decided 24 Nov 2026: only the FIRST NAME is mandatory.
--
-- WHAT IT DOES
--   1. tb_students.last_name and tb_admissions.applicant_last_name become NULLABLE (they were NOT NULL).
--   2. A last name that is present must not be blank: CHECK (last_name IS NULL OR btrim(last_name) <> '') on both columns. The API turns a blank into NULL before it gets here.
--   3. Pre-check (lists the offenders and raises BEFORE anything changes): rows whose last name is blank or only spaces (there are none today).
--   4. Verification (raises, so everything rolls back, on any mismatch): the row counts of both tables are identical before and after, no row's last name was changed,
--      both columns are nullable and both checks exist.
--   Nothing is deleted or rewritten: no existing row changes.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-2a.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK (only while no student or application has an empty last name; otherwise set one first)
--   ALTER TABLE tb_students DROP CONSTRAINT chk_tb_students_last_name_not_blank;  ALTER TABLE tb_students ALTER COLUMN last_name SET NOT NULL;
--   ALTER TABLE tb_admissions DROP CONSTRAINT chk_tb_admissions_last_name_not_blank;  ALTER TABLE tb_admissions ALTER COLUMN applicant_last_name SET NOT NULL;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st2a_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st2a_before (students bigint, students_name_hash text, admissions bigint, admissions_name_hash text) ON COMMIT DROP;

DO $st2a$
DECLARE
    v_mig boolean;
    t     text;
    v_list text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_students', 'tb_admissions'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist. Nothing was changed.', t;
        END IF;
    END LOOP;

    v_mig := NOT (
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_students'   AND column_name = 'last_name'           AND is_nullable = 'YES')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_admissions' AND column_name = 'applicant_last_name' AND is_nullable = 'YES')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_students_last_name_not_blank'   AND conrelid = 'public.tb_students'::regclass)
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_admissions_last_name_not_blank' AND conrelid = 'public.tb_admissions'::regclass));
    INSERT INTO _st2a_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    SELECT string_agg(format('student %s', student_id), ', ' ORDER BY student_id) INTO v_list FROM (SELECT student_id FROM public.tb_students WHERE last_name IS NOT NULL AND btrim(last_name) = '' LIMIT 20) x;
    IF v_list IS NOT NULL THEN RAISE EXCEPTION 'PRE-CHECK FAILED (nothing was changed): students whose last name is blank: %', v_list; END IF;
    SELECT string_agg(format('application %s (%s)', admission_id, application_number), ', ' ORDER BY admission_id) INTO v_list
      FROM (SELECT admission_id, application_number FROM public.tb_admissions WHERE applicant_last_name IS NOT NULL AND btrim(applicant_last_name) = '' LIMIT 20) x;
    IF v_list IS NOT NULL THEN RAISE EXCEPTION 'PRE-CHECK FAILED (nothing was changed): applications whose last name is blank: %', v_list; END IF;
    RAISE NOTICE 'Pre-checks passed: no blank last name in tb_students or tb_admissions.';

    INSERT INTO _st2a_before
    SELECT (SELECT count(*) FROM public.tb_students),   (SELECT md5(coalesce(string_agg(student_id || '|' || coalesce(last_name, '~null~'), ',' ORDER BY student_id), '')) FROM public.tb_students),
           (SELECT count(*) FROM public.tb_admissions), (SELECT md5(coalesce(string_agg(admission_id || '|' || coalesce(applicant_last_name, '~null~'), ',' ORDER BY admission_id), '')) FROM public.tb_admissions);
END
$st2a$;

DO $st2achange$
BEGIN
    IF NOT (SELECT migrating FROM _st2a_state) THEN RETURN; END IF;
    ALTER TABLE public.tb_students   ALTER COLUMN last_name           DROP NOT NULL;
    ALTER TABLE public.tb_admissions ALTER COLUMN applicant_last_name DROP NOT NULL;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_students_last_name_not_blank' AND conrelid = 'public.tb_students'::regclass) THEN
        ALTER TABLE public.tb_students ADD CONSTRAINT chk_tb_students_last_name_not_blank CHECK (last_name IS NULL OR btrim(last_name) <> '');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_admissions_last_name_not_blank' AND conrelid = 'public.tb_admissions'::regclass) THEN
        ALTER TABLE public.tb_admissions ADD CONSTRAINT chk_tb_admissions_last_name_not_blank CHECK (applicant_last_name IS NULL OR btrim(applicant_last_name) <> '');
    END IF;
END
$st2achange$;

DO $st2av$
DECLARE
    b _st2a_before; s bigint; sh text; a bigint; ah text;
BEGIN
    IF NOT (SELECT migrating FROM _st2a_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st2a_before;
    SELECT count(*), md5(coalesce(string_agg(student_id || '|' || coalesce(last_name, '~null~'), ',' ORDER BY student_id), '')) INTO s, sh FROM public.tb_students;
    SELECT count(*), md5(coalesce(string_agg(admission_id || '|' || coalesce(applicant_last_name, '~null~'), ',' ORDER BY admission_id), '')) INTO a, ah FROM public.tb_admissions;
    IF s <> b.students OR sh <> b.students_name_hash OR a <> b.admissions OR ah <> b.admissions_name_hash THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a row or a last name changed. Everything is rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND ((table_name = 'tb_students' AND column_name = 'last_name') OR (table_name = 'tb_admissions' AND column_name = 'applicant_last_name')) AND is_nullable = 'NO') THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a last name column is still NOT NULL. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % students and % applications, every last name unchanged; both last name columns are nullable and both not-blank checks exist.', s, a;
END
$st2av$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_students) AS students,
       (SELECT count(*) FROM public.tb_students WHERE last_name IS NULL) AS students_without_last_name,
       (SELECT count(*) FROM public.tb_admissions) AS applications,
       (SELECT count(*) FROM public.tb_admissions WHERE applicant_last_name IS NULL) AS applications_without_last_name;
