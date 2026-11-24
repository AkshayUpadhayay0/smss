-- =====================================================================================================================
-- RUN-ME: Year continuity, stage 1a = HARDENING of enrolments and the class chain  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/student-year-promotion-design.md (sections 4 and 5, items 1 to 3), approved 24 Nov 2026
-- Needs: the S1 and S2 academic-year scripts (tb_academic_years, tb_academic_year_classes, tb_academic_year_sections).
--
-- WHAT IT DOES
--   1. public.fn_roll_key(text) (new, IMMUTABLE): the normalised form of a roll number: spaces trimmed, case ignored, and a purely numeric value without leading zeros
--      ("01" = "1" = " 1 "; "A-07" = "a-07"). The API uses the same rule (RollKey.Of), so the database and the service always agree.
--   2. tb_student_enrollments gets three composite foreign keys, so an enrolment can no longer point at another school's year or section, or at a section the year does not offer:
--        fk_tb_enrollments_year      (school_id, academic_year_id)        -> tb_academic_years (school_id, academic_year_id)
--        fk_tb_enrollments_section   (school_id, class_section_id)        -> tb_sections (school_id, section_id)
--        fk_tb_enrollments_offering  (academic_year_id, class_section_id) -> tb_academic_year_sections (academic_year_id, section_id)
--      The offering key does not look at the offering's Active / Inactive flag (withdrawal is a status change and the row stays), so the API also checks that the offering is Active.
--   3. uq_tb_enrollments_roll (new partial unique index): one roll number per section per year, compared with fn_roll_key; enrolments without a roll number are not affected.
--   4. tb_classes.next_class_id (new, nullable): the class a student normally moves to. Composite FK (school_id, next_class_id) -> tb_classes (school_id, class_id);
--      CHECK it is not the class itself; partial unique index (school_id, next_class_id), so no two classes share a next class. Loops are checked by the API and verified here.
--      BACKFILL (printed below): for every class whose name is in the Import Masters catalogue (Nursery, LKG, UKG, Class 1 .. Class 12), next_class_id = the catalogue-named class
--      with the next-higher sequence_order in the same school; the highest one stays NULL (its students pass out). Only NULL values are filled; nothing is overwritten.
--   5. fk_tb_ays_section on tb_academic_year_sections becomes DEFERRABLE INITIALLY DEFERRED, so moving an UNUSED section to another class updates its offering rows in place
--      (no offering row is ever deleted: withdrawal stays a status change).
--   6. Pre-checks (list the offending rows and raise BEFORE anything is changed): enrolments that violate any of the three foreign keys, and duplicate roll numbers (by fn_roll_key)
--      within one section and year.
--   7. Verification (raises, so everything rolls back, on any mismatch): enrolment, class and offering counts per school are identical before and after; all objects exist;
--      the next-class chain has no loop.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-yc-1a.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   3. Stop the API (or at least do not enrol students) while it runs.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   Restore the pg_dump, or:
--     ALTER TABLE tb_student_enrollments DROP CONSTRAINT fk_tb_enrollments_year, DROP CONSTRAINT fk_tb_enrollments_section, DROP CONSTRAINT fk_tb_enrollments_offering;
--     DROP INDEX uq_tb_enrollments_roll;  ALTER TABLE tb_classes DROP COLUMN next_class_id;  DROP FUNCTION fn_roll_key(text);
--     ALTER TABLE tb_academic_year_sections ALTER CONSTRAINT fk_tb_ays_section NOT DEFERRABLE;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _yc1a_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _yc1a_before (school_id text PRIMARY KEY, enrolments bigint, classes bigint, offerings bigint, class_offerings bigint) ON COMMIT DROP;

DO $yc1a$
DECLARE
    v_mig boolean;
    t     text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_students', 'tb_student_enrollments', 'tb_classes', 'tb_sections', 'tb_academic_years', 'tb_academic_year_classes', 'tb_academic_year_sections'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist (run the S1 and S2 academic-year scripts first). Nothing was changed.', t;
        END IF;
    END LOOP;

    v_mig := NOT (
        to_regprocedure('public.fn_roll_key(text)') IS NOT NULL
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_enrollments_year'     AND conrelid = 'public.tb_student_enrollments'::regclass)
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_enrollments_section'  AND conrelid = 'public.tb_student_enrollments'::regclass)
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_enrollments_offering' AND conrelid = 'public.tb_student_enrollments'::regclass)
        AND to_regclass('public.uq_tb_enrollments_roll') IS NOT NULL
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_classes' AND column_name = 'next_class_id')
        AND to_regclass('public.uq_tb_classes_school_next') IS NOT NULL
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_ays_section' AND condeferrable));
    INSERT INTO _yc1a_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;
    RAISE NOTICE 'Pre-checks will run now.';
END
$yc1a$;

-- 1. the roll-number key (created first: the duplicate pre-check uses it). Safe to replace: it is only a function.
CREATE OR REPLACE FUNCTION public.fn_roll_key(r text) RETURNS text
LANGUAGE sql IMMUTABLE PARALLEL SAFE AS
$f$ SELECT CASE WHEN btrim(r) ~ '^[0-9]+$' THEN COALESCE(NULLIF(ltrim(btrim(r), '0'), ''), '0') ELSE lower(btrim(r)) END $f$;

INSERT INTO _yc1a_before
SELECT s.school_id::text,
       (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_classes x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_academic_year_sections x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_academic_year_classes x WHERE x.school_id = s.school_id)
FROM public.tb_schools s;

-- 6. pre-checks: list the offenders and stop (changes nothing: this is one transaction and the script raises)
DO $yc1apre$
DECLARE
    v_mig boolean; v_list text;
BEGIN
    SELECT migrating INTO v_mig FROM _yc1a_state;
    IF NOT v_mig THEN RETURN; END IF;

    SELECT string_agg(format('enrolment %s (student %s, school %s, year %s)', e.enrollment_id, e.student_id, e.school_id, e.academic_year_id), '; ' ORDER BY e.enrollment_id) INTO v_list
    FROM (SELECT e.* FROM public.tb_student_enrollments e
          WHERE NOT EXISTS (SELECT 1 FROM public.tb_academic_years y WHERE y.school_id = e.school_id AND y.academic_year_id = e.academic_year_id) LIMIT 20) e;
    IF v_list IS NOT NULL THEN RAISE EXCEPTION 'PRE-CHECK FAILED (nothing was changed): enrolments whose academic year is missing or belongs to another school: %', v_list; END IF;

    SELECT string_agg(format('enrolment %s (student %s, school %s, section %s)', e.enrollment_id, e.student_id, e.school_id, e.class_section_id), '; ' ORDER BY e.enrollment_id) INTO v_list
    FROM (SELECT e.* FROM public.tb_student_enrollments e
          WHERE NOT EXISTS (SELECT 1 FROM public.tb_sections s WHERE s.school_id = e.school_id AND s.section_id = e.class_section_id) LIMIT 20) e;
    IF v_list IS NOT NULL THEN RAISE EXCEPTION 'PRE-CHECK FAILED (nothing was changed): enrolments whose section is missing or belongs to another school: %', v_list; END IF;

    SELECT string_agg(format('enrolment %s (student %s, year %s, section %s)', e.enrollment_id, e.student_id, e.academic_year_id, e.class_section_id), '; ' ORDER BY e.enrollment_id) INTO v_list
    FROM (SELECT e.* FROM public.tb_student_enrollments e
          WHERE NOT EXISTS (SELECT 1 FROM public.tb_academic_year_sections o WHERE o.academic_year_id = e.academic_year_id AND o.section_id = e.class_section_id) LIMIT 20) e;
    IF v_list IS NOT NULL THEN RAISE EXCEPTION 'PRE-CHECK FAILED (nothing was changed): enrolments in a section the year does not offer: %', v_list; END IF;

    SELECT string_agg(format('year %s section %s roll "%s": %s enrolments', d.academic_year_id, d.class_section_id, d.k, d.n), '; ') INTO v_list
    FROM (SELECT academic_year_id, class_section_id, public.fn_roll_key(roll_number) k, count(*) n FROM public.tb_student_enrollments
          WHERE roll_number IS NOT NULL AND btrim(roll_number) <> '' GROUP BY 1, 2, 3 HAVING count(*) > 1 LIMIT 20) d;
    IF v_list IS NOT NULL THEN RAISE EXCEPTION 'PRE-CHECK FAILED (nothing was changed): duplicate roll numbers in one section and year (compared without spaces, case or leading zeros): %', v_list; END IF;

    RAISE NOTICE 'Pre-checks passed: no enrolment violates the new foreign keys and no roll number is duplicated.';
END
$yc1apre$;

-- 2. enrolment foreign keys
DO $yc1afk$
BEGIN
    IF NOT (SELECT migrating FROM _yc1a_state) THEN RETURN; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_enrollments_year' AND conrelid = 'public.tb_student_enrollments'::regclass) THEN
        ALTER TABLE public.tb_student_enrollments ADD CONSTRAINT fk_tb_enrollments_year
            FOREIGN KEY (school_id, academic_year_id) REFERENCES public.tb_academic_years (school_id, academic_year_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_enrollments_section' AND conrelid = 'public.tb_student_enrollments'::regclass) THEN
        ALTER TABLE public.tb_student_enrollments ADD CONSTRAINT fk_tb_enrollments_section
            FOREIGN KEY (school_id, class_section_id) REFERENCES public.tb_sections (school_id, section_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_enrollments_offering' AND conrelid = 'public.tb_student_enrollments'::regclass) THEN
        ALTER TABLE public.tb_student_enrollments ADD CONSTRAINT fk_tb_enrollments_offering
            FOREIGN KEY (academic_year_id, class_section_id) REFERENCES public.tb_academic_year_sections (academic_year_id, section_id);
    END IF;
END
$yc1afk$;

-- 3. roll numbers
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_enrollments_roll
    ON public.tb_student_enrollments (academic_year_id, class_section_id, (public.fn_roll_key(roll_number)))
    WHERE roll_number IS NOT NULL AND btrim(roll_number) <> '';

-- 4. next class
ALTER TABLE public.tb_classes ADD COLUMN IF NOT EXISTS next_class_id bigint NULL;
DO $yc1anext$
BEGIN
    IF NOT (SELECT migrating FROM _yc1a_state) THEN RETURN; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_classes_next' AND conrelid = 'public.tb_classes'::regclass) THEN
        ALTER TABLE public.tb_classes ADD CONSTRAINT fk_tb_classes_next
            FOREIGN KEY (school_id, next_class_id) REFERENCES public.tb_classes (school_id, class_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_classes_next_not_self' AND conrelid = 'public.tb_classes'::regclass) THEN
        ALTER TABLE public.tb_classes ADD CONSTRAINT chk_tb_classes_next_not_self CHECK (next_class_id IS NULL OR next_class_id <> class_id);
    END IF;
END
$yc1anext$;
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_classes_school_next ON public.tb_classes (school_id, next_class_id) WHERE next_class_id IS NOT NULL;

-- backfill (catalogue-named classes only, only where NULL), printed row by row
DO $yc1abf$
DECLARE
    r record; v_n int := 0;
    v_catalogue text[] := ARRAY['Nursery','LKG','UKG','Class 1','Class 2','Class 3','Class 4','Class 5','Class 6','Class 7','Class 8','Class 9','Class 10','Class 11','Class 12'];
BEGIN
    IF NOT (SELECT migrating FROM _yc1a_state) THEN RETURN; END IF;
    FOR r IN
        SELECT c.school_id, c.class_id, c.class_name, n.class_id AS next_id, n.class_name AS next_name
        FROM public.tb_classes c
        JOIN LATERAL (SELECT x.class_id, x.class_name FROM public.tb_classes x
                      WHERE x.school_id = c.school_id AND x.class_name = ANY (v_catalogue) AND x.sequence_order > c.sequence_order
                      ORDER BY x.sequence_order, x.class_id LIMIT 1) n ON true
        WHERE c.class_name = ANY (v_catalogue) AND c.next_class_id IS NULL
        ORDER BY c.school_id, c.sequence_order
    LOOP
        UPDATE public.tb_classes SET next_class_id = r.next_id WHERE class_id = r.class_id AND next_class_id IS NULL;
        v_n := v_n + 1;
        RAISE NOTICE 'next_class_id set: % : % -> %', r.school_id, r.class_name, r.next_name;
    END LOOP;
    RAISE NOTICE 'next_class_id backfill: % classes set (the highest catalogue class of each school stays NULL).', v_n;
END
$yc1abf$;

-- 5. deferrable section key (moving an unused section updates its offering rows in place)
DO $yc1adef$
BEGIN
    IF NOT (SELECT migrating FROM _yc1a_state) THEN RETURN; END IF;
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_ays_section' AND NOT condeferrable) THEN
        ALTER TABLE public.tb_academic_year_sections ALTER CONSTRAINT fk_tb_ays_section DEFERRABLE INITIALLY DEFERRED;
    END IF;
END
$yc1adef$;

-- 7. verification
DO $yc1av$
DECLARE
    r record; v_after _yc1a_before; v_bad text := ''; v_loop int;
BEGIN
    IF NOT (SELECT migrating FROM _yc1a_state) THEN RETURN; END IF;
    FOR r IN SELECT * FROM _yc1a_before LOOP
        SELECT s.school_id::text,
               (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_classes x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_academic_year_sections x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_academic_year_classes x WHERE x.school_id = s.school_id)
          INTO v_after FROM public.tb_schools s WHERE s.school_id = r.school_id;
        IF v_after IS DISTINCT FROM r THEN v_bad := v_bad || format('%s: before=%s after=%s; ', r.school_id, r::text, v_after::text); END IF;
    END LOOP;
    IF v_bad <> '' THEN RAISE EXCEPTION 'VERIFICATION FAILED, everything is rolled back: %', v_bad; END IF;

    WITH RECURSIVE walk(start_id, cur, depth) AS (
        SELECT class_id, next_class_id, 1 FROM public.tb_classes WHERE next_class_id IS NOT NULL
        UNION ALL
        SELECT w.start_id, c.next_class_id, w.depth + 1 FROM walk w JOIN public.tb_classes c ON c.class_id = w.cur
        WHERE c.next_class_id IS NOT NULL AND w.depth < 100 AND w.cur <> w.start_id)
    SELECT count(*) INTO v_loop FROM walk WHERE cur = start_id;
    IF v_loop <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: the next-class chain has a loop. Everything is rolled back.'; END IF;

    IF to_regclass('public.uq_tb_enrollments_roll') IS NULL OR to_regclass('public.uq_tb_classes_school_next') IS NULL THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an index is missing. Everything is rolled back.'; END IF;
    RAISE NOTICE 'VERIFIED: per-school counts of enrolments, classes and offerings are identical before and after; the three enrolment keys, the roll index and the next-class objects exist; no loop.';
END
$yc1av$;

COMMIT;

SELECT s.school_id, s.school_name,
       (SELECT count(*) FROM public.tb_student_enrollments e WHERE e.school_id = s.school_id) AS enrolments,
       (SELECT count(*) FROM public.tb_classes c WHERE c.school_id = s.school_id) AS classes,
       (SELECT count(*) FROM public.tb_classes c WHERE c.school_id = s.school_id AND c.next_class_id IS NOT NULL) AS classes_with_next,
       (SELECT string_agg(c.class_name, ', ') FROM public.tb_classes c WHERE c.school_id = s.school_id AND c.next_class_id IS NULL) AS classes_without_next
FROM public.tb_schools s ORDER BY s.school_id;
