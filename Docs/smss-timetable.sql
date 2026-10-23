-- Teacher Timetable: the school's bell schedule (periods) and the weekly timetable of every section.
--
-- Adds: tb_periods and tb_timetable_entries.
-- Needs tb_schools, tb_academic_years, tb_classes, tb_sections WITH the UNIQUE (section_id, class_id) that smss-class-teacher-assignments.sql adds,
-- tb_subjects, tb_employees (smss-employees.sql), tb_users, lut_status. Run BEFORE starting the API build that contains these entities. Safe to run twice.
--
-- The Subject master already exists (tb_subjects: school-owned, subject_id bigint identity, subject_name unique per school, status_id) and is used as is.
--
-- Decisions:
--  * tb_periods is the school's bell schedule: NOT academic-year-scoped, unique (school_id, period_number).
--  * An entry is one subject + one teacher + a room in one period of one weekday for one section in one academic year. day_of_week is 1 = Monday ..
--    6 = Saturday: Sunday (7) and 0 are not valid.
--  * Like the class-teacher assignments, changing a slot deactivates the old row (status Inactive, kept as history) and inserts a new one.
--  * TWO partial unique indexes on the Active rows enforce the rules at the database level (backstops to the application checks):
--      - one Active entry per (school, year, class, section, day, period)
--      - one Active entry per (school, year, teacher, day, period): a teacher cannot be in two places in the same period.
--    Their `WHERE status_id = <Active sid>` literal is resolved from lut_status when this file runs (status ids are not stable across environments).
--  * Rooms are free text and are NOT checked for double booking.

BEGIN;

CREATE TABLE IF NOT EXISTS public.tb_periods
(
    period_id     bigint        GENERATED ALWAYS AS IDENTITY,
    school_id     varchar(100)  NOT NULL,
    period_number smallint      NOT NULL,
    start_time    time          NOT NULL,
    end_time      time          NOT NULL,
    status_id     integer       NOT NULL,
    created_at    timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at    timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_periods_pkey PRIMARY KEY (period_id),
    CONSTRAINT fk_tb_periods_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_periods_status FOREIGN KEY (status_id) REFERENCES public.lut_status (sid),
    CONSTRAINT uq_tb_periods_school_number UNIQUE (school_id, period_number),
    CONSTRAINT chk_tb_periods_number CHECK (period_number >= 1),
    CONSTRAINT chk_tb_periods_times CHECK (end_time > start_time)
);
CREATE INDEX IF NOT EXISTS idx_tb_periods_school ON public.tb_periods (school_id);

CREATE TABLE IF NOT EXISTS public.tb_timetable_entries
(
    timetable_entry_id bigint        GENERATED ALWAYS AS IDENTITY,
    school_id          varchar(100)  NOT NULL,
    academic_year_id   bigint        NOT NULL,
    class_id           bigint        NOT NULL,
    section_id         bigint        NOT NULL,
    day_of_week        smallint      NOT NULL,   -- 1 = Monday .. 6 = Saturday
    period_id          bigint        NOT NULL,
    subject_id         bigint        NOT NULL,
    employee_id        varchar(20)   NOT NULL,   -- the teacher
    room_no            varchar(50),              -- free text
    status_id          integer       NOT NULL,   -- Active / Inactive (Inactive = a replaced entry, kept as history)
    assigned_by        bigint,                   -- tb_users.user_id of the School Admin, from the token
    created_at         timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at         timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_timetable_entries_pkey PRIMARY KEY (timetable_entry_id),
    CONSTRAINT fk_tb_tte_school      FOREIGN KEY (school_id)            REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_tte_year        FOREIGN KEY (academic_year_id)     REFERENCES public.tb_academic_years (academic_year_id),
    CONSTRAINT fk_tb_tte_class       FOREIGN KEY (class_id)             REFERENCES public.tb_classes (class_id),
    CONSTRAINT fk_tb_tte_section     FOREIGN KEY (section_id, class_id) REFERENCES public.tb_sections (section_id, class_id),
    CONSTRAINT fk_tb_tte_period      FOREIGN KEY (period_id)            REFERENCES public.tb_periods (period_id),
    CONSTRAINT fk_tb_tte_subject     FOREIGN KEY (subject_id)           REFERENCES public.tb_subjects (subject_id),
    CONSTRAINT fk_tb_tte_employee    FOREIGN KEY (employee_id)          REFERENCES public.tb_employees (employee_id),
    CONSTRAINT fk_tb_tte_status      FOREIGN KEY (status_id)            REFERENCES public.lut_status (sid),
    CONSTRAINT fk_tb_tte_assigned_by FOREIGN KEY (assigned_by)          REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_tte_day        CHECK (day_of_week BETWEEN 1 AND 6)
);
CREATE INDEX IF NOT EXISTS idx_tb_tte_section  ON public.tb_timetable_entries (school_id, academic_year_id, section_id);
CREATE INDEX IF NOT EXISTS idx_tb_tte_teacher  ON public.tb_timetable_entries (school_id, academic_year_id, employee_id);
CREATE INDEX IF NOT EXISTS idx_tb_tte_period   ON public.tb_timetable_entries (period_id);

DO $$
DECLARE
    active_sid integer;
BEGIN
    SELECT sid INTO active_sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status';
    IF active_sid IS NULL THEN
        RAISE EXCEPTION 'lut_status has no ''Active'' row of type ''general status''';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'uq_tb_tte_one_active_per_slot') THEN
        EXECUTE format(
            'CREATE UNIQUE INDEX uq_tb_tte_one_active_per_slot ON public.tb_timetable_entries (school_id, academic_year_id, class_id, section_id, day_of_week, period_id) WHERE status_id = %s',
            active_sid);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'uq_tb_tte_teacher_one_active_per_period') THEN
        EXECUTE format(
            'CREATE UNIQUE INDEX uq_tb_tte_teacher_one_active_per_period ON public.tb_timetable_entries (school_id, academic_year_id, employee_id, day_of_week, period_id) WHERE status_id = %s',
            active_sid);
    END IF;
END $$;

COMMIT;
