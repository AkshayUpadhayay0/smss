-- Homework (v1: publish + view only): a teacher (or a School Admin) publishes homework for one section and subject; students of that section read it.
--
-- Adds: tb_homework.
-- Needs tb_schools, tb_academic_years, tb_sections WITH the UNIQUE (section_id, class_id) that smss-class-teacher-assignments.sql adds,
-- tb_subjects, tb_employees (smss-employees.sql), lut_status. Run BEFORE starting the API build that contains this entity. Safe to run twice.
--
-- Decisions:
--  * employee_id is the teacher who OWNS the row (the creator). A School Admin who creates one names the owning teacher (the school-id login is not an employee).
--  * "Delete" is status_id -> Inactive (the project's hide-row pattern). Nothing is ever removed.
--  * attachment_path is the key in the PRIVATE store ({schoolCode}/homework/{homeworkId}/attachment_<timestamp>.<ext>); it is never a URL.
--  * No submissions, no per-student status, no marks: this table only holds what was set.
--  * status_id is integer (lut_status.sid is integer and every other tb_ table uses integer for it).

BEGIN;

CREATE TABLE IF NOT EXISTS public.tb_homework
(
    homework_id      bigint        GENERATED ALWAYS AS IDENTITY,
    school_id        varchar(100)  NOT NULL,
    academic_year_id bigint        NOT NULL,
    class_id         bigint        NOT NULL,
    section_id       bigint        NOT NULL,
    subject_id       bigint        NOT NULL,
    employee_id      varchar(20)   NOT NULL,
    title            varchar(200)  NOT NULL,
    description      text,                                    -- at most 2000 characters (the application checks it)
    assigned_date    date          NOT NULL DEFAULT CURRENT_DATE,   -- the application fills in the school's (IST) today when the client leaves it out
    due_date         date          NOT NULL,
    attachment_path  varchar(500),
    status_id        integer       NOT NULL,
    created_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_homework_pkey PRIMARY KEY (homework_id),
    CONSTRAINT fk_tb_homework_school   FOREIGN KEY (school_id)            REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_homework_year     FOREIGN KEY (academic_year_id)     REFERENCES public.tb_academic_years (academic_year_id),
    CONSTRAINT fk_tb_homework_section  FOREIGN KEY (section_id, class_id) REFERENCES public.tb_sections (section_id, class_id),
    CONSTRAINT fk_tb_homework_subject  FOREIGN KEY (subject_id)           REFERENCES public.tb_subjects (subject_id),
    CONSTRAINT fk_tb_homework_employee FOREIGN KEY (employee_id)          REFERENCES public.tb_employees (employee_id),
    CONSTRAINT fk_tb_homework_status   FOREIGN KEY (status_id)            REFERENCES public.lut_status (sid),
    CONSTRAINT chk_tb_homework_dates   CHECK (due_date >= assigned_date),
    CONSTRAINT chk_tb_homework_title   CHECK (length(btrim(title)) > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_homework_section  ON public.tb_homework (school_id, section_id, due_date);
CREATE INDEX IF NOT EXISTS idx_tb_homework_teacher  ON public.tb_homework (school_id, employee_id, due_date);
CREATE INDEX IF NOT EXISTS idx_tb_homework_year     ON public.tb_homework (school_id, academic_year_id);

COMMIT;
