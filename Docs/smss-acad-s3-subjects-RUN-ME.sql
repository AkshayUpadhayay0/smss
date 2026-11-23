-- =====================================================================================================================
-- RUN-ME: Academic Years, stage S3 = SUBJECTS PER CLASS PER YEAR + section overrides  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/academic-year-config-design.md (sections C.6, C.7, D)
-- Needs: Docs/smss-acad-s1-years-RUN-ME.sql and Docs/smss-acad-s2-offerings-RUN-ME.sql to have been run first.
--
-- WHAT IT DOES
--   1. tb_class_subjects gets academic_year_id (NOT NULL). The old UNIQUE (class_id, subject_id) becomes UNIQUE (academic_year_id, class_id, subject_id).
--      A mapping can only exist for a class that is offered that year (composite FK to tb_academic_year_classes), for a year and a subject of the SAME school.
--   2. BACKFILL RULE (so that nothing changes for any user): every existing mapping row is kept for EVERY academic year of its school that OFFERS its class
--      (S2 offered every class in every year that existed then; a year created after S2 offers only what its school chose). The row that exists today stays
--      (same class_subject_id) and moves to the school's EARLIEST year that offers the class (by start date, then id); a copy of it, with the same status, is
--      inserted for every other such year. Result: every year has exactly the subjects every year had before, which is the old meaning ("a class has these
--      subjects, in every year"). A mapping whose class no year offers (or a school with mappings but no year) is a pre-check offender: it is listed and
--      nothing is changed.
--   3. tb_section_subject_overrides (new): for ONE section in ONE year, ADD a subject its class does not have, or REMOVE one its class has. Starts empty.
--   4. vw_section_effective_subjects (new view) = the ONE definition of "what a section is taught in a year":
--           class mapping (Active) - REMOVE overrides (Active) + ADD overrides (Active), for sections and classes that are offered (Active) in that year.
--      Every reader of subject mappings (timetable, homework, exams, report marks) uses it, so the rule cannot drift between modules.
--   5. UNIQUE (school_id, subject_id) on tb_subjects (repeats the primary key; cannot fail) so child rows can never point at another school's subject.
--   6. Pre-check (lists offenders, raises before changing anything): a mapping whose class or subject belongs to a different school than the row; a school that has
--      mappings but no academic year; a mapping of a class for which a year has no class offering (cannot happen after S2; checked anyway).
--   7. Verification (raises, so everything rolls back, on any mismatch): per school the counts and sums of classes, sections, years, enrolments, fee structures,
--      dues, periods, timetable rows, class-teacher rows and attendance rows are identical before and after; class_subjects after = rows before x the years that offer their class; the distinct
--      (class, subject) pairs are the same set; the overrides table is empty; no mapping lacks its class offering.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-acad-s3.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   3. Run the new API build right after: the old build inserts class-subject rows without a year and would fail against the NOT NULL column.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   Restore the pg_dump. (Or, before the new build is used: DROP VIEW vw_section_effective_subjects; DROP TABLE tb_section_subject_overrides;
--   DELETE FROM tb_class_subjects WHERE academic_year_id <> (earliest year of that school); then drop the year column and its constraints and re-create
--   UNIQUE (class_id, subject_id).)
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _s3_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _s3_before (school_id text PRIMARY KEY, years bigint, classes bigint, sections bigint, enrolments bigint, structures bigint, dues bigint,
                              dues_due numeric, dues_paid numeric, class_subjects bigint, pairs bigint, periods bigint, timetable bigint, class_teachers bigint,
                              attendance bigint, expected_rows bigint) ON COMMIT DROP;

DO $s3$
DECLARE
    v_offenders text;
    v_mig       boolean;
    t           text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_users', 'lut_status', 'tb_academic_years', 'tb_classes', 'tb_sections', 'tb_subjects', 'tb_class_subjects', 'tb_periods',
                             'tb_student_enrollments', 'tb_fee_structures', 'tb_student_fee_dues', 'tb_student_attendance', 'tb_timetable_entries',
                             'tb_class_teacher_assignments', 'tb_academic_year_classes', 'tb_academic_year_sections'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist (for the offering tables: run Docs/smss-acad-s1-years-RUN-ME.sql and Docs/smss-acad-s2-offerings-RUN-ME.sql first). Nothing was changed.', t;
        END IF;
    END LOOP;
    IF NOT EXISTS (SELECT 1 FROM lut_status WHERE sname = 'Active' AND stype = 'general status') THEN
        RAISE EXCEPTION 'lut_status has no Active / general status row. Nothing was changed.';
    END IF;

    v_mig := NOT (EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_class_subjects' AND column_name = 'academic_year_id')
                  AND to_regclass('public.tb_section_subject_overrides') IS NOT NULL
                  AND to_regclass('public.vw_section_effective_subjects') IS NOT NULL
                  AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_class_subjects_year_class_subject'));
    INSERT INTO _s3_state VALUES (v_mig);

    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    -- (only meaningful on the first run: afterwards the year column exists)
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_class_subjects' AND column_name = 'academic_year_id') THEN
        SELECT string_agg('class_subject ' || m.class_subject_id || ' (school ' || m.school_id || ') points at class ' || m.class_id || ' of school ' || c.school_id, '; ') INTO v_offenders
        FROM public.tb_class_subjects m JOIN public.tb_classes c ON c.class_id = m.class_id WHERE c.school_id <> m.school_id;
        IF v_offenders IS NOT NULL THEN
            RAISE EXCEPTION 'A class-subject mapping points at a class of ANOTHER school: %. Fix these rows and run again. Nothing was changed.', v_offenders;
        END IF;
        SELECT string_agg('class_subject ' || m.class_subject_id || ' (school ' || m.school_id || ') points at subject ' || m.subject_id || ' of school ' || s.school_id, '; ') INTO v_offenders
        FROM public.tb_class_subjects m JOIN public.tb_subjects s ON s.subject_id = m.subject_id WHERE s.school_id <> m.school_id;
        IF v_offenders IS NOT NULL THEN
            RAISE EXCEPTION 'A class-subject mapping points at a subject of ANOTHER school: %. Fix these rows and run again. Nothing was changed.', v_offenders;
        END IF;
        SELECT string_agg(school_id || ' (' || n || ' mappings)', ', ') INTO v_offenders
        FROM (SELECT m.school_id, count(*) AS n FROM public.tb_class_subjects m
              WHERE NOT EXISTS (SELECT 1 FROM public.tb_academic_years y WHERE y.school_id = m.school_id) GROUP BY m.school_id) x;
        IF v_offenders IS NOT NULL THEN
            RAISE EXCEPTION 'These schools have class-subject mappings but NO academic year, so the mappings cannot be given a year: %. Create the year (or delete the mappings) and run again. Nothing was changed.', v_offenders;
        END IF;
        SELECT string_agg('class_subject ' || m.class_subject_id || ' (school ' || m.school_id || ', class ' || m.class_id || ')', '; ') INTO v_offenders
        FROM public.tb_class_subjects m
        WHERE NOT EXISTS (SELECT 1 FROM public.tb_academic_year_classes o WHERE o.school_id = m.school_id AND o.class_id = m.class_id);
        IF v_offenders IS NOT NULL THEN
            RAISE EXCEPTION 'A mapped class is offered in NO academic year, so its mapping cannot be given a year (run Docs/smss-acad-s2-offerings-RUN-ME.sql, or offer the class in a year): %. Nothing was changed.', v_offenders;
        END IF;
    END IF;
    RAISE NOTICE 'Pre-checks passed.';
END
$s3$;

INSERT INTO _s3_before
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
       (SELECT count(DISTINCT (x.class_id, x.subject_id)) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_periods x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_attendance x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_subjects m JOIN public.tb_academic_year_classes o ON o.school_id = m.school_id AND o.class_id = m.class_id WHERE m.school_id = s.school_id)
FROM public.tb_schools s
WHERE (SELECT migrating FROM _s3_state);

-- ---------------------------------------------------------------- the changes (skipped on a re-run)
DO $s3ddl$
BEGIN
    IF NOT (SELECT migrating FROM _s3_state) THEN RETURN; END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_subjects_school_subject') THEN
        ALTER TABLE public.tb_subjects ADD CONSTRAINT uq_tb_subjects_school_subject UNIQUE (school_id, subject_id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_class_subjects' AND column_name = 'academic_year_id') THEN
        ALTER TABLE public.tb_class_subjects ADD COLUMN academic_year_id bigint NULL;

        -- the existing row moves to the earliest year of the school that OFFERS its class ...
        UPDATE public.tb_class_subjects m
        SET academic_year_id = (SELECT y.academic_year_id FROM public.tb_academic_years y
                                JOIN public.tb_academic_year_classes o ON o.academic_year_id = y.academic_year_id AND o.class_id = m.class_id
                                WHERE y.school_id = m.school_id
                                ORDER BY y.start_date, y.academic_year_id LIMIT 1);
        -- ... and every other year that offers the class gets a copy (a year that does not offer the class cannot hold a mapping for it)
        INSERT INTO public.tb_class_subjects (school_id, class_id, subject_id, status_id, academic_year_id, created_at, updated_at)
        SELECT m.school_id, m.class_id, m.subject_id, m.status_id, o.academic_year_id, m.created_at, m.updated_at
        FROM public.tb_class_subjects m
        JOIN public.tb_academic_year_classes o ON o.school_id = m.school_id AND o.class_id = m.class_id AND o.academic_year_id <> m.academic_year_id;

        ALTER TABLE public.tb_class_subjects ALTER COLUMN academic_year_id SET NOT NULL;
    END IF;

    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_class_subjects_class_subject') THEN
        ALTER TABLE public.tb_class_subjects DROP CONSTRAINT uq_tb_class_subjects_class_subject;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_class_subjects_year_class_subject') THEN
        ALTER TABLE public.tb_class_subjects ADD CONSTRAINT uq_tb_class_subjects_year_class_subject UNIQUE (academic_year_id, class_id, subject_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_class_subjects_year') THEN
        ALTER TABLE public.tb_class_subjects ADD CONSTRAINT fk_tb_class_subjects_year FOREIGN KEY (school_id, academic_year_id) REFERENCES public.tb_academic_years (school_id, academic_year_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_class_subjects_offering') THEN
        ALTER TABLE public.tb_class_subjects ADD CONSTRAINT fk_tb_class_subjects_offering FOREIGN KEY (academic_year_id, class_id) REFERENCES public.tb_academic_year_classes (academic_year_id, class_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_class_subjects_school_class') THEN
        ALTER TABLE public.tb_class_subjects ADD CONSTRAINT fk_tb_class_subjects_school_class FOREIGN KEY (school_id, class_id) REFERENCES public.tb_classes (school_id, class_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_class_subjects_school_subject') THEN
        ALTER TABLE public.tb_class_subjects ADD CONSTRAINT fk_tb_class_subjects_school_subject FOREIGN KEY (school_id, subject_id) REFERENCES public.tb_subjects (school_id, subject_id);
    END IF;
    CREATE INDEX IF NOT EXISTS idx_tb_class_subjects_year_class ON public.tb_class_subjects (school_id, academic_year_id, class_id);

    CREATE TABLE IF NOT EXISTS public.tb_section_subject_overrides (
        override_id      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        school_id        varchar(100) NOT NULL,
        academic_year_id bigint       NOT NULL,
        class_id         bigint       NOT NULL,
        section_id       bigint       NOT NULL,
        subject_id       bigint       NOT NULL,
        action           varchar(6)   NOT NULL,
        status_id        integer      NOT NULL,
        created_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        created_by       bigint       NULL,
        updated_by       bigint       NULL,
        CONSTRAINT chk_tb_sso_action CHECK (action IN ('ADD', 'REMOVE')),
        CONSTRAINT uq_tb_sso_year_section_subject UNIQUE (academic_year_id, section_id, subject_id),
        CONSTRAINT fk_tb_sso_year    FOREIGN KEY (school_id, academic_year_id)  REFERENCES public.tb_academic_years (school_id, academic_year_id),
        CONSTRAINT fk_tb_sso_offered FOREIGN KEY (academic_year_id, section_id) REFERENCES public.tb_academic_year_sections (academic_year_id, section_id),
        CONSTRAINT fk_tb_sso_section FOREIGN KEY (section_id, class_id)         REFERENCES public.tb_sections (section_id, class_id),
        CONSTRAINT fk_tb_sso_subject FOREIGN KEY (school_id, subject_id)        REFERENCES public.tb_subjects (school_id, subject_id),
        CONSTRAINT fk_tb_sso_status  FOREIGN KEY (status_id)                    REFERENCES public.lut_status (sid),
        CONSTRAINT fk_tb_sso_creator FOREIGN KEY (created_by) REFERENCES public.tb_users (user_id),
        CONSTRAINT fk_tb_sso_updater FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id)
    );
    CREATE INDEX IF NOT EXISTS idx_tb_sso_school_year ON public.tb_section_subject_overrides (school_id, academic_year_id, class_id);

    -- THE one definition of a section's subjects in a year (statuses resolved by NAME, never by id)
    CREATE OR REPLACE VIEW public.vw_section_effective_subjects AS
    WITH act AS (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')
    SELECT ays.school_id, ays.academic_year_id, ays.class_id, ays.section_id, cs.subject_id, 'CLASS'::varchar(5) AS source
    FROM public.tb_academic_year_sections ays
    JOIN public.tb_academic_year_classes ayc ON ayc.academic_year_id = ays.academic_year_id AND ayc.class_id = ays.class_id
    JOIN public.tb_class_subjects cs ON cs.academic_year_id = ays.academic_year_id AND cs.class_id = ays.class_id
    WHERE ays.status_id IN (SELECT sid FROM act) AND ayc.status_id IN (SELECT sid FROM act) AND cs.status_id IN (SELECT sid FROM act)
      AND NOT EXISTS (SELECT 1 FROM public.tb_section_subject_overrides o
                      WHERE o.academic_year_id = ays.academic_year_id AND o.section_id = ays.section_id AND o.subject_id = cs.subject_id
                        AND o.action = 'REMOVE' AND o.status_id IN (SELECT sid FROM act))
    UNION ALL
    SELECT ays.school_id, ays.academic_year_id, ays.class_id, ays.section_id, o.subject_id, 'ADD'::varchar(5) AS source
    FROM public.tb_section_subject_overrides o
    JOIN public.tb_academic_year_sections ays ON ays.academic_year_id = o.academic_year_id AND ays.section_id = o.section_id
    JOIN public.tb_academic_year_classes ayc ON ayc.academic_year_id = ays.academic_year_id AND ayc.class_id = ays.class_id
    WHERE o.action = 'ADD' AND o.status_id IN (SELECT sid FROM act) AND ays.status_id IN (SELECT sid FROM act) AND ayc.status_id IN (SELECT sid FROM act)
      AND NOT EXISTS (SELECT 1 FROM public.tb_class_subjects cs
                      WHERE cs.academic_year_id = o.academic_year_id AND cs.class_id = ays.class_id AND cs.subject_id = o.subject_id AND cs.status_id IN (SELECT sid FROM act));
END
$s3ddl$;

-- ---------------------------------------------------------------- verification
DO $s3v$
DECLARE
    r       record;
    v_after _s3_before;
    v_bad   text := '';
    v_n     bigint;
BEGIN
    IF NOT (SELECT migrating FROM _s3_state) THEN RETURN; END IF;

    FOR r IN SELECT * FROM _s3_before ORDER BY school_id LOOP
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
               (SELECT count(DISTINCT (x.class_id, x.subject_id)) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_periods x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_attendance x WHERE x.school_id = s.school_id),
               r.expected_rows
          INTO v_after FROM public.tb_schools s WHERE s.school_id = r.school_id;

        -- everything but the class_subjects count must be identical; class_subjects must be (before rows x years that offer their class)
        IF (v_after.years, v_after.classes, v_after.sections, v_after.enrolments, v_after.structures, v_after.dues, v_after.dues_due, v_after.dues_paid, v_after.pairs,
            v_after.periods, v_after.timetable, v_after.class_teachers, v_after.attendance)
           IS DISTINCT FROM
           (r.years, r.classes, r.sections, r.enrolments, r.structures, r.dues, r.dues_due, r.dues_paid, r.pairs, r.periods, r.timetable, r.class_teachers, r.attendance)
        THEN
            v_bad := v_bad || format('%s: before=%s after=%s; ', r.school_id, r::text, v_after::text);
        END IF;
        IF v_after.class_subjects <> r.expected_rows THEN
            v_bad := v_bad || format('%s: class_subjects %s, expected %s (rows before x years offering their class); ', r.school_id, v_after.class_subjects, r.expected_rows);
        END IF;

    END LOOP;
    IF v_bad <> '' THEN RAISE EXCEPTION 'VERIFICATION FAILED, everything is rolled back: %', v_bad; END IF;

    SELECT count(*) INTO v_n FROM public.tb_section_subject_overrides;
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: the overrides table should start empty, it has % rows. Everything is rolled back.', v_n; END IF;

    SELECT count(*) INTO v_n FROM public.tb_class_subjects m
    WHERE NOT EXISTS (SELECT 1 FROM public.tb_academic_year_classes o WHERE o.academic_year_id = m.academic_year_id AND o.class_id = m.class_id);
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % mapping(s) without a class offering. Everything is rolled back.', v_n; END IF;

    -- with no overrides, a section's effective subjects equal its class's mapping, for every year (counts per school)
    SELECT count(*) INTO v_n FROM (
        SELECT ays.school_id, ays.academic_year_id, ays.section_id,
               (SELECT count(*) FROM public.vw_section_effective_subjects v WHERE v.academic_year_id = ays.academic_year_id AND v.section_id = ays.section_id) AS eff,
               (SELECT count(*) FROM public.tb_class_subjects m WHERE m.academic_year_id = ays.academic_year_id AND m.class_id = ays.class_id) AS mapped
        FROM public.tb_academic_year_sections ays
        WHERE ays.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')
          AND EXISTS (SELECT 1 FROM public.tb_academic_year_classes c WHERE c.academic_year_id = ays.academic_year_id AND c.class_id = ays.class_id
                      AND c.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status'))
    ) q WHERE q.eff <> q.mapped
      AND (SELECT count(*) FROM public.tb_class_subjects m WHERE m.academic_year_id = q.academic_year_id AND m.status_id IS DISTINCT FROM
           (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')) = 0;
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % section(s) whose effective subjects differ from their class mapping. Everything is rolled back.', v_n; END IF;

    RAISE NOTICE 'VERIFIED: per-school counts and sums are identical before and after; class subjects = rows before x years offering their class, same distinct pairs; effective subjects = class mapping.';
END
$s3v$;

COMMIT;

-- Per-school summary (shown in the Data Output tab)
SELECT s.school_id, s.school_name,
       (SELECT count(*) FROM public.tb_academic_years y WHERE y.school_id = s.school_id) AS years,
       (SELECT count(DISTINCT (m.class_id, m.subject_id)) FROM public.tb_class_subjects m WHERE m.school_id = s.school_id) AS distinct_class_subject_pairs,
       (SELECT count(*) FROM public.tb_class_subjects m WHERE m.school_id = s.school_id) AS class_subject_rows,
       (SELECT count(*) FROM public.tb_section_subject_overrides o WHERE o.school_id = s.school_id) AS overrides,
       (SELECT count(*) FROM public.vw_section_effective_subjects v WHERE v.school_id = s.school_id) AS effective_rows
FROM public.tb_schools s ORDER BY s.school_id;
