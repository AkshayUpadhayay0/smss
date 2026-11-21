-- =====================================================================================================================
-- RUN-ME: Academic Years, stage S1  (one self-contained file for pgAdmin's Query Tool; single transaction, no psql meta-commands)
-- Design: Docs/academic-year-config-design.md (section C.2)
--
-- WHAT IT DOES
--   1. tb_academic_years.closed_at / closed_by / close_note: a year can be CLOSED (see the design: a closed year blocks configuration, attendance, enrolments,
--      homework and timetable edits; fee payments, voids, receipts and reads still work). Closed is only a marker; the state shown to users is derived.
--   2. CHECK chk_tb_ay_closed_not_current: the current year can never be closed.
--   3. UNIQUE INDEX uq_tb_ay_one_current (school_id) WHERE is_current: ONE current year per school, enforced by the database (was service-only).
--   4. EXCLUDE constraint ex_tb_ay_no_overlap: two ACTIVE years of one school cannot overlap (inclusive dates: a year starting on the day another ends is rejected).
--      Inactive (retired) years may overlap. The 'Active' status id is looked up by NAME when the script runs, never hard-coded.
--   5. Pre-check: if a school already has more than one current year, or two overlapping Active years, the script LISTS them and raises BEFORE changing anything.
--   6. Verification (raises, so the whole transaction rolls back, on any mismatch): per school the counts of years, current years and the dependants of a year
--      (enrolments, fee structures, dues + sums, attendance, timetable rows, class-teacher rows, homework, class tests) are exactly what they were before;
--      the new columns, check, index and exclusion constraint exist.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-acad-s1.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   3. The new API build and this script go together (the build maps the new columns). The extension btree_gist is needed (already used by the leave module).
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   Restore the pg_dump. (Or, before the new build is used:  ALTER TABLE tb_academic_years DROP CONSTRAINT ex_tb_ay_no_overlap,
--   DROP CONSTRAINT chk_tb_ay_closed_not_current, DROP COLUMN closed_at, DROP COLUMN closed_by, DROP COLUMN close_note;  DROP INDEX uq_tb_ay_one_current;)
-- =====================================================================================================================

BEGIN;

CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TEMP TABLE _s1_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _s1_before (school_id text PRIMARY KEY, years bigint, current_years bigint, enrolments bigint, structures bigint, dues bigint, dues_due numeric,
                              dues_paid numeric, attendance bigint, timetable bigint, class_teachers bigint, homework bigint, tests bigint) ON COMMIT DROP;

DO $s1$
DECLARE
    v_active    int;
    v_offenders text;
    v_mig       boolean;
    t           text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_users', 'lut_status', 'tb_academic_years', 'tb_student_enrollments', 'tb_fee_structures', 'tb_student_fee_dues',
                             'tb_student_attendance', 'tb_timetable_entries', 'tb_class_teacher_assignments', 'tb_homework', 'tb_class_assessments'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist. This is not the database this script is meant for. Nothing was changed.', t;
        END IF;
    END LOOP;

    SELECT sid INTO v_active FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status';
    IF v_active IS NULL THEN RAISE EXCEPTION 'lut_status has no Active / general status row. Nothing was changed.'; END IF;

    v_mig := NOT (
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_academic_years' AND column_name = 'closed_at')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_academic_years' AND column_name = 'closed_by')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_academic_years' AND column_name = 'close_note')
        AND EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'uq_tb_ay_one_current')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_tb_ay_no_overlap')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_ay_closed_not_current'));
    INSERT INTO _s1_state VALUES (v_mig);

    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    -- ---------------------------------------------------------------- pre-checks (offenders are listed, nothing is changed)
    SELECT string_agg(school_id || ' has ' || n || ' current years', '; ') INTO v_offenders
    FROM (SELECT school_id, count(*) n FROM public.tb_academic_years WHERE is_current GROUP BY school_id HAVING count(*) > 1) x;
    IF v_offenders IS NOT NULL THEN
        RAISE EXCEPTION 'More than one current academic year in: %. Fix these (keep exactly one is_current per school) and run again. Nothing was changed.', v_offenders;
    END IF;

    SELECT string_agg(a.school_id || ': ' || a.year_name || ' (' || a.start_date || ' to ' || a.end_date || ') overlaps ' || b.year_name || ' (' || b.start_date || ' to ' || b.end_date || ')', '; ')
      INTO v_offenders
    FROM public.tb_academic_years a JOIN public.tb_academic_years b
      ON a.school_id = b.school_id AND a.academic_year_id < b.academic_year_id
     AND a.start_date <= b.end_date AND b.start_date <= a.end_date
    WHERE a.status_id = v_active AND b.status_id = v_active;
    IF v_offenders IS NOT NULL THEN
        RAISE EXCEPTION 'Overlapping ACTIVE academic years: %. Change the dates or deactivate one of each pair, then run again. Nothing was changed.', v_offenders;
    END IF;

    RAISE NOTICE 'Pre-checks passed.';
END
$s1$;

INSERT INTO _s1_before
SELECT s.school_id::text,
       (SELECT count(*) FROM public.tb_academic_years y WHERE y.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_academic_years y WHERE y.school_id = s.school_id AND y.is_current),
       (SELECT count(*) FROM public.tb_student_enrollments e WHERE e.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_fee_structures f WHERE f.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_fee_dues d WHERE d.school_id = s.school_id),
       (SELECT coalesce(sum(d.amount_due), 0) FROM public.tb_student_fee_dues d WHERE d.school_id = s.school_id),
       (SELECT coalesce(sum(d.amount_paid), 0) FROM public.tb_student_fee_dues d WHERE d.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_attendance a WHERE a.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_timetable_entries t WHERE t.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_teacher_assignments c WHERE c.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_homework h WHERE h.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_assessments c WHERE c.school_id = s.school_id)
FROM public.tb_schools s;

-- ---------------------------------------------------------------- the changes (skipped on a re-run)
DO $s1ddl$
DECLARE
    v_active int;
BEGIN
    IF NOT (SELECT migrating FROM _s1_state) THEN RETURN; END IF;
    SELECT sid INTO v_active FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status';

    ALTER TABLE public.tb_academic_years ADD COLUMN IF NOT EXISTS closed_at  timestamptz NULL;
    ALTER TABLE public.tb_academic_years ADD COLUMN IF NOT EXISTS closed_by  bigint NULL;
    ALTER TABLE public.tb_academic_years ADD COLUMN IF NOT EXISTS close_note varchar(250) NULL;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_ay_closed_by') THEN
        ALTER TABLE public.tb_academic_years ADD CONSTRAINT fk_tb_ay_closed_by FOREIGN KEY (closed_by) REFERENCES public.tb_users (user_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_ay_closed_not_current') THEN
        ALTER TABLE public.tb_academic_years ADD CONSTRAINT chk_tb_ay_closed_not_current CHECK (closed_at IS NULL OR is_current = false);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_ay_close_note') THEN
        ALTER TABLE public.tb_academic_years ADD CONSTRAINT chk_tb_ay_close_note CHECK (close_note IS NULL OR length(btrim(close_note)) > 0);
    END IF;
    CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_ay_one_current ON public.tb_academic_years (school_id) WHERE is_current;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_tb_ay_no_overlap') THEN
        EXECUTE format('ALTER TABLE public.tb_academic_years ADD CONSTRAINT ex_tb_ay_no_overlap EXCLUDE USING gist (school_id WITH =, daterange(start_date, end_date, ''[]'') WITH &&) WHERE (status_id = %s)', v_active);
    END IF;
END
$s1ddl$;

-- ---------------------------------------------------------------- verification
DO $s1v$
DECLARE
    r        record;
    v_after  _s1_before;
    v_bad    text := '';
BEGIN
    IF NOT (SELECT migrating FROM _s1_state) THEN RETURN; END IF;

    FOR r IN SELECT * FROM _s1_before ORDER BY school_id LOOP
        SELECT s.school_id::text,
               (SELECT count(*) FROM public.tb_academic_years y WHERE y.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_academic_years y WHERE y.school_id = s.school_id AND y.is_current),
               (SELECT count(*) FROM public.tb_student_enrollments e WHERE e.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_fee_structures f WHERE f.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_fee_dues d WHERE d.school_id = s.school_id),
               (SELECT coalesce(sum(d.amount_due), 0) FROM public.tb_student_fee_dues d WHERE d.school_id = s.school_id),
               (SELECT coalesce(sum(d.amount_paid), 0) FROM public.tb_student_fee_dues d WHERE d.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_attendance a WHERE a.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_timetable_entries t WHERE t.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_class_teacher_assignments c WHERE c.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_homework h WHERE h.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_class_assessments c WHERE c.school_id = s.school_id)
          INTO v_after FROM public.tb_schools s WHERE s.school_id = r.school_id;
        IF v_after IS DISTINCT FROM r THEN
            v_bad := v_bad || format('%s: before=%s after=%s; ', r.school_id, r::text, v_after::text);
        END IF;
    END LOOP;
    IF v_bad <> '' THEN RAISE EXCEPTION 'VERIFICATION FAILED, everything is rolled back: %', v_bad; END IF;

    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_academic_years' AND column_name IN ('closed_at', 'closed_by', 'close_note')) <> 3
       OR NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'uq_tb_ay_one_current')
       OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_tb_ay_no_overlap')
       OR NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_ay_closed_not_current') THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a new column or constraint is missing. Everything is rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM public.tb_academic_years WHERE closed_at IS NOT NULL) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a year is closed right after the migration. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: per-school counts and sums are identical before and after; one-current index, no-overlap exclusion and close columns are in place.';
END
$s1v$;

COMMIT;

-- Per-school summary (shown in the Data Output tab)
SELECT s.school_id, s.school_name,
       (SELECT count(*) FROM public.tb_academic_years y WHERE y.school_id = s.school_id) AS years,
       (SELECT count(*) FROM public.tb_academic_years y WHERE y.school_id = s.school_id AND y.is_current) AS current_years,
       (SELECT count(*) FROM public.tb_academic_years y WHERE y.school_id = s.school_id AND y.closed_at IS NOT NULL) AS closed_years
FROM public.tb_schools s ORDER BY s.school_id;
