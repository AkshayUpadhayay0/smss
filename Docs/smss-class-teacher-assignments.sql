-- Class-Teacher Assignment: which employee is the class teacher of a section, for one academic year. First step toward Attendance.
--
-- Needs tb_schools, tb_academic_years, tb_classes, tb_sections, tb_employees (smss-employees.sql), lut_status, tb_users.
-- Run BEFORE starting the API build that contains this entity. Safe to run twice.
--
-- What the real schema looked like when this was written (no mismatch with the brief, two notes):
--  * Sections ARE their own table (tb_sections: section_id PK, class_id FK, section_name; unique (class_id, section_name)).
--  * A section is NOT tied to an academic year (it persists across years), so the year lives on the assignment, not on the section.
--  * tb_academic_years(academic_year_id) is the same FK target tb_fee_structures uses.
--
-- Design:
--  * Reassigning a section deactivates the old row (status Inactive) and inserts a new one: nothing is deleted or overwritten, so the
--    history of who was class teacher stays queryable.
--  * ONE ACTIVE assignment per (school, academic year, class, section) is enforced HERE, by a partial unique index on the active rows.
--  * class_id is derivable from the section but is stored (as asked) and kept consistent by a composite FK to tb_sections(section_id, class_id).
--  * The partial-index predicate needs a literal, and status ids are not stable across environments, so the index is created from the
--    'Active' (general status) id found in lut_status at the time this file runs.

BEGIN;

-- (section_id, class_id) is trivially unique (section_id is the PK); declaring it lets the assignment's composite FK check the class
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_sections_id_class' AND conrelid = 'public.tb_sections'::regclass) THEN
        ALTER TABLE public.tb_sections ADD CONSTRAINT uq_tb_sections_id_class UNIQUE (section_id, class_id);
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.tb_class_teacher_assignments
(
    assignment_id    bigint        GENERATED ALWAYS AS IDENTITY,
    school_id        varchar(100)  NOT NULL,
    academic_year_id bigint        NOT NULL,
    class_id         bigint        NOT NULL,
    section_id       bigint        NOT NULL,
    employee_id      varchar(20)   NOT NULL,
    status_id        integer       NOT NULL,   -- lut_status Active / Inactive (Inactive = a past assignment, kept as history)
    assigned_by      bigint,                   -- tb_users.user_id of the School Admin who made it, from the token
    created_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_class_teacher_assignments_pkey PRIMARY KEY (assignment_id),
    CONSTRAINT fk_tb_cta_school       FOREIGN KEY (school_id)        REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_cta_year         FOREIGN KEY (academic_year_id) REFERENCES public.tb_academic_years (academic_year_id),
    CONSTRAINT fk_tb_cta_class        FOREIGN KEY (class_id)         REFERENCES public.tb_classes (class_id),
    CONSTRAINT fk_tb_cta_section      FOREIGN KEY (section_id, class_id) REFERENCES public.tb_sections (section_id, class_id),
    CONSTRAINT fk_tb_cta_employee     FOREIGN KEY (employee_id)      REFERENCES public.tb_employees (employee_id),
    CONSTRAINT fk_tb_cta_status       FOREIGN KEY (status_id)        REFERENCES public.lut_status (sid),
    CONSTRAINT fk_tb_cta_assigned_by  FOREIGN KEY (assigned_by)      REFERENCES public.tb_users (user_id)
);
CREATE INDEX IF NOT EXISTS idx_tb_cta_school_year ON public.tb_class_teacher_assignments (school_id, academic_year_id);
CREATE INDEX IF NOT EXISTS idx_tb_cta_employee    ON public.tb_class_teacher_assignments (employee_id);

-- ONE ACTIVE assignment per school / year / class / section
DO $$
DECLARE
    active_sid integer;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'uq_tb_cta_one_active_per_section') THEN
        SELECT sid INTO active_sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status';
        IF active_sid IS NULL THEN
            RAISE EXCEPTION 'lut_status has no ''Active'' row of type ''general status''';
        END IF;
        EXECUTE format(
            'CREATE UNIQUE INDEX uq_tb_cta_one_active_per_section ON public.tb_class_teacher_assignments (school_id, academic_year_id, class_id, section_id) WHERE status_id = %s',
            active_sid);
    END IF;
END $$;

COMMIT;
