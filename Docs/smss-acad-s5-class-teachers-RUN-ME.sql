-- =====================================================================================================================
-- RUN-ME: Academic Years, stage S5 = CLASS TEACHER ASSIGNMENTS WITH HISTORY  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/academic-year-config-design.md (section C.9)
-- Needs: Docs/smss-acad-s1-years-RUN-ME.sql and Docs/smss-acad-s2-offerings-RUN-ME.sql to have been run first (btree_gist, the section offerings).
--
-- WHAT IT DOES
--   1. tb_class_teacher_assignments gets effective_from (DATE, NOT NULL), effective_to (DATE, NULL while current), assign_reason, ended_by (FK tb_users) and end_reason.
--      A row is NEVER deleted or edited in place afterwards: reassigning closes the old row (effective_to, ended_by, end_reason, status Inactive) and opens a new one.
--   2. CHECK effective_to >= effective_from; CHECK "status Active  <=>  effective_to IS NULL" (the two ways of saying "current" can never disagree; the status literal is resolved
--      by NAME when the constraint is created). The existing unique index "one Active row per section and year" stays.
--   3. DATABASE GUARD AGAINST OVERLAPPING HISTORY: EXCLUDE (school, year, section, daterange(effective_from, coalesce(effective_to, infinity), '[)')): two rows of one section and year
--      can never cover the same day (half-open, so closing one and opening the next on the same day is valid).
--   4. FK (academic_year_id, section_id) -> tb_academic_year_sections: a class teacher can only be assigned to a section that is offered in that year (S2).
--   5. BACKFILL RULE (existing rows, so the teacher dashboard and every reader keep working): per (school, year, section) the rows are put in creation order.
--      effective_from = the row's created_at as an India date. A row that has a LATER row in the same section and year ends the day that next row starts; the LAST row ends
--      never (effective_to NULL) when it is Active, and on its updated_at date (not before its start) when it is Inactive. Result: contiguous, non-overlapping history,
--      and the Active row of every section is still the same row, still current.
--   6. Pre-check (lists offenders, raises before changing anything): an Active row that is not the LAST row of its section and year (it would overlap a later row); a row of a section that
--      the year does not offer.
--   7. Verification (raises, so everything rolls back, on any mismatch): per school the rows, the Active rows, the sections with a teacher, class subjects, enrolments and attendance
--      rows are identical before and after; every Active row has effective_to NULL and every Inactive row has one; no two rows of a section and year overlap.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-acad-s5.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   3. Run the new API build right after: the old build inserts rows without effective_from and would fail against the NOT NULL column.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   Restore the pg_dump. (Or, before the new build is used: drop the exclusion ex_tb_cta_no_overlap, the checks chk_tb_cta_*, the FK fk_tb_cta_offered and the five new columns.)
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _s5_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _s5_before (school_id text PRIMARY KEY, assignments bigint, active_rows bigint, sections_with_teacher bigint, class_subjects bigint, enrolments bigint,
                              attendance bigint, timetable bigint) ON COMMIT DROP;

DO $s5$
DECLARE
    v_offenders text;
    v_mig       boolean;
    t           text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_users', 'lut_status', 'tb_academic_years', 'tb_academic_year_sections', 'tb_class_teacher_assignments', 'tb_class_subjects',
                             'tb_student_enrollments', 'tb_student_attendance', 'tb_timetable_entries'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist (run the S1 and S2 scripts first). Nothing was changed.', t;
        END IF;
    END LOOP;
    IF NOT EXISTS (SELECT 1 FROM lut_status WHERE sname = 'Active' AND stype = 'general status') THEN
        RAISE EXCEPTION 'lut_status has no Active / general status row. Nothing was changed.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist') THEN
        RAISE EXCEPTION 'The btree_gist extension is missing (Docs/smss-acad-s1-years-RUN-ME.sql creates it). Nothing was changed.';
    END IF;

    v_mig := NOT (EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_class_teacher_assignments' AND column_name = 'effective_from')
                  AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_tb_cta_no_overlap')
                  AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_cta_offered'));
    INSERT INTO _s5_state VALUES (v_mig);

    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    SELECT string_agg('school ' || a.school_id || ' section ' || a.section_id || ' year ' || a.academic_year_id || ': Active row ' || a.assignment_id || ' is followed by row ' || b.assignment_id, '; ') INTO v_offenders
    FROM public.tb_class_teacher_assignments a
    JOIN public.tb_class_teacher_assignments b ON b.school_id = a.school_id AND b.academic_year_id = a.academic_year_id AND b.section_id = a.section_id
         AND (b.created_at, b.assignment_id) > (a.created_at, a.assignment_id)
    WHERE a.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status');
    IF v_offenders IS NOT NULL THEN
        RAISE EXCEPTION 'An Active class-teacher row is followed by a later row of the same section and year (they would overlap): %. Fix these rows and run again. Nothing was changed.', v_offenders;
    END IF;

    SELECT string_agg('row ' || a.assignment_id || ' (school ' || a.school_id || ', section ' || a.section_id || ', year ' || a.academic_year_id || ')', '; ') INTO v_offenders
    FROM public.tb_class_teacher_assignments a
    WHERE NOT EXISTS (SELECT 1 FROM public.tb_academic_year_sections o WHERE o.academic_year_id = a.academic_year_id AND o.section_id = a.section_id);
    IF v_offenders IS NOT NULL THEN
        RAISE EXCEPTION 'A class-teacher row points at a section the year does not offer (run Docs/smss-acad-s2-offerings-RUN-ME.sql): %. Nothing was changed.', v_offenders;
    END IF;
    RAISE NOTICE 'Pre-checks passed.';
END
$s5$;

INSERT INTO _s5_before
SELECT s.school_id::text,
       (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id AND x.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')),
       (SELECT count(DISTINCT (x.academic_year_id, x.section_id)) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id AND x.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')),
       (SELECT count(*) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_attendance x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id)
FROM public.tb_schools s
WHERE (SELECT migrating FROM _s5_state);

-- ---------------------------------------------------------------- the changes (skipped on a re-run)
DO $s5ddl$
DECLARE
    v_active int;
BEGIN
    IF NOT (SELECT migrating FROM _s5_state) THEN RETURN; END IF;
    SELECT sid INTO v_active FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status';

    ALTER TABLE public.tb_class_teacher_assignments ADD COLUMN IF NOT EXISTS effective_from date NULL;
    ALTER TABLE public.tb_class_teacher_assignments ADD COLUMN IF NOT EXISTS effective_to   date NULL;
    ALTER TABLE public.tb_class_teacher_assignments ADD COLUMN IF NOT EXISTS assign_reason  varchar(250) NULL;
    ALTER TABLE public.tb_class_teacher_assignments ADD COLUMN IF NOT EXISTS ended_by       bigint NULL;
    ALTER TABLE public.tb_class_teacher_assignments ADD COLUMN IF NOT EXISTS end_reason     varchar(250) NULL;

    -- BACKFILL (see the header, point 5)
    WITH ordered AS (
        SELECT assignment_id, status_id,
               (created_at AT TIME ZONE 'Asia/Kolkata')::date AS d_from,
               lead((created_at AT TIME ZONE 'Asia/Kolkata')::date) OVER w AS next_from,
               (updated_at AT TIME ZONE 'Asia/Kolkata')::date AS d_upd
        FROM public.tb_class_teacher_assignments
        WINDOW w AS (PARTITION BY school_id, academic_year_id, section_id ORDER BY created_at, assignment_id)
    )
    UPDATE public.tb_class_teacher_assignments a
    SET effective_from = o.d_from,
        effective_to = CASE WHEN o.status_id = v_active THEN NULL
                            WHEN o.next_from IS NOT NULL THEN greatest(o.next_from, o.d_from)
                            ELSE greatest(o.d_upd, o.d_from) END
    FROM ordered o WHERE o.assignment_id = a.assignment_id;

    ALTER TABLE public.tb_class_teacher_assignments ALTER COLUMN effective_from SET NOT NULL;
    ALTER TABLE public.tb_class_teacher_assignments ADD CONSTRAINT chk_tb_cta_dates CHECK (effective_to IS NULL OR effective_to >= effective_from);
    EXECUTE format('ALTER TABLE public.tb_class_teacher_assignments ADD CONSTRAINT chk_tb_cta_active_is_open CHECK ((status_id = %s) = (effective_to IS NULL))', v_active);
    ALTER TABLE public.tb_class_teacher_assignments ADD CONSTRAINT fk_tb_cta_ended_by FOREIGN KEY (ended_by) REFERENCES public.tb_users (user_id);
    ALTER TABLE public.tb_class_teacher_assignments ADD CONSTRAINT fk_tb_cta_offered FOREIGN KEY (academic_year_id, section_id) REFERENCES public.tb_academic_year_sections (academic_year_id, section_id);
    ALTER TABLE public.tb_class_teacher_assignments ADD CONSTRAINT ex_tb_cta_no_overlap
        EXCLUDE USING gist (school_id WITH =, academic_year_id WITH =, section_id WITH =, daterange(effective_from, COALESCE(effective_to, 'infinity'::date), '[)') WITH &&);
    CREATE INDEX IF NOT EXISTS idx_tb_cta_section_year ON public.tb_class_teacher_assignments (school_id, academic_year_id, section_id, effective_from);
END
$s5ddl$;

-- ---------------------------------------------------------------- verification
DO $s5v$
DECLARE
    r       record;
    v_after _s5_before;
    v_bad   text := '';
    v_n     bigint;
BEGIN
    IF NOT (SELECT migrating FROM _s5_state) THEN RETURN; END IF;

    FOR r IN SELECT * FROM _s5_before ORDER BY school_id LOOP
        SELECT s.school_id::text,
               (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id AND x.effective_to IS NULL),
               (SELECT count(DISTINCT (x.academic_year_id, x.section_id)) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id AND x.effective_to IS NULL),
               (SELECT count(*) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_attendance x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id)
          INTO v_after FROM public.tb_schools s WHERE s.school_id = r.school_id;
        IF v_after IS DISTINCT FROM r THEN v_bad := v_bad || format('%s: before=%s after=%s; ', r.school_id, r::text, v_after::text); END IF;
    END LOOP;
    IF v_bad <> '' THEN RAISE EXCEPTION 'VERIFICATION FAILED, everything is rolled back: %', v_bad; END IF;

    SELECT count(*) INTO v_n FROM public.tb_class_teacher_assignments
    WHERE (status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')) <> (effective_to IS NULL);
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % row(s) whose status and effective_to disagree. Everything is rolled back.', v_n; END IF;

    SELECT count(*) INTO v_n FROM public.tb_class_teacher_assignments a JOIN public.tb_class_teacher_assignments b
        ON b.school_id = a.school_id AND b.academic_year_id = a.academic_year_id AND b.section_id = a.section_id AND b.assignment_id > a.assignment_id
       AND daterange(a.effective_from, COALESCE(a.effective_to, 'infinity'::date), '[)') && daterange(b.effective_from, COALESCE(b.effective_to, 'infinity'::date), '[)');
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % overlapping history pair(s). Everything is rolled back.', v_n; END IF;

    RAISE NOTICE 'VERIFIED: per-school counts are identical before and after; every Active row is open (effective_to NULL), every past row is closed; no overlapping history.';
END
$s5v$;

COMMIT;

-- Per-school summary (shown in the Data Output tab)
SELECT s.school_id, s.school_name,
       (SELECT count(*) FROM public.tb_class_teacher_assignments a WHERE a.school_id = s.school_id) AS rows,
       (SELECT count(*) FROM public.tb_class_teacher_assignments a WHERE a.school_id = s.school_id AND a.effective_to IS NULL) AS current_rows,
       (SELECT min(a.effective_from) FROM public.tb_class_teacher_assignments a WHERE a.school_id = s.school_id) AS earliest_from
FROM public.tb_schools s ORDER BY s.school_id;
