-- =====================================================================================================================
-- Admission Type becomes a GLOBAL master (lut_admission_type), managed by the Super Admin.
-- STEP 1 of 2: create + seed + repoint + verify.  (Step 2, dropping the old table, is Docs/smss-admission-type-global-drop-old.sql
-- and is run separately, later, once the new API has been live and checked.)
--
-- WHAT IT DOES (one transaction; any failed check raises and rolls EVERYTHING back):
--   1. creates lut_admission_type (BIGINT identity, type_code, type_name, description, is_active, created_at, updated_at)
--      with case-insensitive unique indexes on type_name and on type_code;
--   2. seeds it from the DISTINCT names in tb_admission_types across all schools. Names are normalised: trimmed, inner whitespace collapsed,
--      grouped case-insensitively; the spelling used most often wins (ties: alphabetical). A type is active when ANY school still had it active.
--      "Existing Student" is also seeded if missing, because Add Existing Student / the Excel import look it up by that name;
--   3. records old id -> new id in public.mig_admission_type_map (name match, so every school's row for one name maps to one global row);
--   4. repoints tb_admissions, tb_students and tb_student_enrollments (column admission_type_id) through that map. Any foreign key
--      those columns have to tb_admission_types is dropped first and a foreign key to lut_admission_type is added after;
--   5. verifies: zero unmapped old rows, zero orphans in the three tables, and the per-table / per-school / per-type row counts are
--      IDENTICAL before and after (compared by type NAME, so a wrong mapping cannot hide). It stops if any other table has an
--      admission_type_id column this script does not know about.
--
-- IDEMPOTENT: safe to run again. The map table remembers that the repoint already happened (repointed_at); a second run only adds new
-- names, never re-maps ids, and re-runs the checks. If tb_admission_types is already gone (step 2 ran) the whole script is a no-op check.
--
-- ROLLBACK
--   * Before COMMIT (any error): automatic - nothing is changed.
--   * After COMMIT and before step 2: the old table is untouched, so rolling back = restore the old ids from the map:
--         BEGIN;
--         UPDATE tb_admissions a SET admission_type_id = m.old_id FROM mig_admission_type_map m WHERE a.admission_type_id = m.new_id AND a.school_id = m.school_id;
--         ... same for tb_students and tb_student_enrollments ...
--         DROP TABLE lut_admission_type CASCADE; DROP TABLE mig_admission_type_map;  COMMIT;
--     (the map keeps one row per (school_id, old_id), so the reverse is exact), then deploy the previous API build.
--   * After step 2: restore from the backup taken before it. Take a backup before running step 1 as well.
--
-- Run on a scratch restore first and compare the "before/after" notices.   psql -v ON_ERROR_STOP=1 -f smss-admission-type-global.sql
-- =====================================================================================================================

BEGIN;

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
-- 2 + 3. seed from the school-level names and build the old -> new map (only while the old table still exists)
-- ---------------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.mig_admission_type_map (
    school_id     VARCHAR NOT NULL,
    old_id        BIGINT  NOT NULL,
    new_id        BIGINT  NOT NULL REFERENCES public.lut_admission_type (admission_type_id),
    repointed_at  TIMESTAMPTZ NULL,
    PRIMARY KEY (school_id, old_id)
);

DO $mig$
DECLARE
    v_old_exists boolean := to_regclass('public.tb_admission_types') IS NOT NULL;
    v_code_col   text;
BEGIN
    IF NOT v_old_exists THEN
        RAISE NOTICE 'tb_admission_types is already gone: nothing to seed or repoint (checks below still run).';
        RETURN;
    END IF;

    -- one canonical spelling per normalised name
    CREATE TEMP TABLE _adm_names ON COMMIT DROP AS
    WITH raw AS (
        SELECT t.school_id, t.admission_type_id AS old_id,
               btrim(regexp_replace(t.type_name, '\s+', ' ', 'g')) AS clean_name,
               (t.status_id IS NOT NULL AND EXISTS (SELECT 1 FROM public.lut_status s WHERE s.sid = t.status_id AND s.sname = 'Active')) AS is_active,
               t.description
        FROM public.tb_admission_types t
        WHERE btrim(t.type_name) <> ''
    ),
    spell AS (
        SELECT lower(clean_name) AS k, clean_name, count(*) AS n,
               row_number() OVER (PARTITION BY lower(clean_name) ORDER BY count(*) DESC, clean_name) AS rk
        FROM raw GROUP BY lower(clean_name), clean_name
    )
    SELECT s.k, s.clean_name AS canonical,
           (SELECT bool_or(r.is_active) FROM raw r WHERE lower(r.clean_name) = s.k) AS is_active,
           (SELECT max(r.description) FROM raw r WHERE lower(r.clean_name) = s.k AND r.description IS NOT NULL) AS description
    FROM spell s WHERE s.rk = 1;

    -- the type the onboarding / import screens look up by name
    INSERT INTO _adm_names (k, canonical, is_active, description)
    SELECT 'existing student', 'Existing Student', TRUE, 'Students already studying at the school when it joined'
    WHERE NOT EXISTS (SELECT 1 FROM _adm_names WHERE k = 'existing student')
      AND NOT EXISTS (SELECT 1 FROM public.lut_admission_type WHERE lower(type_name) = 'existing student');

    -- codes: UPPER_SNAKE of the name; a collision (two names that squash to one code) gets _2, _3 ...
    INSERT INTO public.lut_admission_type (type_code, type_name, description, is_active)
    SELECT left(c.code, CASE WHEN c.rn = 1 THEN 30 ELSE 26 END) || CASE WHEN c.rn = 1 THEN '' ELSE '_' || c.rn END,
           c.canonical, left(c.description, 250), c.is_active
    FROM (
        SELECT n.*, coalesce(nullif(upper(regexp_replace(btrim(n.canonical), '[^A-Za-z0-9]+', '_', 'g')), ''), 'TYPE') AS code,
               row_number() OVER (PARTITION BY upper(regexp_replace(btrim(n.canonical), '[^A-Za-z0-9]+', '_', 'g')) ORDER BY n.canonical) AS rn
        FROM _adm_names n
    ) c
    WHERE NOT EXISTS (SELECT 1 FROM public.lut_admission_type l WHERE lower(l.type_name) = c.k)
      AND NOT EXISTS (SELECT 1 FROM public.lut_admission_type l WHERE upper(l.type_code) = upper(left(c.code, CASE WHEN c.rn = 1 THEN 30 ELSE 26 END) || CASE WHEN c.rn = 1 THEN '' ELSE '_' || c.rn END));

    -- map every old row (per school) to the global row with the same normalised name; existing map rows are left alone
    INSERT INTO public.mig_admission_type_map (school_id, old_id, new_id)
    SELECT t.school_id, t.admission_type_id, l.admission_type_id
    FROM public.tb_admission_types t
    JOIN public.lut_admission_type l ON lower(l.type_name) = lower(btrim(regexp_replace(t.type_name, '\s+', ' ', 'g')))
    ON CONFLICT (school_id, old_id) DO NOTHING;
END
$mig$;

-- ---------------------------------------------------------------------------------------------------------------------
-- 4 + 5. repoint, then verify. Counts are taken BEFORE (by type name, through the OLD table) and compared AFTER (through the NEW one).
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
        -- BEFORE snapshot: rows per table / school / type name (old ids resolved through the old table)
        CREATE TEMP TABLE _adm_before (tbl text, school_id text, type_key text, n bigint) ON COMMIT DROP;
        FOREACH t IN ARRAY tbls LOOP
            EXECUTE format($q$
                INSERT INTO _adm_before
                SELECT %L, x.school_id::text, lower(btrim(regexp_replace(o.type_name, '\s+', ' ', 'g'))), count(*)
                FROM public.%I x
                JOIN public.tb_admission_types o ON o.admission_type_id = x.admission_type_id AND o.school_id = x.school_id
                GROUP BY 1, 2, 3$q$, t, t);
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

        -- AFTER: identical counts per table / school / type name, read through the NEW table
        CREATE TEMP TABLE _adm_after (tbl text, school_id text, type_key text, n bigint) ON COMMIT DROP;
        FOREACH t IN ARRAY tbls LOOP
            EXECUTE format($q$
                INSERT INTO _adm_after
                SELECT %L, x.school_id::text, lower(l.type_name), count(*)
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

        FOR r IN SELECT tbl, type_key, sum(n) AS n FROM _adm_after GROUP BY tbl, type_key ORDER BY tbl, type_key LOOP
            RAISE NOTICE 'verified  % | % | % rows (same before and after, per school)', r.tbl, r.type_key, r.n;
        END LOOP;
    END IF;

    -- zero rows left unmapped / zero orphans (runs on every run, including the no-op re-run)
    IF v_old_exists THEN
        SELECT count(*) INTO v_bad FROM public.tb_admission_types o
        WHERE NOT EXISTS (SELECT 1 FROM public.mig_admission_type_map m WHERE m.school_id = o.school_id AND m.old_id = o.admission_type_id);
        IF v_bad > 0 THEN RAISE EXCEPTION '% school-level admission type row(s) are not mapped (empty or odd name?). Rolled back.', v_bad; END IF;
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

COMMIT;

-- Quick look afterwards:
--   SELECT admission_type_id, type_code, type_name, is_active FROM public.lut_admission_type ORDER BY type_name;
--   SELECT * FROM public.mig_admission_type_map ORDER BY school_id, old_id;
