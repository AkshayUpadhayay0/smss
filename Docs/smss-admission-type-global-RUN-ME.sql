-- =====================================================================================================================
-- RUN-ME: Admission Type becomes a GLOBAL master  (one self-contained file for pgAdmin's Query Tool)
--
-- WHAT IT DOES
--   Creates the global table lut_admission_type with exactly two types (NEW_ADMISSION "New Admission", EXISTING_STUDENT "Existing Student"),
--   points every admission / student / enrollment at one of them (old name "existing student" -> Existing Student; "new admission" -> New Admission;
--   ANY OTHER old name is MERGED into New Admission and listed in the Messages tab), and keeps the old-id -> new-id record in mig_admission_type_map.
--   It does NOT drop the old table tb_admission_types (that is a separate, later script).
--   Everything is ONE transaction (BEGIN ... COMMIT below). If any check fails the whole thing is rolled back and the database is unchanged.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-admission-type.dump smss-db
--   2. Make sure you are connected to the RIGHT database. The script prints current_database() as its first message: read it.
--
-- HOW TO RUN IT IN pgAdmin
--   Open this file in the Query Tool of the database you want to change (right-click the database > Query Tool > open file),
--   run the whole file with F5 (Execute script), and read the "Messages" tab: you should see "VERIFIED" near the end and,
--   in the "Data Output" tab, the final table (type_code, type_name, is_active, how many admissions / students / enrollments use each).
--   If something fails, pgAdmin shows an error and the transaction is rolled back (if the Query Tool is left in a failed state, click Rollback).
--   Do not edit the file between BEGIN and COMMIT.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and says "ALREADY MIGRATED: nothing changed" in the Messages tab.
--
-- HOW TO ROLL BACK
--   Simplest and exact: restore the pg_dump taken in step 1.
--   Before step 2 has ever been run, the old table is untouched, so the reverse is also possible with the old ids kept in
--   mig_admission_type_map (see the header of smss-admission-type-global.sql; merged rows need the backup to be exact).
--
-- DO NOT RUN smss-admission-type-global-drop-old.sql YET. It deletes the old table. Run it only after the new API build has been live
-- and checked, and after another backup.
--
-- The block below the line "ORIGINAL SCRIPT" is the full text of smss-admission-type-global.sql, with its own BEGIN / COMMIT removed so
-- that there is exactly one transaction around everything.
-- =====================================================================================================================

BEGIN;

-- ---------------------------------------------------------------------------------------------------------------------
-- PRE-CHECKS (nothing has been changed yet)
-- ---------------------------------------------------------------------------------------------------------------------
DO $pre$
DECLARE
    v_old   boolean := to_regclass('public.tb_admission_types') IS NOT NULL;
    v_lut   boolean := to_regclass('public.lut_admission_type') IS NOT NULL;
    v_map   boolean := to_regclass('public.mig_admission_type_map') IS NOT NULL;
    v_done  boolean := false;
    v_bad   bigint;
    v_missing text;
    t       text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    -- already migrated? (the old table is still there, but everything is in the map and repointed)
    IF v_old AND v_lut AND v_map THEN
        EXECUTE 'SELECT NOT EXISTS (SELECT 1 FROM public.mig_admission_type_map WHERE repointed_at IS NULL)
                        AND NOT EXISTS (SELECT 1 FROM public.tb_admission_types o WHERE NOT EXISTS
                              (SELECT 1 FROM public.mig_admission_type_map m WHERE m.school_id = o.school_id AND m.old_id = o.admission_type_id))' INTO v_done;
    END IF;

    -- (a) the old table must exist (unless a previous run already dropped it through step 2, in which case there is nothing to do)
    IF NOT v_old THEN
        IF v_lut AND v_map THEN
            RAISE NOTICE 'tb_admission_types does not exist but lut_admission_type and mig_admission_type_map do: this looks already migrated (and step 2 already run).';
        ELSE
            RAISE EXCEPTION 'tb_admission_types does not exist (and there is no lut_admission_type / mig_admission_type_map). This is not the database this script is meant for. Nothing was changed.';
        END IF;
    ELSE
        SELECT string_agg(c, ', ') INTO v_missing FROM unnest(ARRAY['school_id', 'admission_type_id', 'type_name']) c
        WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_admission_types' AND column_name = c);
        IF v_missing IS NOT NULL THEN RAISE EXCEPTION 'tb_admission_types is missing column(s): %. Not the expected schema. Nothing was changed.', v_missing; END IF;
    END IF;

    FOREACH t IN ARRAY ARRAY['tb_admissions', 'tb_students', 'tb_student_enrollments'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN RAISE EXCEPTION 'Table % does not exist. Not the expected schema. Nothing was changed.', t; END IF;
        IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = t AND column_name = 'admission_type_id')
           OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = t AND column_name = 'school_id') THEN
            RAISE EXCEPTION 'Table % has no admission_type_id / school_id column. Not the expected schema. Nothing was changed.', t;
        END IF;
    END LOOP;

    -- (b) lut_admission_type, if it already exists, must have the right shape
    IF v_lut THEN
        SELECT string_agg(e.col, ', ') INTO v_missing
        FROM (VALUES ('admission_type_id', 'bigint'), ('type_code', 'character varying'), ('type_name', 'character varying'), ('description', 'character varying'),
                     ('is_active', 'boolean'), ('created_at', 'timestamp with time zone'), ('updated_at', 'timestamp with time zone')) AS e(col, typ)
        WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns c WHERE c.table_schema = 'public' AND c.table_name = 'lut_admission_type'
                          AND c.column_name = e.col AND c.data_type = e.typ);
        IF v_missing IS NOT NULL THEN
            RAISE EXCEPTION 'lut_admission_type already exists but with the wrong shape (missing or wrong type: %). Nothing was changed; do not run this script until that table is looked at.', v_missing;
        END IF;
    END IF;

    -- old rows must have a usable name (a NULL / blank name cannot be mapped honestly)
    IF v_old THEN
        EXECUTE 'SELECT count(*) FROM public.tb_admission_types WHERE type_name IS NULL OR btrim(type_name) = ''''' INTO v_bad;
        IF v_bad > 0 THEN RAISE EXCEPTION '% row(s) of tb_admission_types have a NULL or blank type_name. Fix them first. Nothing was changed.', v_bad; END IF;
    END IF;

    IF v_done THEN RAISE NOTICE 'Pre-checks passed. The data is already migrated: this run will not change anything.';
    ELSE RAISE NOTICE 'Pre-checks passed.'; END IF;
END
$pre$;

-- ---------------------------------------------------------------------------------------------------------------------
-- BEFORE SNAPSHOT + REPORT (only when there is something to migrate). Temp tables vanish at COMMIT.
-- ---------------------------------------------------------------------------------------------------------------------
CREATE TEMP TABLE _runme_state (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _runme_counts (tbl text, n bigint) ON COMMIT DROP;
CREATE TEMP TABLE _runme_names (tbl text, school_id text, old_key text, target text, n bigint) ON COMMIT DROP;

DO $snap$
DECLARE
    v_migrating boolean;
    r record;
    t text;
    tbls text[] := ARRAY['tb_admissions', 'tb_students', 'tb_student_enrollments'];
    v_merged text := '';
BEGIN
    IF to_regclass('public.tb_admission_types') IS NULL THEN
        v_migrating := false;
    ELSIF to_regclass('public.mig_admission_type_map') IS NULL THEN
        v_migrating := true;   -- the map does not exist yet (dynamic SQL below: a missing table must not fail the planner)
    ELSE
        EXECUTE 'SELECT EXISTS (SELECT 1 FROM public.mig_admission_type_map WHERE repointed_at IS NULL)
                        OR EXISTS (SELECT 1 FROM public.tb_admission_types o WHERE NOT EXISTS
                              (SELECT 1 FROM public.mig_admission_type_map m WHERE m.school_id = o.school_id AND m.old_id = o.admission_type_id))' INTO v_migrating;
    END IF;
    INSERT INTO _runme_state VALUES (v_migrating);
    IF NOT v_migrating THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing to change (the checks below still run).';
        RETURN;
    END IF;

    FOREACH t IN ARRAY tbls LOOP
        EXECUTE format('INSERT INTO _runme_counts SELECT %L, count(*) FROM public.%I', t, t);
        -- rows per (school, old name), with the target the mapping rule gives it
        EXECUTE format($q$
            INSERT INTO _runme_names
            SELECT %L, x.school_id::text, lower(btrim(regexp_replace(o.type_name, '\s+', ' ', 'g'))),
                   CASE WHEN lower(btrim(regexp_replace(o.type_name, '\s+', ' ', 'g'))) = 'existing student' THEN 'EXISTING_STUDENT' ELSE 'NEW_ADMISSION' END,
                   count(*)
            FROM public.%I x
            JOIN public.tb_admission_types o ON o.admission_type_id = x.admission_type_id AND o.school_id = x.school_id
            GROUP BY 1, 2, 3, 4$q$, t, t);
        -- a row that points at something that is not its own school's old type would silently vanish from the counts: stop
        EXECUTE format('SELECT count(*) FROM public.%I x WHERE NOT EXISTS (SELECT 1 FROM public.tb_admission_types o WHERE o.admission_type_id = x.admission_type_id AND o.school_id = x.school_id)', t) INTO r;
        IF (r.count) > 0 THEN
            RAISE EXCEPTION '% row(s) in % point at an admission type that is not one of their own school''s (or does not exist). Review them first. Nothing was changed.', r.count, t;
        END IF;
        RAISE NOTICE 'rows now in %: %', t, (SELECT n FROM _runme_counts WHERE tbl = t);
    END LOOP;

    RAISE NOTICE '--- per school and old type name: how many rows each maps to ---';
    FOR r IN
        SELECT o.school_id, o.type_name AS raw_name, btrim(regexp_replace(o.type_name, '\s+', ' ', 'g')) AS nm,
               CASE WHEN lower(btrim(regexp_replace(o.type_name, '\s+', ' ', 'g'))) = 'existing student' THEN 'Existing Student' ELSE 'New Admission' END AS target,
               lower(btrim(regexp_replace(o.type_name, '\s+', ' ', 'g'))) IN ('new admission', 'existing student') AS exact_name,
               o.type_name IN ('New Admission', 'Existing Student') AS exact_spelling,
               (SELECT count(*) FROM public.tb_admissions x WHERE x.school_id = o.school_id AND x.admission_type_id = o.admission_type_id) AS adm,
               (SELECT count(*) FROM public.tb_students x WHERE x.school_id = o.school_id AND x.admission_type_id = o.admission_type_id) AS stu,
               (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.school_id = o.school_id AND x.admission_type_id = o.admission_type_id) AS enr
        FROM public.tb_admission_types o ORDER BY o.school_id, nm
    LOOP
        RAISE NOTICE 'school % | "%" -> % | admissions % | students % | enrollments % %',
            r.school_id, r.raw_name, r.target, r.adm, r.stu, r.enr,
            CASE WHEN NOT r.exact_name THEN '   <-- MERGED into New Admission (not an exact match)'
                 WHEN NOT r.exact_spelling THEN '   (spelling / case / spacing normalised)' ELSE '' END;
        IF NOT r.exact_name THEN v_merged := v_merged || format('%s: "%s"; ', r.school_id, r.raw_name); END IF;
    END LOOP;
    IF v_merged = '' THEN RAISE NOTICE 'No names were merged: every old name is exactly "New Admission" or "Existing Student".';
    ELSE RAISE NOTICE 'NAMES MERGED INTO NEW ADMISSION (school: old name): %', v_merged; END IF;
END
$snap$;

-- =====================================================================================================================
-- ORIGINAL SCRIPT (smss-admission-type-global.sql), BEGIN / COMMIT removed
-- =====================================================================================================================
-- To run this by hand in pgAdmin use Docs/smss-admission-type-global-RUN-ME.sql (this script plus pre-checks, reports and a final verification, in one file).
-- =====================================================================================================================
-- Admission Type becomes a GLOBAL master (lut_admission_type), managed by the Super Admin.
-- STEP 1 of 2: create + seed + repoint + verify.  (Step 2, dropping the old table, is Docs/smss-admission-type-global-drop-old.sql
-- and is run separately, later, once the new API has been live and checked.)
--
-- THE GLOBAL TABLE STARTS WITH EXACTLY TWO TYPES (the application finds them by CODE, never by id or display name):
--     NEW_ADMISSION     "New Admission"       the default of the admission application / Confirm Admission
--     EXISTING_STUDENT  "Existing Student"    always used by Add Existing Student and the student import
--
-- HOW THE OLD SCHOOL-LEVEL ROWS ARE MAPPED (name normalised: trimmed, inner whitespace collapsed, case ignored):
--     "existing student"  ->  EXISTING_STUDENT
--     "new admission"     ->  NEW_ADMISSION
--     ANY OTHER NAME      ->  NEW_ADMISSION   (e.g. "Transfer Admission", "RTE Admission": the global list no longer has them)
-- Every admission / student / enrollment keeps a type, but an "other name" is MERGED into New Admission. Nothing is lost silently:
--   * a NOTICE lists each such name per school with the number of rows it covered, and
--   * public.mig_admission_type_map keeps school_id, old_id, old_name -> new_id for every old row (so it can be reversed or re-split).
-- If a merged name should stay a type of its own, add it under Masters > Admission Type and re-point those rows from the map.
--
-- WHAT IT DOES (one transaction; any failed check raises and rolls EVERYTHING back):
--   1. creates lut_admission_type (BIGINT identity, type_code, type_name, description, is_active, created_at, updated_at)
--      with case-insensitive unique indexes on type_name and on type_code;
--   2. seeds the two types above (only if missing);
--   3. records old id -> new id (+ old name) in public.mig_admission_type_map;
--   4. repoints tb_admissions, tb_students and tb_student_enrollments (column admission_type_id) through that map. Any foreign key
--      those columns have to tb_admission_types is dropped first and a foreign key to lut_admission_type is added after;
--   5. verifies: zero unmapped old rows, zero orphans in the three tables, and the per-table / per-school / per-TARGET-type row counts are
--      IDENTICAL before and after. It stops if any other table has an admission_type_id column this script does not know about.
--
-- IDEMPOTENT: safe to run again. The map table remembers that the repoint already happened (repointed_at); a second run changes nothing
-- and re-runs the checks. If tb_admission_types is already gone (step 2 ran) the script is a no-op check.
--
-- ROLLBACK
--   * Before COMMIT (any error): automatic - nothing is changed.
--   * After COMMIT and before step 2: the old table is untouched, so rolling back = restore the old ids from the map:
--         BEGIN;
--         UPDATE tb_admissions a SET admission_type_id = m.old_id FROM mig_admission_type_map m WHERE a.admission_type_id = m.new_id AND a.school_id = m.school_id;
--         ... same for tb_students and tb_student_enrollments ... (ambiguous for rows that were merged: the map has one row per old id, so
--             use the pre-migration backup for those)
--         DROP TABLE lut_admission_type CASCADE; DROP TABLE mig_admission_type_map;  COMMIT;
--     then deploy the previous API build. Simplest and exact: restore the backup taken before step 1.
--   * After step 2: restore from the backup taken before it. Take a backup before running step 1 as well.
--
-- Run on a scratch restore first and compare the notices.   psql -v ON_ERROR_STOP=1 -f smss-admission-type-global.sql
-- =====================================================================================================================


-- ---------------------------------------------------------------------------------------------------------------------
-- 1. the global lookup
-- ---------------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.lut_admission_type (
    admission_type_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    type_code         VARCHAR(30)  NOT NULL,
    type_name         VARCHAR(100) NOT NULL,
    description       VARCHAR(250) NULL,
    is_active         BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at        TIMESTAMPTZ  NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_lut_admission_type_name ON public.lut_admission_type (lower(type_name));
CREATE UNIQUE INDEX IF NOT EXISTS uq_lut_admission_type_code ON public.lut_admission_type (upper(type_code));

-- ---------------------------------------------------------------------------------------------------------------------
-- 2. the two seeded types
-- ---------------------------------------------------------------------------------------------------------------------
INSERT INTO public.lut_admission_type (type_code, type_name, description, is_active)
SELECT 'NEW_ADMISSION', 'New Admission', 'A fresh admission through the application process', TRUE
WHERE NOT EXISTS (SELECT 1 FROM public.lut_admission_type WHERE upper(type_code) = 'NEW_ADMISSION' OR lower(type_name) = 'new admission');

INSERT INTO public.lut_admission_type (type_code, type_name, description, is_active)
SELECT 'EXISTING_STUDENT', 'Existing Student', 'A student already studying at the school when it joined', TRUE
WHERE NOT EXISTS (SELECT 1 FROM public.lut_admission_type WHERE upper(type_code) = 'EXISTING_STUDENT' OR lower(type_name) = 'existing student');

-- ---------------------------------------------------------------------------------------------------------------------
-- 3. the old -> new map (only while the old table still exists)
-- ---------------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.mig_admission_type_map (
    school_id     VARCHAR NOT NULL,
    old_id        BIGINT  NOT NULL,
    old_name      TEXT    NULL,
    new_id        BIGINT  NOT NULL REFERENCES public.lut_admission_type (admission_type_id),
    repointed_at  TIMESTAMPTZ NULL,
    PRIMARY KEY (school_id, old_id)
);
ALTER TABLE public.mig_admission_type_map ADD COLUMN IF NOT EXISTS old_name TEXT NULL;

DO $mig$
DECLARE
    r record;
BEGIN
    IF to_regclass('public.tb_admission_types') IS NULL THEN
        RAISE NOTICE 'tb_admission_types is already gone: nothing to map or repoint (checks below still run).';
        RETURN;
    END IF;

    INSERT INTO public.mig_admission_type_map (school_id, old_id, old_name, new_id)
    SELECT t.school_id, t.admission_type_id, t.type_name,
           (SELECT l.admission_type_id FROM public.lut_admission_type l
             WHERE upper(l.type_code) = CASE WHEN lower(btrim(regexp_replace(t.type_name, '\s+', ' ', 'g'))) = 'existing student' THEN 'EXISTING_STUDENT' ELSE 'NEW_ADMISSION' END)
    FROM public.tb_admission_types t
    ON CONFLICT (school_id, old_id) DO NOTHING;

    -- report every name that is not one of the two, per school, with how many rows it covers
    FOR r IN
        SELECT m.school_id, btrim(regexp_replace(m.old_name, '\s+', ' ', 'g')) AS nm,
               (SELECT count(*) FROM public.tb_admissions a WHERE a.school_id = m.school_id AND a.admission_type_id = m.old_id)
             + (SELECT count(*) FROM public.tb_students s WHERE s.school_id = m.school_id AND s.admission_type_id = m.old_id)
             + (SELECT count(*) FROM public.tb_student_enrollments e WHERE e.school_id = m.school_id AND e.admission_type_id = m.old_id) AS used_by
        FROM public.mig_admission_type_map m
        WHERE m.repointed_at IS NULL
          AND lower(btrim(regexp_replace(m.old_name, '\s+', ' ', 'g'))) NOT IN ('new admission', 'existing student')
        ORDER BY m.school_id, nm
    LOOP
        RAISE NOTICE 'OTHER NAME merged into New Admission: school % | "%" | covers % row(s)', r.school_id, r.nm, r.used_by;
    END LOOP;

    -- spelling / case / whitespace variants of the two names that were normalised
    FOR r IN
        SELECT m.school_id, m.old_name, l.type_name
        FROM public.mig_admission_type_map m JOIN public.lut_admission_type l ON l.admission_type_id = m.new_id
        WHERE m.repointed_at IS NULL AND m.old_name <> l.type_name
          AND lower(btrim(regexp_replace(m.old_name, '\s+', ' ', 'g'))) IN ('new admission', 'existing student')
        ORDER BY m.school_id
    LOOP
        RAISE NOTICE 'normalised: school % | "%" -> "%"', r.school_id, r.old_name, r.type_name;
    END LOOP;
END
$mig$;

-- ---------------------------------------------------------------------------------------------------------------------
-- 4 + 5. repoint, then verify. Counts are taken BEFORE (through the OLD table, target = the type the rule gives it) and compared AFTER
--        (through the NEW table), per table / school / target code.
-- ---------------------------------------------------------------------------------------------------------------------
DO $mig$
DECLARE
    v_old_exists boolean := to_regclass('public.tb_admission_types') IS NOT NULL;
    v_pending    boolean;
    v_bad        bigint;
    v_unknown    text;
    r            record;
    t            text;
    tbls         text[] := ARRAY['tb_admissions', 'tb_students', 'tb_student_enrollments'];
BEGIN
    -- any admission_type_id column we do not know about would be left pointing at dead ids: refuse
    SELECT string_agg(table_name, ', ') INTO v_unknown
    FROM information_schema.columns
    WHERE table_schema = 'public' AND column_name = 'admission_type_id'
      AND table_name NOT IN ('tb_admissions', 'tb_students', 'tb_student_enrollments', 'tb_admission_types', 'lut_admission_type', 'mig_admission_type_map');
    IF v_unknown IS NOT NULL THEN
        RAISE EXCEPTION 'Unexpected table(s) with an admission_type_id column: %. Add them to this script first.', v_unknown;
    END IF;

    SELECT EXISTS (SELECT 1 FROM public.mig_admission_type_map WHERE repointed_at IS NULL) INTO v_pending;

    IF v_old_exists AND v_pending THEN
        -- BEFORE snapshot: rows per table / school / target code (old ids resolved through the map's own target)
        CREATE TEMP TABLE _adm_before (tbl text, school_id text, type_key text, n bigint) ON COMMIT DROP;
        FOREACH t IN ARRAY tbls LOOP
            EXECUTE format($q$
                INSERT INTO _adm_before
                SELECT %L, x.school_id::text,
                       CASE WHEN lower(btrim(regexp_replace(o.type_name, '\s+', ' ', 'g'))) = 'existing student' THEN 'EXISTING_STUDENT' ELSE 'NEW_ADMISSION' END,
                       count(*)
                FROM public.%I x
                JOIN public.tb_admission_types o ON o.admission_type_id = x.admission_type_id AND o.school_id = x.school_id
                GROUP BY 1, 2, 3$q$, t, t);
        END LOOP;

        -- rows whose admission_type_id is not an old row of their own school would be lost by the join above: stop before changing anything
        FOREACH t IN ARRAY tbls LOOP
            EXECUTE format('SELECT count(*) FROM public.%I x WHERE NOT EXISTS (SELECT 1 FROM public.tb_admission_types o WHERE o.admission_type_id = x.admission_type_id AND o.school_id = x.school_id)', t) INTO v_bad;
            IF v_bad > 0 AND NOT EXISTS (SELECT 1 FROM public.mig_admission_type_map WHERE repointed_at IS NOT NULL) THEN
                RAISE EXCEPTION '% row(s) in % point at an admission type that is not one of their own school''s (or does not exist). Fix or review them first. Rolled back.', v_bad, t;
            END IF;
        END LOOP;

        -- drop any FK from these columns to the old table, repoint, add the FK to the new one
        FOREACH t IN ARRAY tbls LOOP
            FOR r IN
                SELECT c.conname FROM pg_constraint c
                WHERE c.contype = 'f' AND c.conrelid = format('public.%I', t)::regclass AND c.confrelid = 'public.tb_admission_types'::regclass
            LOOP
                EXECUTE format('ALTER TABLE public.%I DROP CONSTRAINT %I', t, r.conname);
            END LOOP;

            -- ONE statement per table: the join reads the pre-update ids, so an id that is both an old and a new value cannot be mapped twice
            EXECUTE format($q$
                UPDATE public.%I x SET admission_type_id = m.new_id
                FROM public.mig_admission_type_map m
                WHERE m.old_id = x.admission_type_id AND m.school_id = x.school_id AND m.repointed_at IS NULL$q$, t);
        END LOOP;

        UPDATE public.mig_admission_type_map SET repointed_at = now() WHERE repointed_at IS NULL;

        -- AFTER: identical counts per table / school / target code, read through the NEW table
        CREATE TEMP TABLE _adm_after (tbl text, school_id text, type_key text, n bigint) ON COMMIT DROP;
        FOREACH t IN ARRAY tbls LOOP
            EXECUTE format($q$
                INSERT INTO _adm_after
                SELECT %L, x.school_id::text, upper(l.type_code), count(*)
                FROM public.%I x
                JOIN public.lut_admission_type l ON l.admission_type_id = x.admission_type_id
                GROUP BY 1, 2, 3$q$, t, t);
        END LOOP;

        SELECT count(*) INTO v_bad FROM (
            (SELECT * FROM _adm_before EXCEPT SELECT * FROM _adm_after)
            UNION ALL
            (SELECT * FROM _adm_after EXCEPT SELECT * FROM _adm_before)
        ) d;
        IF v_bad > 0 THEN
            RAISE EXCEPTION 'Before/after counts per table, school and type DIFFER (% rows). Rolled back.', v_bad;
        END IF;

        FOR r IN SELECT tbl, school_id, type_key, n FROM _adm_after ORDER BY tbl, school_id, type_key LOOP
            RAISE NOTICE 'verified  % | school % | % | % row(s) (same before and after)', r.tbl, r.school_id, r.type_key, r.n;
        END LOOP;
    END IF;

    -- zero rows left unmapped / zero orphans (runs on every run, including the no-op re-run)
    IF v_old_exists THEN
        SELECT count(*) INTO v_bad FROM public.tb_admission_types o
        WHERE NOT EXISTS (SELECT 1 FROM public.mig_admission_type_map m WHERE m.school_id = o.school_id AND m.old_id = o.admission_type_id);
        IF v_bad > 0 THEN RAISE EXCEPTION '% school-level admission type row(s) are not mapped. Rolled back.', v_bad; END IF;
    END IF;

    FOREACH t IN ARRAY tbls LOOP
        EXECUTE format('SELECT count(*) FROM public.%I x WHERE NOT EXISTS (SELECT 1 FROM public.lut_admission_type l WHERE l.admission_type_id = x.admission_type_id)', t) INTO v_bad;
        IF v_bad > 0 THEN RAISE EXCEPTION '% row(s) in % have an admission_type_id that is not in lut_admission_type. Rolled back.', v_bad, t; END IF;
        RAISE NOTICE 'no orphans in %', t;
    END LOOP;

    -- the FKs to the new table (after the orphan check, so they cannot fail)
    FOREACH t IN ARRAY tbls LOOP
        IF NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.contype = 'f' AND c.conrelid = format('public.%I', t)::regclass
                       AND c.confrelid = 'public.lut_admission_type'::regclass) THEN
            EXECUTE format('ALTER TABLE public.%I ADD CONSTRAINT %I FOREIGN KEY (admission_type_id) REFERENCES public.lut_admission_type (admission_type_id)',
                           t, 'fk_' || t || '_admission_type');
        END IF;
    END LOOP;
END
$mig$;


-- Quick look afterwards:
--   SELECT admission_type_id, type_code, type_name, is_active FROM public.lut_admission_type ORDER BY type_name;
--   SELECT * FROM public.mig_admission_type_map ORDER BY school_id, old_id;

-- =====================================================================================================================
-- FINAL VERIFICATION (same transaction). Any failure RAISES, which rolls back EVERYTHING above.
-- =====================================================================================================================
DO $verify$
DECLARE
    v_migrating boolean;
    v_bad       bigint;
    v_codes     text;
    r           record;
    t           text;
    tbls        text[] := ARRAY['tb_admissions', 'tb_students', 'tb_student_enrollments'];
BEGIN
    SELECT migrating INTO v_migrating FROM _runme_state;

    -- the two codes
    SELECT string_agg(upper(type_code), ',' ORDER BY upper(type_code)) INTO v_codes FROM public.lut_admission_type;
    IF v_migrating THEN
        IF v_codes IS DISTINCT FROM 'EXISTING_STUDENT,NEW_ADMISSION' THEN
            RAISE EXCEPTION 'VERIFICATION FAILED: lut_admission_type must hold exactly NEW_ADMISSION and EXISTING_STUDENT, but holds: %. Rolled back.', coalesce(v_codes, '(nothing)');
        END IF;
    ELSIF v_codes IS NULL OR position('EXISTING_STUDENT' IN v_codes) = 0 OR position('NEW_ADMISSION' IN v_codes) = 0 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: lut_admission_type lacks NEW_ADMISSION and/or EXISTING_STUDENT (has: %). Rolled back.', coalesce(v_codes, '(nothing)');
    END IF;

    -- the rollback record survives the commit (a normal table, not temporary)
    IF to_regclass('public.mig_admission_type_map') IS NULL THEN RAISE EXCEPTION 'VERIFICATION FAILED: mig_admission_type_map is missing. Rolled back.'; END IF;
    IF (SELECT relpersistence FROM pg_class WHERE oid = 'public.mig_admission_type_map'::regclass) <> 'p' THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: mig_admission_type_map is not a permanent table. Rolled back.';
    END IF;

    -- zero orphans
    FOREACH t IN ARRAY tbls LOOP
        EXECUTE format('SELECT count(*) FROM public.%I x WHERE NOT EXISTS (SELECT 1 FROM public.lut_admission_type l WHERE l.admission_type_id = x.admission_type_id)', t) INTO v_bad;
        IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % orphan row(s) in %. Rolled back.', v_bad, t; END IF;
    END LOOP;

    IF v_migrating THEN
        -- row counts of the three tables unchanged
        FOR r IN SELECT c.tbl, c.n AS before_n FROM _runme_counts c LOOP
            EXECUTE format('SELECT count(*) FROM public.%I', r.tbl) INTO v_bad;
            IF v_bad <> r.before_n THEN RAISE EXCEPTION 'VERIFICATION FAILED: % had % rows before and has % now. Rolled back.', r.tbl, r.before_n, v_bad; END IF;
        END LOOP;

        -- rows per old type name (grouped by the type it maps to) = rows per new type, per table and school
        FOREACH t IN ARRAY tbls LOOP
            EXECUTE format($q$
                SELECT count(*) FROM (
                    SELECT school_id, target AS k, sum(n) AS n FROM _runme_names WHERE tbl = %L GROUP BY 1, 2
                    EXCEPT
                    SELECT x.school_id::text, upper(l.type_code), count(*) FROM public.%I x JOIN public.lut_admission_type l ON l.admission_type_id = x.admission_type_id GROUP BY 1, 2
                ) d$q$, t, t) INTO v_bad;
            IF v_bad = 0 THEN
                EXECUTE format($q$
                    SELECT count(*) FROM (
                        SELECT x.school_id::text, upper(l.type_code), count(*) FROM public.%I x JOIN public.lut_admission_type l ON l.admission_type_id = x.admission_type_id GROUP BY 1, 2
                        EXCEPT
                        SELECT school_id, target, sum(n) FROM _runme_names WHERE tbl = %L GROUP BY 1, 2
                    ) d$q$, t, t) INTO v_bad;
            END IF;
            IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: rows per old type name do not equal rows per new type in % (% group(s) differ). Rolled back.', t, v_bad; END IF;
        END LOOP;

        -- every old row is in the map and repointed
        IF to_regclass('public.tb_admission_types') IS NOT NULL THEN
            SELECT count(*) INTO v_bad FROM public.tb_admission_types o
            WHERE NOT EXISTS (SELECT 1 FROM public.mig_admission_type_map m WHERE m.school_id = o.school_id AND m.old_id = o.admission_type_id AND m.repointed_at IS NOT NULL);
            IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % old admission type row(s) are not in the map / not repointed. Rolled back.', v_bad; END IF;
        END IF;
        RAISE NOTICE 'VERIFIED: two types only, no orphans, row counts unchanged, per-name counts equal per-type counts, map kept (% rows).', (SELECT count(*) FROM public.mig_admission_type_map);
    ELSE
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed. (two types present, no orphans)';
    END IF;
END
$verify$;

COMMIT;

-- =====================================================================================================================
-- RESULT: the types and how many rows use each
-- =====================================================================================================================
SELECT l.type_code, l.type_name, l.is_active,
       (SELECT count(*) FROM public.tb_admissions x WHERE x.admission_type_id = l.admission_type_id)           AS admissions,
       (SELECT count(*) FROM public.tb_students x WHERE x.admission_type_id = l.admission_type_id)             AS students,
       (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.admission_type_id = l.admission_type_id)  AS enrollments
FROM public.lut_admission_type l
ORDER BY l.type_code;
