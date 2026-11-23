-- =====================================================================================================================
-- RUN-ME: Academic Years, stage S7 = YEAR TRANSITION (copy configuration from another year): the copy LOG table only  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/academic-year-config-design.md (sections C.11 and C.12)
-- Needs: S1 to S6 RUN-ME scripts (the copy writes into the S2 to S6 tables; this script itself only creates one new table).
--
-- WHAT IT DOES
--   1. tb_year_copy_log (new): one row per REAL copy run (a preview writes nothing): school, source year, target year, the parts selected (JSON), the per-part result counts (JSON),
--      COMPLETED | FAILED, a short error note, who ran it and when. CHECK source <> target; composite FKs to both years (so a log row can only name two years of the SAME school);
--      index (school, target year, started_at DESC).
--   2. Nothing existing is altered: no existing table, column, row or constraint is touched.
--   3. Pre-check (lists offenders, raises before changing anything): the S6 objects the copy relies on must exist (tb_timetables, tb_academic_year_schedules, tb_section_subject_overrides).
--   4. Verification (raises, so everything rolls back, on any mismatch): per school the counts of years, offerings, class subjects, schedule assignments, class-teacher rows and timetables are
--      identical before and after; the log table exists and is empty.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-acad-s7.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   Restore the pg_dump, or:  DROP TABLE tb_year_copy_log;   (nothing else depends on it)
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _s7_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _s7_before (school_id text PRIMARY KEY, years bigint, year_classes bigint, year_sections bigint, class_subjects bigint, overrides bigint, schedule_rows bigint,
                              class_teachers bigint, timetables bigint, entries bigint) ON COMMIT DROP;

DO $s7$
DECLARE
    v_mig boolean;
    t     text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_users', 'tb_academic_years', 'tb_academic_year_classes', 'tb_academic_year_sections', 'tb_class_subjects', 'tb_section_subject_overrides',
                             'tb_academic_year_schedules', 'tb_class_teacher_assignments', 'tb_timetables', 'tb_timetable_entries'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist (run the S1 to S6 scripts first). Nothing was changed.', t;
        END IF;
    END LOOP;

    v_mig := to_regclass('public.tb_year_copy_log') IS NULL;
    INSERT INTO _s7_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;
    RAISE NOTICE 'Pre-checks passed.';
END
$s7$;

INSERT INTO _s7_before
SELECT s.school_id::text,
       (SELECT count(*) FROM public.tb_academic_years x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_academic_year_classes x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_academic_year_sections x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_section_subject_overrides x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_academic_year_schedules x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_timetables x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id)
FROM public.tb_schools s
WHERE (SELECT migrating FROM _s7_state);

DO $s7ddl$
BEGIN
    IF NOT (SELECT migrating FROM _s7_state) THEN RETURN; END IF;

    CREATE TABLE public.tb_year_copy_log (
        copy_id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        school_id       varchar(100) NOT NULL,
        source_year_id  bigint       NOT NULL,
        target_year_id  bigint       NOT NULL,
        parts           jsonb        NOT NULL,
        result          jsonb        NOT NULL,
        status          varchar(10)  NOT NULL,
        error_note      varchar(250) NULL,
        run_by          bigint       NULL,
        started_at      timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        finished_at     timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        CONSTRAINT chk_tb_ycl_status    CHECK (status IN ('COMPLETED', 'FAILED')),
        CONSTRAINT chk_tb_ycl_years     CHECK (source_year_id <> target_year_id),
        CONSTRAINT fk_tb_ycl_school     FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
        CONSTRAINT fk_tb_ycl_source     FOREIGN KEY (school_id, source_year_id) REFERENCES public.tb_academic_years (school_id, academic_year_id),
        CONSTRAINT fk_tb_ycl_target     FOREIGN KEY (school_id, target_year_id) REFERENCES public.tb_academic_years (school_id, academic_year_id),
        CONSTRAINT fk_tb_ycl_user       FOREIGN KEY (run_by) REFERENCES public.tb_users (user_id)
    );
    CREATE INDEX idx_tb_ycl_target ON public.tb_year_copy_log (school_id, target_year_id, started_at DESC);
END
$s7ddl$;

DO $s7v$
DECLARE
    r       record;
    v_after _s7_before;
    v_bad   text := '';
    v_n     bigint;
BEGIN
    IF NOT (SELECT migrating FROM _s7_state) THEN RETURN; END IF;
    FOR r IN SELECT * FROM _s7_before ORDER BY school_id LOOP
        SELECT s.school_id::text,
               (SELECT count(*) FROM public.tb_academic_years x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_academic_year_classes x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_academic_year_sections x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_section_subject_overrides x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_academic_year_schedules x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_timetables x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id)
          INTO v_after FROM public.tb_schools s WHERE s.school_id = r.school_id;
        IF v_after IS DISTINCT FROM r THEN v_bad := v_bad || format('%s: before=%s after=%s; ', r.school_id, r::text, v_after::text); END IF;
    END LOOP;
    IF v_bad <> '' THEN RAISE EXCEPTION 'VERIFICATION FAILED, everything is rolled back: %', v_bad; END IF;
    SELECT count(*) INTO v_n FROM public.tb_year_copy_log;
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: the copy log should start empty. Everything is rolled back.'; END IF;
    RAISE NOTICE 'VERIFIED: per-school counts of years, offerings, subjects, schedule rows, class teachers, timetables and entries are identical before and after; tb_year_copy_log exists and is empty.';
END
$s7v$;

COMMIT;

SELECT s.school_id, s.school_name,
       (SELECT count(*) FROM public.tb_academic_years y WHERE y.school_id = s.school_id) AS years,
       (SELECT count(*) FROM public.tb_year_copy_log l WHERE l.school_id = s.school_id) AS copy_log_rows
FROM public.tb_schools s ORDER BY s.school_id;
