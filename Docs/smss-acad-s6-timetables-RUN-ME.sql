-- =====================================================================================================================
-- RUN-ME: Academic Years, stage S6 = TIMETABLE VERSIONS (several timetables per section and year, exactly one ACTIVE)  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/academic-year-config-design.md (sections C.10, D and the addendum I.2)
-- Needs: S1 to S5 RUN-ME scripts (S4 for the schedules, S2 for the section offerings, S1 for btree_gist).
--
-- WHAT IT DOES
--   1. tb_timetables (new): one row per timetable of a SECTION OFFERING in a year: name (unique per section and year), the bell schedule it is based on, state DRAFT | ACTIVE | ARCHIVED,
--      is_active (always = state ACTIVE; a real column so entries can reference it), ever_active (set at the first activation, never reset), who / when activated, audit.
--      EXACTLY ONE ACTIVE TIMETABLE PER SECTION AND YEAR: partial unique index on (academic_year_id, section_id) WHERE is_active.
--      A timetable can exist only for a section that is offered in that year (FK to tb_academic_year_sections).
--   2. tb_timetable_entries gets timetable_id (NOT NULL), tt_is_active (NOT NULL, a MIRROR of the timetable's flag), needs_teacher, and employee_id becomes NULLABLE (a slot copied without a
--      teacher is allowed and flagged). FOREIGN KEY (timetable_id, tt_is_active) -> tb_timetables (timetable_id, is_active) ON UPDATE CASCADE: an entry can never carry a flag different from
--      its timetable's, and activating / archiving a timetable flips the flag of every one of its entries in the same statement (no trigger, no application code writes it afterwards).
--   3. TEACHER DOUBLE-BOOKING IS IMPOSSIBLE AMONG ACTIVE TIMETABLES, IN THE DATABASE: unique index on (school, year, teacher, day, period) WHERE tt_is_active AND the entry is Active AND a teacher is set.
--      The old "one Active entry per section slot" index becomes "one per TIMETABLE slot" (timetable_id, day, period); the old teacher index is replaced by the one above.
--      Another FK (timetable_id, year, section, class) keeps an entry's own year / section / class equal to its timetable's.
--   4. BACKFILL RULE: for every (school, year, section) that has at least one existing entry (Active or Inactive history) ONE timetable is created: name "Regular timetable", schedule = the school's
--      default schedule (S4), state ACTIVE, ever_active true, activated_at = the earliest entry's created_at; ALL entries of that section and year (history rows included) are attached to it with
--      tt_is_active true. Sections without entries get no timetable. Entry counts are unchanged.
--   5. Pre-check (lists offenders, raises before changing anything): entries of a section that the year does not offer; entries of a school with no default schedule (S4 not run); two Active
--      entries of one teacher at the same day and period (they could not both sit in active timetables).
--   6. Verification (raises, so everything rolls back, on any mismatch): per school the entry counts (all / Active) and the other counts are unchanged; timetables = distinct (year, section) of the entries;
--      every entry has a timetable and the mirror equals the timetable's flag; exactly one ACTIVE timetable per (year, section) that has entries; no ever-active timetable is a draft.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-acad-s6.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   3. Run the new API build right after: the old build inserts entries without a timetable and would fail against the NOT NULL column.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   Restore the pg_dump. (Or, before the new build is used: restore the two old unique indexes uq_tb_tte_one_active_per_slot and uq_tb_tte_teacher_one_active_per_period, set employee_id NOT NULL,
--   drop the new entry columns and constraints, then DROP TABLE tb_timetables.)
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _s6_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _s6_before (school_id text PRIMARY KEY, entries bigint, active_entries bigint, sections_with_entries bigint, class_subjects bigint, enrolments bigint, attendance bigint,
                              periods bigint, class_teachers bigint) ON COMMIT DROP;

DO $s6$
DECLARE
    v_offenders text;
    v_mig       boolean;
    t           text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_users', 'lut_status', 'tb_academic_years', 'tb_academic_year_sections', 'tb_period_schedules', 'tb_periods', 'tb_timetable_entries',
                             'tb_class_subjects', 'tb_student_enrollments', 'tb_student_attendance', 'tb_class_teacher_assignments'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist (run the S1 to S5 scripts first). Nothing was changed.', t;
        END IF;
    END LOOP;
    IF NOT EXISTS (SELECT 1 FROM lut_status WHERE sname = 'Active' AND stype = 'general status') THEN
        RAISE EXCEPTION 'lut_status has no Active / general status row. Nothing was changed.';
    END IF;

    v_mig := NOT (to_regclass('public.tb_timetables') IS NOT NULL
                  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_timetable_entries' AND column_name = 'timetable_id')
                  AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_tte_timetable_flag'));
    INSERT INTO _s6_state VALUES (v_mig);

    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    SELECT string_agg('entry ' || e.timetable_entry_id || ' (school ' || e.school_id || ', section ' || e.section_id || ', year ' || e.academic_year_id || ')', '; ') INTO v_offenders
    FROM public.tb_timetable_entries e
    WHERE NOT EXISTS (SELECT 1 FROM public.tb_academic_year_sections o WHERE o.academic_year_id = e.academic_year_id AND o.section_id = e.section_id);
    IF v_offenders IS NOT NULL THEN
        RAISE EXCEPTION 'A timetable entry belongs to a section the year does not offer (run Docs/smss-acad-s2-offerings-RUN-ME.sql): %. Nothing was changed.', v_offenders;
    END IF;

    SELECT string_agg(school_id, ', ') INTO v_offenders
    FROM (SELECT DISTINCT e.school_id FROM public.tb_timetable_entries e
          WHERE NOT EXISTS (SELECT 1 FROM public.tb_period_schedules d WHERE d.school_id = e.school_id AND d.is_default)) x;
    IF v_offenders IS NOT NULL THEN
        RAISE EXCEPTION 'These schools have timetable entries but no default bell schedule (run Docs/smss-acad-s4-schedules-RUN-ME.sql): %. Nothing was changed.', v_offenders;
    END IF;

    SELECT string_agg('teacher ' || a.employee_id || ' day ' || a.day_of_week || ' period ' || a.period_id || ' (year ' || a.academic_year_id || ')', '; ') INTO v_offenders
    FROM public.tb_timetable_entries a
    JOIN public.tb_timetable_entries b ON b.school_id = a.school_id AND b.academic_year_id = a.academic_year_id AND b.employee_id = a.employee_id AND b.day_of_week = a.day_of_week
         AND b.period_id = a.period_id AND b.timetable_entry_id > a.timetable_entry_id
    WHERE a.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')
      AND b.status_id = a.status_id;
    IF v_offenders IS NOT NULL THEN
        RAISE EXCEPTION 'A teacher has two Active timetable entries at the same day and period: %. Fix these rows and run again. Nothing was changed.', v_offenders;
    END IF;
    RAISE NOTICE 'Pre-checks passed.';
END
$s6$;

INSERT INTO _s6_before
SELECT s.school_id::text,
       (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id AND x.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')),
       (SELECT count(DISTINCT (x.academic_year_id, x.section_id)) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_attendance x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_periods x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id)
FROM public.tb_schools s
WHERE (SELECT migrating FROM _s6_state);

-- ---------------------------------------------------------------- the changes (skipped on a re-run)
DO $s6ddl$
DECLARE
    v_active int;
BEGIN
    IF NOT (SELECT migrating FROM _s6_state) THEN RETURN; END IF;
    SELECT sid INTO v_active FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status';

    CREATE TABLE IF NOT EXISTS public.tb_timetables (
        timetable_id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        school_id           varchar(100) NOT NULL,
        academic_year_id    bigint       NOT NULL,
        class_id            bigint       NOT NULL,
        section_id          bigint       NOT NULL,
        timetable_name      varchar(80)  NOT NULL,
        schedule_id         bigint       NOT NULL,
        state               varchar(8)   NOT NULL DEFAULT 'DRAFT',
        is_active           boolean      NOT NULL DEFAULT false,
        ever_active         boolean      NOT NULL DEFAULT false,
        first_activated_at  timestamptz  NULL,
        activated_at        timestamptz  NULL,
        activated_by        bigint       NULL,
        deactivated_at      timestamptz  NULL,
        created_at          timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at          timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        created_by          bigint       NULL,
        updated_by          bigint       NULL,
        CONSTRAINT chk_tb_tt_state        CHECK (state IN ('DRAFT', 'ACTIVE', 'ARCHIVED')),
        CONSTRAINT chk_tb_tt_active_flag  CHECK (is_active = (state = 'ACTIVE')),
        CONSTRAINT chk_tb_tt_ever_active  CHECK (NOT is_active OR ever_active),
        CONSTRAINT chk_tb_tt_name         CHECK (length(btrim(timetable_name)) > 0),
        CONSTRAINT uq_tb_tt_name          UNIQUE (academic_year_id, section_id, timetable_name),
        CONSTRAINT uq_tb_tt_id_flag       UNIQUE (timetable_id, is_active),
        CONSTRAINT uq_tb_tt_id_scope      UNIQUE (timetable_id, academic_year_id, section_id, class_id),
        CONSTRAINT fk_tb_tt_year      FOREIGN KEY (school_id, academic_year_id)    REFERENCES public.tb_academic_years (school_id, academic_year_id),
        CONSTRAINT fk_tb_tt_offered   FOREIGN KEY (academic_year_id, section_id)   REFERENCES public.tb_academic_year_sections (academic_year_id, section_id),
        CONSTRAINT fk_tb_tt_section   FOREIGN KEY (section_id, class_id)           REFERENCES public.tb_sections (section_id, class_id),
        CONSTRAINT fk_tb_tt_schedule  FOREIGN KEY (school_id, schedule_id)         REFERENCES public.tb_period_schedules (school_id, schedule_id),
        CONSTRAINT fk_tb_tt_activator FOREIGN KEY (activated_by) REFERENCES public.tb_users (user_id),
        CONSTRAINT fk_tb_tt_creator   FOREIGN KEY (created_by)   REFERENCES public.tb_users (user_id),
        CONSTRAINT fk_tb_tt_updater   FOREIGN KEY (updated_by)   REFERENCES public.tb_users (user_id)
    );
    CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_tt_one_active ON public.tb_timetables (academic_year_id, section_id) WHERE is_active;
    CREATE INDEX IF NOT EXISTS idx_tb_tt_section ON public.tb_timetables (school_id, academic_year_id, section_id);

    -- BACKFILL: one ACTIVE "Regular timetable" per (school, year, section) that has entries; all its entries (history included) attach to it
    INSERT INTO public.tb_timetables (school_id, academic_year_id, class_id, section_id, timetable_name, schedule_id, state, is_active, ever_active, first_activated_at, activated_at)
    SELECT e.school_id, e.academic_year_id, e.class_id, e.section_id, 'Regular timetable',
           (SELECT d.schedule_id FROM public.tb_period_schedules d WHERE d.school_id = e.school_id AND d.is_default),
           'ACTIVE', true, true, min(e.created_at), min(e.created_at)
    FROM public.tb_timetable_entries e
    GROUP BY e.school_id, e.academic_year_id, e.class_id, e.section_id;

    ALTER TABLE public.tb_timetable_entries ADD COLUMN IF NOT EXISTS timetable_id  bigint  NULL;
    ALTER TABLE public.tb_timetable_entries ADD COLUMN IF NOT EXISTS tt_is_active  boolean NULL;
    ALTER TABLE public.tb_timetable_entries ADD COLUMN IF NOT EXISTS needs_teacher boolean NOT NULL DEFAULT false;

    UPDATE public.tb_timetable_entries e
    SET timetable_id = t.timetable_id, tt_is_active = true
    FROM public.tb_timetables t
    WHERE t.school_id = e.school_id AND t.academic_year_id = e.academic_year_id AND t.section_id = e.section_id AND e.timetable_id IS NULL;

    ALTER TABLE public.tb_timetable_entries ALTER COLUMN timetable_id SET NOT NULL;
    ALTER TABLE public.tb_timetable_entries ALTER COLUMN tt_is_active SET NOT NULL;
    ALTER TABLE public.tb_timetable_entries ALTER COLUMN tt_is_active SET DEFAULT false;
    ALTER TABLE public.tb_timetable_entries ALTER COLUMN employee_id DROP NOT NULL;
    ALTER TABLE public.tb_timetable_entries ADD CONSTRAINT chk_tb_tte_needs_teacher CHECK (NOT needs_teacher OR employee_id IS NULL);

    ALTER TABLE public.tb_timetable_entries ADD CONSTRAINT fk_tb_tte_timetable_flag FOREIGN KEY (timetable_id, tt_is_active)
        REFERENCES public.tb_timetables (timetable_id, is_active) ON UPDATE CASCADE;
    ALTER TABLE public.tb_timetable_entries ADD CONSTRAINT fk_tb_tte_timetable_scope FOREIGN KEY (timetable_id, academic_year_id, section_id, class_id)
        REFERENCES public.tb_timetables (timetable_id, academic_year_id, section_id, class_id);

    DROP INDEX IF EXISTS public.uq_tb_tte_one_active_per_slot;
    DROP INDEX IF EXISTS public.uq_tb_tte_teacher_one_active_per_period;
    EXECUTE format('CREATE UNIQUE INDEX uq_tb_tte_slot_per_timetable ON public.tb_timetable_entries (timetable_id, day_of_week, period_id) WHERE status_id = %s', v_active);
    EXECUTE format('CREATE UNIQUE INDEX uq_tb_tte_teacher_active ON public.tb_timetable_entries (school_id, academic_year_id, employee_id, day_of_week, period_id) WHERE tt_is_active AND status_id = %s AND employee_id IS NOT NULL', v_active);
    CREATE INDEX IF NOT EXISTS idx_tb_tte_timetable ON public.tb_timetable_entries (timetable_id);
END
$s6ddl$;

-- ---------------------------------------------------------------- verification
DO $s6v$
DECLARE
    r       record;
    v_after _s6_before;
    v_bad   text := '';
    v_n     bigint;
BEGIN
    IF NOT (SELECT migrating FROM _s6_state) THEN RETURN; END IF;

    FOR r IN SELECT * FROM _s6_before ORDER BY school_id LOOP
        SELECT s.school_id::text,
               (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id AND x.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')),
               (SELECT count(DISTINCT (x.academic_year_id, x.section_id)) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_attendance x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_periods x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.school_id = s.school_id)
          INTO v_after FROM public.tb_schools s WHERE s.school_id = r.school_id;
        IF v_after IS DISTINCT FROM r THEN v_bad := v_bad || format('%s: before=%s after=%s; ', r.school_id, r::text, v_after::text); END IF;

        SELECT count(*) INTO v_n FROM public.tb_timetables t WHERE t.school_id = r.school_id;
        IF v_n <> r.sections_with_entries THEN v_bad := v_bad || format('%s: %s timetables, expected %s (one per year and section with entries); ', r.school_id, v_n, r.sections_with_entries); END IF;
        SELECT count(*) INTO v_n FROM public.tb_timetables t WHERE t.school_id = r.school_id AND t.is_active;
        IF v_n <> r.sections_with_entries THEN v_bad := v_bad || format('%s: %s ACTIVE timetables, expected %s; ', r.school_id, v_n, r.sections_with_entries); END IF;
    END LOOP;
    IF v_bad <> '' THEN RAISE EXCEPTION 'VERIFICATION FAILED, everything is rolled back: %', v_bad; END IF;

    SELECT count(*) INTO v_n FROM public.tb_timetable_entries e JOIN public.tb_timetables t ON t.timetable_id = e.timetable_id WHERE e.tt_is_active IS DISTINCT FROM t.is_active;
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % entr(ies) whose mirror differs from their timetable. Everything is rolled back.', v_n; END IF;
    SELECT count(*) INTO v_n FROM (SELECT academic_year_id, section_id FROM public.tb_timetables WHERE is_active GROUP BY 1, 2 HAVING count(*) <> 1) q;
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % section(s) without exactly one active timetable. Everything is rolled back.', v_n; END IF;
    SELECT count(*) INTO v_n FROM public.tb_timetables WHERE is_active AND NOT ever_active;
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: an active timetable that was never active. Everything is rolled back.'; END IF;

    RAISE NOTICE 'VERIFIED: per-school entry counts are unchanged; one ACTIVE "Regular timetable" per section and year that has entries; every entry sits on it and its mirror flag is true.';
END
$s6v$;

COMMIT;

-- Per-school summary (shown in the Data Output tab)
SELECT s.school_id, s.school_name,
       (SELECT count(*) FROM public.tb_timetables t WHERE t.school_id = s.school_id) AS timetables,
       (SELECT count(*) FROM public.tb_timetables t WHERE t.school_id = s.school_id AND t.is_active) AS active_timetables,
       (SELECT count(*) FROM public.tb_timetable_entries e WHERE e.school_id = s.school_id) AS entries,
       (SELECT count(*) FROM public.tb_timetable_entries e WHERE e.school_id = s.school_id AND e.tt_is_active) AS entries_on_active
FROM public.tb_schools s ORDER BY s.school_id;
