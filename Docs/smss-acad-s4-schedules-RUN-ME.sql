-- =====================================================================================================================
-- RUN-ME: Academic Years, stage S4 = PERIOD SCHEDULES (bell schedules), breaks, and schedules per class / day / year  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/academic-year-config-design.md (sections C.8, C.10, D) and the addendum I
-- Needs: Docs/smss-acad-s1-years-RUN-ME.sql, Docs/smss-acad-s2-offerings-RUN-ME.sql (and S3 for the web build) to have been run first.
--
-- WHAT IT DOES
--   1. tb_period_schedules (new): named bell schedules of a school ("Regular", "Winter", "Saturday"). At most ONE default per school (partial unique index).
--   2. tb_periods becomes the SLOTS of a schedule: schedule_id (NOT NULL), slot_type PERIOD | BREAK, slot_label (e.g. "Lunch"), slot_order (display order by time).
--      period_number becomes NULL for a break (CHECK: a PERIOD has a number, a BREAK has none). The old UNIQUE (school_id, period_number) becomes UNIQUE (schedule_id, period_number).
--      DATABASE OVERLAP GUARD: an EXCLUDE constraint makes two Active slots of one schedule with overlapping clock times impossible (today only the service checked).
--   3. tb_academic_year_schedules (new): which schedule applies to which CLASS on which DAY in a year (class NULL = every class, day NULL = every day; day 1..6 = Mon..Sat).
--      A class / day with no row uses the school's default schedule, so a school that never uses this table behaves exactly as today.
--   4. BREAKS CAN NEVER HOLD A TIMETABLE ENTRY, in the database: tb_timetable_entries gets slot_type (always 'PERIOD', CHECK) and a foreign key (period_id, slot_type) to
--      tb_periods (period_id, slot_type); turning a period that has entries into a break, or pointing an entry at a break, is refused by the database.
--   5. BACKFILL RULE: every school that has periods (or timetable entries) gets ONE default schedule named "Regular"; every existing period joins it as a PERIOD with slot_order =
--      period_number and keeps its period_id, number and times. No assignment rows are created (so every class and day resolves to "Regular"). HOW EXISTING TIMETABLE
--      ENTRIES MAP: an entry points at a period_id; that period is now a slot of "Regular", "Regular" is what every class and day resolves to, so every existing entry is valid,
--      untouched, and shows in the same cell as before.
--   6. Pre-check (lists offenders, raises before changing anything): two Active periods of one school whose times overlap (they could not share a schedule).
--   7. Verification (raises, so everything rolls back, on any mismatch): per school the counts of periods, Active periods, timetable entries, class subjects, enrolments, fee
--      structures, dues and attendance rows are identical before and after; every school with periods has exactly one default schedule; every period belongs to it with
--      slot_order = period_number; no break exists; every timetable entry still points at a period of its school's default schedule; the assignments table is empty.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-acad-s4.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   3. Run the new API build right after: the old build inserts periods without a schedule and would fail against the NOT NULL column.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   Restore the pg_dump. (Or, before the new build is used: ALTER TABLE tb_timetable_entries DROP CONSTRAINT fk_tb_tte_period_slot, DROP CONSTRAINT chk_tb_tte_slot_type, DROP COLUMN slot_type;
--   DROP TABLE tb_academic_year_schedules; DELETE FROM tb_periods WHERE slot_type = 'BREAK'; drop the exclusion and the new unique constraints, restore UNIQUE (school_id, period_number)
--   and period_number NOT NULL, drop the schedule_id / slot_* columns; DROP TABLE tb_period_schedules.)
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _s4_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _s4_before (school_id text PRIMARY KEY, periods bigint, active_periods bigint, timetable bigint, class_subjects bigint, enrolments bigint, structures bigint,
                              dues bigint, dues_due numeric, dues_paid numeric, attendance bigint, entries_with_period bigint) ON COMMIT DROP;

DO $s4$
DECLARE
    v_offenders text;
    v_mig       boolean;
    t           text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_users', 'lut_status', 'tb_academic_years', 'tb_classes', 'tb_academic_year_classes', 'tb_periods', 'tb_timetable_entries',
                             'tb_class_subjects', 'tb_student_enrollments', 'tb_fee_structures', 'tb_student_fee_dues', 'tb_student_attendance'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist (for the offering tables: run Docs/smss-acad-s1-years-RUN-ME.sql and Docs/smss-acad-s2-offerings-RUN-ME.sql first). Nothing was changed.', t;
        END IF;
    END LOOP;
    IF NOT EXISTS (SELECT 1 FROM lut_status WHERE sname = 'Active' AND stype = 'general status') THEN
        RAISE EXCEPTION 'lut_status has no Active / general status row. Nothing was changed.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'btree_gist') THEN
        RAISE EXCEPTION 'The btree_gist extension is missing (Docs/smss-acad-s1-years-RUN-ME.sql creates it). Nothing was changed.';
    END IF;

    v_mig := NOT (to_regclass('public.tb_period_schedules') IS NOT NULL AND to_regclass('public.tb_academic_year_schedules') IS NOT NULL
                  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_periods' AND column_name = 'schedule_id')
                  AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_timetable_entries' AND column_name = 'slot_type')
                  AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_tb_periods_no_overlap'));
    INSERT INTO _s4_state VALUES (v_mig);

    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    SELECT string_agg('school ' || a.school_id || ': period ' || a.period_number || ' (' || a.start_time || '-' || a.end_time || ') overlaps period ' || b.period_number || ' (' || b.start_time || '-' || b.end_time || ')', '; ') INTO v_offenders
    FROM public.tb_periods a
    JOIN public.tb_periods b ON b.school_id = a.school_id AND b.period_id > a.period_id AND a.start_time < b.end_time AND b.start_time < a.end_time
    WHERE a.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')
      AND b.status_id = a.status_id;
    IF v_offenders IS NOT NULL THEN
        RAISE EXCEPTION 'Two Active periods of one school have overlapping times, so they cannot be slots of one schedule: %. Change or deactivate one of each pair and run again. Nothing was changed.', v_offenders;
    END IF;
    RAISE NOTICE 'Pre-checks passed.';
END
$s4$;

INSERT INTO _s4_before
SELECT s.school_id::text,
       (SELECT count(*) FROM public.tb_periods x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_periods x WHERE x.school_id = s.school_id AND x.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')),
       (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_fee_structures x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
       (SELECT coalesce(sum(x.amount_due), 0) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
       (SELECT coalesce(sum(x.amount_paid), 0) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_attendance x WHERE x.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_timetable_entries e JOIN public.tb_periods p ON p.period_id = e.period_id AND p.school_id = e.school_id WHERE e.school_id = s.school_id)
FROM public.tb_schools s
WHERE (SELECT migrating FROM _s4_state);

-- ---------------------------------------------------------------- the changes (skipped on a re-run)
DO $s4ddl$
DECLARE
    v_active int;
BEGIN
    IF NOT (SELECT migrating FROM _s4_state) THEN RETURN; END IF;
    SELECT sid INTO v_active FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status';

    CREATE TABLE IF NOT EXISTS public.tb_period_schedules (
        schedule_id   bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        school_id     varchar(100) NOT NULL,
        schedule_name varchar(60)  NOT NULL,
        is_default    boolean      NOT NULL DEFAULT false,
        status_id     integer      NOT NULL,
        created_at    timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at    timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        created_by    bigint       NULL,
        updated_by    bigint       NULL,
        CONSTRAINT uq_tb_ps_school_name     UNIQUE (school_id, schedule_name),
        CONSTRAINT uq_tb_ps_school_schedule UNIQUE (school_id, schedule_id),
        CONSTRAINT chk_tb_ps_name           CHECK (length(btrim(schedule_name)) > 0),
        CONSTRAINT fk_tb_ps_school  FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
        CONSTRAINT fk_tb_ps_status  FOREIGN KEY (status_id) REFERENCES public.lut_status (sid),
        CONSTRAINT fk_tb_ps_creator FOREIGN KEY (created_by) REFERENCES public.tb_users (user_id),
        CONSTRAINT fk_tb_ps_updater FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id)
    );
    CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_ps_one_default ON public.tb_period_schedules (school_id) WHERE is_default;

    -- BACKFILL: one default schedule "Regular" for every school that has periods or timetable entries
    INSERT INTO public.tb_period_schedules (school_id, schedule_name, is_default, status_id)
    SELECT s.school_id, 'Regular', true, v_active
    FROM public.tb_schools s
    WHERE (EXISTS (SELECT 1 FROM public.tb_periods p WHERE p.school_id = s.school_id) OR EXISTS (SELECT 1 FROM public.tb_timetable_entries e WHERE e.school_id = s.school_id))
      AND NOT EXISTS (SELECT 1 FROM public.tb_period_schedules x WHERE x.school_id = s.school_id AND x.is_default);

    -- tb_periods -> the slots of a schedule
    ALTER TABLE public.tb_periods ADD COLUMN IF NOT EXISTS schedule_id bigint NULL;
    ALTER TABLE public.tb_periods ADD COLUMN IF NOT EXISTS slot_type   varchar(8) NOT NULL DEFAULT 'PERIOD';
    ALTER TABLE public.tb_periods ADD COLUMN IF NOT EXISTS slot_label  varchar(30) NULL;
    ALTER TABLE public.tb_periods ADD COLUMN IF NOT EXISTS slot_order  smallint NULL;

    UPDATE public.tb_periods p
    SET schedule_id = (SELECT d.schedule_id FROM public.tb_period_schedules d WHERE d.school_id = p.school_id AND d.is_default),
        slot_order  = p.period_number
    WHERE p.schedule_id IS NULL;

    ALTER TABLE public.tb_periods ALTER COLUMN schedule_id SET NOT NULL;
    ALTER TABLE public.tb_periods ALTER COLUMN slot_order  SET NOT NULL;
    ALTER TABLE public.tb_periods ALTER COLUMN period_number DROP NOT NULL;

    ALTER TABLE public.tb_periods DROP CONSTRAINT IF EXISTS uq_tb_periods_school_number;
    ALTER TABLE public.tb_periods ADD CONSTRAINT chk_tb_periods_slot_type CHECK (slot_type IN ('PERIOD', 'BREAK'));
    ALTER TABLE public.tb_periods ADD CONSTRAINT chk_tb_periods_number_by_type CHECK ((slot_type = 'PERIOD') = (period_number IS NOT NULL));
    ALTER TABLE public.tb_periods ADD CONSTRAINT chk_tb_periods_order CHECK (slot_order >= 1);
    ALTER TABLE public.tb_periods ADD CONSTRAINT uq_tb_periods_schedule_number UNIQUE (schedule_id, period_number);
    ALTER TABLE public.tb_periods ADD CONSTRAINT uq_tb_periods_schedule_order  UNIQUE (schedule_id, slot_order);
    ALTER TABLE public.tb_periods ADD CONSTRAINT uq_tb_periods_id_type         UNIQUE (period_id, slot_type);
    ALTER TABLE public.tb_periods ADD CONSTRAINT fk_tb_periods_schedule FOREIGN KEY (school_id, schedule_id) REFERENCES public.tb_period_schedules (school_id, schedule_id);
    CREATE INDEX IF NOT EXISTS idx_tb_periods_schedule ON public.tb_periods (schedule_id);

    -- two Active slots of one schedule can never overlap in clock time (the status literal is resolved by NAME, here, when the constraint is created)
    EXECUTE format($ex$ALTER TABLE public.tb_periods ADD CONSTRAINT ex_tb_periods_no_overlap
        EXCLUDE USING gist (schedule_id WITH =, tsrange(DATE '2000-01-01' + start_time, DATE '2000-01-01' + end_time, '[)') WITH &&) WHERE (status_id = %s)$ex$, v_active);

    -- which schedule applies to which class on which day in a year
    CREATE TABLE IF NOT EXISTS public.tb_academic_year_schedules (
        year_schedule_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        school_id        varchar(100) NOT NULL,
        academic_year_id bigint       NOT NULL,
        class_id         bigint       NULL,
        day_of_week      smallint     NULL,
        schedule_id      bigint       NOT NULL,
        created_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        created_by       bigint       NULL,
        updated_by       bigint       NULL,
        CONSTRAINT chk_tb_ays2_day CHECK (day_of_week IS NULL OR day_of_week BETWEEN 1 AND 6),
        CONSTRAINT fk_tb_ays2_year     FOREIGN KEY (school_id, academic_year_id) REFERENCES public.tb_academic_years (school_id, academic_year_id),
        CONSTRAINT fk_tb_ays2_schedule FOREIGN KEY (school_id, schedule_id)      REFERENCES public.tb_period_schedules (school_id, schedule_id),
        CONSTRAINT fk_tb_ays2_class    FOREIGN KEY (academic_year_id, class_id)  REFERENCES public.tb_academic_year_classes (academic_year_id, class_id),
        CONSTRAINT fk_tb_ays2_creator  FOREIGN KEY (created_by) REFERENCES public.tb_users (user_id),
        CONSTRAINT fk_tb_ays2_updater  FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id)
    );
    -- one assignment per (year, class, day); NULL class / day mean "every"
    CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_ays2_year_class_day ON public.tb_academic_year_schedules (academic_year_id, COALESCE(class_id, 0), COALESCE(day_of_week, 0));
    CREATE INDEX IF NOT EXISTS idx_tb_ays2_schedule ON public.tb_academic_year_schedules (schedule_id);

    -- breaks can never hold a timetable entry
    ALTER TABLE public.tb_timetable_entries ADD COLUMN IF NOT EXISTS slot_type varchar(8) NOT NULL DEFAULT 'PERIOD';
    ALTER TABLE public.tb_timetable_entries ADD CONSTRAINT chk_tb_tte_slot_type CHECK (slot_type = 'PERIOD');
    ALTER TABLE public.tb_timetable_entries ADD CONSTRAINT fk_tb_tte_period_slot FOREIGN KEY (period_id, slot_type) REFERENCES public.tb_periods (period_id, slot_type);
END
$s4ddl$;

-- ---------------------------------------------------------------- verification
DO $s4v$
DECLARE
    r       record;
    v_after _s4_before;
    v_bad   text := '';
    v_n     bigint;
BEGIN
    IF NOT (SELECT migrating FROM _s4_state) THEN RETURN; END IF;

    FOR r IN SELECT * FROM _s4_before ORDER BY school_id LOOP
        SELECT s.school_id::text,
               (SELECT count(*) FROM public.tb_periods x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_periods x WHERE x.school_id = s.school_id AND x.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')),
               (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_class_subjects x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_fee_structures x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
               (SELECT coalesce(sum(x.amount_due), 0) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
               (SELECT coalesce(sum(x.amount_paid), 0) FROM public.tb_student_fee_dues x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_student_attendance x WHERE x.school_id = s.school_id),
               (SELECT count(*) FROM public.tb_timetable_entries e JOIN public.tb_periods p ON p.period_id = e.period_id AND p.school_id = e.school_id
                  JOIN public.tb_period_schedules d ON d.schedule_id = p.schedule_id AND d.is_default AND d.school_id = e.school_id WHERE e.school_id = s.school_id)
          INTO v_after FROM public.tb_schools s WHERE s.school_id = r.school_id;
        IF v_after.periods IS DISTINCT FROM r.periods OR v_after.active_periods IS DISTINCT FROM r.active_periods OR v_after.timetable IS DISTINCT FROM r.timetable
           OR v_after.class_subjects IS DISTINCT FROM r.class_subjects OR v_after.enrolments IS DISTINCT FROM r.enrolments OR v_after.structures IS DISTINCT FROM r.structures
           OR v_after.dues IS DISTINCT FROM r.dues OR v_after.dues_due IS DISTINCT FROM r.dues_due OR v_after.dues_paid IS DISTINCT FROM r.dues_paid
           OR v_after.attendance IS DISTINCT FROM r.attendance THEN
            v_bad := v_bad || format('%s: before=%s after=%s; ', r.school_id, r::text, v_after::text);
        END IF;
        -- every timetable entry still points at a period of the school's default schedule (the old behaviour, now explicit)
        IF v_after.entries_with_period IS DISTINCT FROM r.entries_with_period THEN
            v_bad := v_bad || format('%s: entries on a default-schedule period %s, expected %s; ', r.school_id, v_after.entries_with_period, r.entries_with_period);
        END IF;
        -- one default schedule for a school that has periods
        SELECT count(*) INTO v_n FROM public.tb_period_schedules d WHERE d.school_id = r.school_id AND d.is_default;
        IF (r.periods > 0 AND v_n <> 1) OR v_n > 1 THEN v_bad := v_bad || format('%s: %s default schedules; ', r.school_id, v_n); END IF;
    END LOOP;
    IF v_bad <> '' THEN RAISE EXCEPTION 'VERIFICATION FAILED, everything is rolled back: %', v_bad; END IF;

    SELECT count(*) INTO v_n FROM public.tb_periods p
    WHERE p.slot_type <> 'PERIOD' OR p.slot_order IS DISTINCT FROM p.period_number OR p.slot_label IS NOT NULL
       OR NOT EXISTS (SELECT 1 FROM public.tb_period_schedules d WHERE d.schedule_id = p.schedule_id AND d.school_id = p.school_id AND d.is_default);
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % period(s) are not plain numbered slots of their school''s default schedule. Everything is rolled back.', v_n; END IF;

    SELECT count(*) INTO v_n FROM public.tb_academic_year_schedules;
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: the assignments table should start empty, it has % rows. Everything is rolled back.', v_n; END IF;

    SELECT count(*) INTO v_n FROM public.tb_timetable_entries WHERE slot_type <> 'PERIOD';
    IF v_n <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % timetable entries are not on a PERIOD slot. Everything is rolled back.', v_n; END IF;

    RAISE NOTICE 'VERIFIED: per-school counts and sums are identical before and after; every period is a numbered slot of its school''s default schedule "Regular"; every timetable entry still sits on it; no assignment, no break.';
END
$s4v$;

COMMIT;

-- Per-school summary (shown in the Data Output tab)
SELECT s.school_id, s.school_name,
       (SELECT count(*) FROM public.tb_period_schedules d WHERE d.school_id = s.school_id) AS schedules,
       (SELECT string_agg(d.schedule_name, ', ') FROM public.tb_period_schedules d WHERE d.school_id = s.school_id AND d.is_default) AS default_schedule,
       (SELECT count(*) FROM public.tb_periods p WHERE p.school_id = s.school_id) AS slots,
       (SELECT count(*) FROM public.tb_periods p WHERE p.school_id = s.school_id AND p.slot_type = 'BREAK') AS breaks,
       (SELECT count(*) FROM public.tb_timetable_entries e WHERE e.school_id = s.school_id) AS timetable_entries,
       (SELECT count(*) FROM public.tb_academic_year_schedules a WHERE a.school_id = s.school_id) AS assignments
FROM public.tb_schools s ORDER BY s.school_id;
