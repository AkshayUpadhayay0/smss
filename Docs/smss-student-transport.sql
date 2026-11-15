-- =====================================================================================================================
-- SMSS Transport, STAGE 3 (student side): assigning a student to a transport area
--
-- RUN ORDER (important): 1) Docs/smss-transport.sql (Stage 1: areas, vehicles, drivers, the DRIVER role),
--                        2) Docs/smss-transport-live.sql (Stage 2: duty sessions, live location),
--                        3) THIS script.
-- It refuses to run (with a clear message) when Stage 1 is missing. Safe to run twice: every statement is guarded.
--
-- Creates:
--   public.tb_student_transport   one row per student per academic year: which transport area the student uses.
--                                 Soft delete through status_id (Active / Inactive): removing an assignment hides the row, assigning again REACTIVATES
--                                 the same row. The unique (student_id, academic_year_id) means there can never be a second row.
-- Adds (only if missing):
--   uq_tb_academic_years_school_id  UNIQUE (school_id, academic_year_id) on tb_academic_years: the target of the composite foreign key below.
--                                   It is additive and cannot fail: academic_year_id is already the primary key.
-- Needs (already present, confirmed): tb_students UNIQUE (student_id, school_id)  [uq_tb_students_id_school].
--
-- No fee wiring and no stops or times here. Later extensions are all additive: a nullable stop_id (composite FK to a future tb_transport_stops),
-- a nullable fee_item_id (link to the student's Transport fee item), a nullable driver_id / vehicle_id for a one-bus-per-student rule.
--
-- Before running on the real database take a backup, e.g.:
--   pg_dump -h localhost -U postgres -Fc -f "E:\SecurityBackup\smss-db-before-student-transport.dump" smss-db
-- (Docs/smss-db*.sql and *.dump are git-ignored; keep backups outside the repository.)
-- Roll back (only if nothing depends on it):  DROP TABLE public.tb_student_transport;
-- =====================================================================================================================

DO $$
BEGIN
    IF to_regclass('public.tb_transport_areas') IS NULL OR to_regclass('public.tb_drivers') IS NULL THEN
        RAISE EXCEPTION 'Stage 1 is missing: run Docs/smss-transport.sql (then Docs/smss-transport-live.sql) first, then this script.';
    END IF;
    IF to_regclass('public.tb_students') IS NULL OR to_regclass('public.tb_academic_years') IS NULL THEN
        RAISE EXCEPTION 'tb_students / tb_academic_years not found: this database is not an SMSS database.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_students'::regclass AND contype = 'u'
                   AND pg_get_constraintdef(oid) = 'UNIQUE (student_id, school_id)') THEN
        RAISE EXCEPTION 'tb_students has no UNIQUE (student_id, school_id): the composite foreign key below needs it.';
    END IF;
END $$;

-- the target of the composite (school_id, academic_year_id) foreign key (guarded: skipped when any unique constraint on exactly those columns exists)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_academic_years'::regclass AND contype IN ('u', 'p')
                   AND pg_get_constraintdef(oid) = 'UNIQUE (school_id, academic_year_id)') THEN
        ALTER TABLE public.tb_academic_years ADD CONSTRAINT uq_tb_academic_years_school_id UNIQUE (school_id, academic_year_id);
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.tb_student_transport
(
    student_transport_id  bigint        GENERATED ALWAYS AS IDENTITY,
    school_id             varchar(100)  NOT NULL,
    student_id            varchar(20)   NOT NULL,
    academic_year_id      bigint        NOT NULL,
    area_id               bigint        NOT NULL,
    status_id             integer       NOT NULL,                  -- Active / Inactive (soft delete); resolved by NAME in the code, never hard-coded
    created_at            timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at            timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_student_transport_pkey PRIMARY KEY (student_transport_id),
    CONSTRAINT uq_tb_student_transport_student_year UNIQUE (student_id, academic_year_id),   -- one row per student and year: reassign or reactivate, never add a second
    CONSTRAINT fk_tb_student_transport_student FOREIGN KEY (student_id, school_id)       REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_student_transport_year    FOREIGN KEY (school_id, academic_year_id) REFERENCES public.tb_academic_years (school_id, academic_year_id),
    CONSTRAINT fk_tb_student_transport_area    FOREIGN KEY (school_id, area_id)          REFERENCES public.tb_transport_areas (school_id, area_id),
    CONSTRAINT fk_tb_student_transport_status  FOREIGN KEY (status_id) REFERENCES public.lut_status (sid)
);

CREATE INDEX IF NOT EXISTS idx_tb_student_transport_school ON public.tb_student_transport (school_id);
CREATE INDEX IF NOT EXISTS idx_tb_student_transport_area   ON public.tb_student_transport (school_id, area_id, academic_year_id);
