-- =====================================================================================================================
-- RUN-ME: Academic Years, stage S2 = class and section OFFERINGS per year  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/academic-year-config-design.md (sections C.3, C.4, C.5, D)
-- Needs: Docs/smss-acad-s1-years-RUN-ME.sql (closed years) to have been run first.
--
-- WHAT IT DOES
--   1. Helper keys UNIQUE (school_id, class_id) on tb_classes and UNIQUE (school_id, section_id) on tb_sections. They only repeat the primary key, so they can
--      never fail on existing data; child tables use them so a row of school A can never point at a class or section of school B.
--   2. tb_academic_year_classes  : which classes exist in which academic year (one row per year + class; Active = offered, Inactive = withdrawn).
--      tb_academic_year_sections : which sections exist in which year (one row per year + section), with an optional per-year capacity (NULL = the
--                                  section master's max_strength). A section can only be offered when its class is offered that year (composite FK).
--      The class and section MASTERS (tb_classes, tb_sections) are unchanged: same ids, names, order and status. Nothing that points at them is touched.
--   3. BACKFILL RULE (so that nothing changes for any user): every existing academic year of every school (current, past, future, active or not, closed or
--      not) gets one offering row for EVERY class and EVERY section of its school, with the master's status copied (a master with no status counts as Active).
--      That is exactly the old meaning ("every class and section applies to every year").
--   4. Pre-check: a section whose school differs from its class's school, or a class/section of a school that does not exist, is LISTED and the script raises
--      before changing anything.
--   5. Verification (raises, so everything rolls back, on any mismatch): per school the counts and sums of classes, sections, years, enrolments, fee structures,
--      dues (count + amount_due + amount_paid), class subjects, periods, timetable rows, class-teacher rows and attendance rows are identical before and after;
--      offering rows = years x classes and years x sections for every school; every offering row carries its master's status; no section is offered without
--      its class.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-acad-s2.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   3. Run the new API build right after: the old build knows nothing of the offerings (it keeps working, but Import Masters, the setup checklist and the
--      dashboards read the offerings only in the new build).
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   Restore the pg_dump. (Or, before the new build is used:  DROP TABLE tb_academic_year_sections; DROP TABLE tb_academic_year_classes;
--   ALTER TABLE tb_sections DROP CONSTRAINT uq_tb_sections_school_section;  ALTER TABLE tb_classes DROP CONSTRAINT uq_tb_classes_school_class;)
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _s2_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _s2_before (school_id text PRIMARY KEY, years bigint, classes bigint, sections bigint, enrolments bigint, structures bigint, dues bigint,
                              dues_due numeric, dues_paid numeric, class_subjects bigint, periods bigint, timetable bigint, class_teachers bigint, attendance bigint) ON COMMIT DROP;

DO $s2$
DECLARE
    v_offenders text;
    v_mig       boolean;
    t           text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_users', 'lut_status', 'tb_academic_years', 'tb_classes', 'tb_sections', 'tb_class_subjects', 'tb_periods',
                             'tb_student_enrollments', 'tb_fee_structures', 'tb_student_fee_dues', 'tb_student_attendance', 'tb_timetable_entries', 'tb_class_teacher_assignments'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist. This is not the database this script is meant for. Nothing was changed.', t;
        END IF;
    END LOOP;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_academic_years' AND column_name = 'closed_at') THEN
        RAISE EXCEPTION 'tb_academic_years.closed_at is missing: run Docs/smss-acad-s1-years-RUN-ME.sql first. Nothing was changed.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM lut_status WHERE sname = 'Active' AND stype = 'general status') THEN
        RAISE EXCEPTION 'lut_status has no Active / general status row. Nothing was changed.';
    END IF;

    v_mig := NOT (to_regclass('public.tb_academic_year_classes') IS NOT NULL AND to_regclass('public.tb_academic_year_sections') IS NOT NULL
                  AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_classes_school_class')
                  AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_sections_school_section'));
    INSERT INTO _s2_state VALUES (v_mig);

    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    SELECT string_agg('section ' || s.section_id || ' (school ' || s.school_id || ') belongs to class ' || c.class_id || ' of school ' || c.school_id, '; ') INTO v_offenders
    FROM public.tb_sections s JOIN public.tb_classes c ON c.class_id = s.class_id WHERE s.school_id <> c.school_id;
    IF v_offenders IS NOT NULL THEN
        RAISE EXCEPTION 'A section belongs to a class of ANOTHER school: %. Fix these rows and run again. Nothing was changed.', v_offenders;
    END IF;
    RAISE NOTICE 'Pre-checks passed.';
END
$s2$;

INSERT INTO _s2_before
SELECT s.school_id::text,
       (SELECT count(*) FROM public.tb_academic_years x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_classes x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_sections x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_fee_structures x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
       (SELECT coalesce(sum(x.amount_due), 0) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
       (SELECT coalesce(sum(x.amount_paid), 0) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_periods x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_attendance x WHERE x.school_id = s.school_id)
FROM public.tb_schools s;

-- ---------------------------------------------------------------- the changes (skipped on a re-run)
DO $s2ddl$
DECLARE
    v_active int;
BEGIN
    IF NOT (SELECT migrating FROM _s2_state) THEN RETURN; END IF;
    SELECT sid INTO v_active FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status';

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_classes_school_class') THEN
        ALTER TABLE public.tb_classes ADD CONSTRAINT uq_tb_classes_school_class UNIQUE (school_id, class_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_sections_school_section') THEN
        ALTER TABLE public.tb_sections ADD CONSTRAINT uq_tb_sections_school_section UNIQUE (school_id, section_id);
    END IF;

    CREATE TABLE IF NOT EXISTS public.tb_academic_year_classes (
        year_class_id    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        school_id        varchar(100) NOT NULL,
        academic_year_id bigint       NOT NULL,
        class_id         bigint       NOT NULL,
        status_id        integer      NOT NULL,
        created_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        created_by       bigint       NULL,
        updated_by       bigint       NULL,
        CONSTRAINT uq_tb_ayc_year_class UNIQUE (academic_year_id, class_id),
        CONSTRAINT fk_tb_ayc_year   FOREIGN KEY (school_id, academic_year_id) REFERENCES public.tb_academic_years (school_id, academic_year_id),
        CONSTRAINT fk_tb_ayc_class  FOREIGN KEY (school_id, class_id)         REFERENCES public.tb_classes (school_id, class_id),
        CONSTRAINT fk_tb_ayc_status FOREIGN KEY (status_id)                   REFERENCES public.lut_status (sid),
        CONSTRAINT fk_tb_ayc_creator FOREIGN KEY (created_by) REFERENCES public.tb_users (user_id),
        CONSTRAINT fk_tb_ayc_updater FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id)
    );
    CREATE INDEX IF NOT EXISTS idx_tb_ayc_school_year ON public.tb_academic_year_classes (school_id, academic_year_id);
    CREATE INDEX IF NOT EXISTS idx_tb_ayc_class       ON public.tb_academic_year_classes (class_id);

    CREATE TABLE IF NOT EXISTS public.tb_academic_year_sections (
        year_section_id  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        school_id        varchar(100) NOT NULL,
        academic_year_id bigint       NOT NULL,
        class_id         bigint       NOT NULL,
        section_id       bigint       NOT NULL,
        max_strength     smallint     NULL,
        status_id        integer      NOT NULL,
        created_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        created_by       bigint       NULL,
        updated_by       bigint       NULL,
        CONSTRAINT uq_tb_ays_year_section UNIQUE (academic_year_id, section_id),
        CONSTRAINT chk_tb_ays_max_strength CHECK (max_strength IS NULL OR max_strength > 0),
        CONSTRAINT fk_tb_ays_year    FOREIGN KEY (school_id, academic_year_id)       REFERENCES public.tb_academic_years (school_id, academic_year_id),
        CONSTRAINT fk_tb_ays_class   FOREIGN KEY (academic_year_id, class_id)        REFERENCES public.tb_academic_year_classes (academic_year_id, class_id),
        CONSTRAINT fk_tb_ays_section FOREIGN KEY (section_id, class_id)              REFERENCES public.tb_sections (section_id, class_id),
        CONSTRAINT fk_tb_ays_school_section FOREIGN KEY (school_id, section_id)      REFERENCES public.tb_sections (school_id, section_id),
        CONSTRAINT fk_tb_ays_status  FOREIGN KEY (status_id)                         REFERENCES public.lut_status (sid),
        CONSTRAINT fk_tb_ays_creator FOREIGN KEY (created_by) REFERENCES public.tb_users (user_id),
        CONSTRAINT fk_tb_ays_updater FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id)
    );
    CREATE INDEX IF NOT EXISTS idx_tb_ays_school_year ON public.tb_academic_year_sections (school_id, academic_year_id);
    CREATE INDEX IF NOT EXISTS idx_tb_ays_section     ON public.tb_academic_year_sections (section_id);

    -- BACKFILL: every year x every class / section of the same school; the master's status is copied (no status = Active)
    INSERT INTO public.tb_academic_year_classes (school_id, academic_year_id, class_id, status_id)
    SELECT y.school_id, y.academic_year_id, c.class_id, coalesce(c.status_id, v_active)
    FROM public.tb_academic_years y JOIN public.tb_classes c ON c.school_id = y.school_id
    ON CONFLICT (academic_year_id, class_id) DO NOTHING;

    INSERT INTO public.tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, max_strength, status_id)
    SELECT y.school_id, y.academic_year_id, s.class_id, s.section_id, NULL, coalesce(s.status_id, v_active)
    FROM public.tb_academic_years y JOIN public.tb_sections s ON s.school_id = y.school_id
    ON CONFLICT (academic_year_id, section_id) DO NOTHING;
END
$s2ddl$;

-- ---------------------------------------------------------------- verification
DO $s2v$
DECLARE
    r       record;
    v_after _s2_before;
    v_bad   text := '';
    v_n     bigint;
BEGIN
    IF NOT (SELECT migrating FROM _s2_state) THEN RETURN; END IF;

    FOR r IN SELECT * FROM _s2_before ORDER BY school_id LOOP
        SELECT s.school_id::text,
               (SELECT count(*) FROM public.tb_academic_years x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_classes x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_sections x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_fee_structures x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
               (SELECT coalesce(sum(x.amount_due), 0) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
               (SELECT coalesce(sum(x.amount_paid), 0) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_periods x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_attendance x WHERE x.school_id = s.school_id)
          INTO v_after FROM public.tb_schools s WHERE s.school_id = r.school_id;
        IF v_after IS DISTINCT FROM r THEN
            v_bad := v_bad || format('%s: before=%s after=%s; ', r.school_id, r::text, v_after::text);
        END IF;

        SELECT count(*) INTO v_n FROM public.tb_academic_year_classes x WHERE x.school_id = r.school_id;
        IF v_n <> r.years * r.classes THEN v_bad := v_bad || format('%s: class offerings %s, expected %s x %s; ', r.school_id, v_n, r.years, r.classes); END IF;
        SELECT count(*) INTO v_n FROM public.tb_academic_year_sections x WHERE x.school_id = r.school_id;
        IF v_n <> r.years * r.sections THEN v_bad := v_bad || format('%s: section offerings %s, expected %s x %s; ', r.school_id, v_n, r.years, r.sections); END IF;
    END LOOP;
    IF v_bad <> '' THEN RAISE EXCEPTION 'VERIFICATION FAILED, everything is rolled back: %', v_bad; END IF;

    SELECT count(*) INTO v_n FROM public.tb_academic_year_classes o JOIN public.tb_classes c ON c.class_id = o.class_id
    WHERE o.status_id IS DISTINCT FROM coalesce(c.status_id, (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status'));
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % class offering(s) do not carry their class''s status. Everything is rolled back.', v_n; END IF;
    SELECT count(*) INTO v_n FROM public.tb_academic_year_sections o JOIN public.tb_sections s ON s.section_id = o.section_id
    WHERE o.status_id IS DISTINCT FROM coalesce(s.status_id, (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status'));
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % section offering(s) do not carry their section''s status. Everything is rolled back.', v_n; END IF;
    SELECT count(*) INTO v_n FROM public.tb_academic_year_sections o
    WHERE NOT EXISTS (SELECT 1 FROM public.tb_academic_year_classes c WHERE c.academic_year_id = o.academic_year_id AND c.class_id = o.class_id);
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % section(s) offered without their class. Everything is rolled back.', v_n; END IF;

    RAISE NOTICE 'VERIFIED: per-school counts and sums are identical before and after; offerings = years x classes and years x sections for every school.';
END
$s2v$;

COMMIT;

-- Per-school summary (shown in the Data Output tab)
SELECT s.school_id, s.school_name,
       (SELECT count(*) FROM public.tb_academic_years y WHERE y.school_id = s.school_id) AS years,
       (SELECT count(*) FROM public.tb_classes c WHERE c.school_id = s.school_id) AS classes,
       (SELECT count(*) FROM public.tb_sections c WHERE c.school_id = s.school_id) AS sections,
       (SELECT count(*) FROM public.tb_academic_year_classes o WHERE o.school_id = s.school_id) AS class_offerings,
       (SELECT count(*) FROM public.tb_academic_year_sections o WHERE o.school_id = s.school_id) AS section_offerings
FROM public.tb_schools s ORDER BY s.school_id;
