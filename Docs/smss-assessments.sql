-- Internal Assessment / Class Tests: a teacher records one test for a section and subject, with a mark (or an absence) for every student of the section.
--
-- Adds: lut_assessment_type (global, seeded), tb_class_assessments (the test), tb_student_assessment_marks (one row per student).
-- Needs tb_schools, tb_academic_years, tb_sections WITH the UNIQUE (section_id, class_id) that smss-class-teacher-assignments.sql adds,
-- tb_subjects, tb_employees, tb_students, lut_status. Run BEFORE starting the API build that contains these entities. Safe to run twice.
--
-- Decisions:
--  * "Delete" is status_id -> Inactive on the test (the project hide-row pattern); its marks are kept. status_id is integer, like every other tb_ table
--    (lut_status.sid is integer).
--  * employee_id is the teacher who OWNS the test (the one who recorded it). A School Admin who records one names the owning teacher.
--  * A mark row is EITHER a mark (is_absent = false, obtained_marks set) OR an absence (is_absent = true, obtained_marks NULL): a CHECK enforces it.
--  * obtained_marks <= max_marks spans two tables, so the application enforces it on create and update (not a database check).
--  * One mark row per student per test (unique); marks go away with their test only if the test row itself is ever deleted (ON DELETE CASCADE).

BEGIN;

CREATE TABLE IF NOT EXISTS public.lut_assessment_type
(
    assessment_type_id bigint       GENERATED ALWAYS AS IDENTITY,
    type_name          varchar(50)  NOT NULL,
    type_code          varchar(30)  NOT NULL,
    is_active          boolean      NOT NULL DEFAULT true,
    created_at         timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at         timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT lut_assessment_type_pkey PRIMARY KEY (assessment_type_id),
    CONSTRAINT uq_lut_assessment_type_code UNIQUE (type_code),
    CONSTRAINT uq_lut_assessment_type_name UNIQUE (type_name)
);

INSERT INTO public.lut_assessment_type (type_name, type_code) VALUES
    ('Class Test',   'CLASS_TEST'),
    ('Unit Test',    'UNIT_TEST'),
    ('Monthly Test', 'MONTHLY_TEST'),
    ('Other',        'OTHER')
ON CONFLICT (type_code) DO NOTHING;

CREATE TABLE IF NOT EXISTS public.tb_class_assessments
(
    assessment_id      bigint        GENERATED ALWAYS AS IDENTITY,
    school_id          varchar(100)  NOT NULL,
    academic_year_id   bigint        NOT NULL,
    class_id           bigint        NOT NULL,
    section_id         bigint        NOT NULL,
    subject_id         bigint        NOT NULL,
    assessment_type_id bigint        NOT NULL,
    employee_id        varchar(20)   NOT NULL,
    title              varchar(200)  NOT NULL,
    test_date          date          NOT NULL,
    max_marks          numeric(6,2)  NOT NULL,
    status_id          integer       NOT NULL,
    created_at         timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at         timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_class_assessments_pkey PRIMARY KEY (assessment_id),
    CONSTRAINT fk_tb_ca_school   FOREIGN KEY (school_id)            REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_ca_year     FOREIGN KEY (academic_year_id)     REFERENCES public.tb_academic_years (academic_year_id),
    CONSTRAINT fk_tb_ca_section  FOREIGN KEY (section_id, class_id) REFERENCES public.tb_sections (section_id, class_id),
    CONSTRAINT fk_tb_ca_subject  FOREIGN KEY (subject_id)           REFERENCES public.tb_subjects (subject_id),
    CONSTRAINT fk_tb_ca_type     FOREIGN KEY (assessment_type_id)   REFERENCES public.lut_assessment_type (assessment_type_id),
    CONSTRAINT fk_tb_ca_employee FOREIGN KEY (employee_id)          REFERENCES public.tb_employees (employee_id),
    CONSTRAINT fk_tb_ca_status   FOREIGN KEY (status_id)            REFERENCES public.lut_status (sid),
    CONSTRAINT chk_tb_ca_max_marks CHECK (max_marks > 0),
    CONSTRAINT chk_tb_ca_title     CHECK (length(btrim(title)) > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_ca_section ON public.tb_class_assessments (school_id, section_id, test_date);
CREATE INDEX IF NOT EXISTS idx_tb_ca_teacher ON public.tb_class_assessments (school_id, employee_id, test_date);
CREATE INDEX IF NOT EXISTS idx_tb_ca_year    ON public.tb_class_assessments (school_id, academic_year_id);

CREATE TABLE IF NOT EXISTS public.tb_student_assessment_marks
(
    mark_id         bigint        GENERATED ALWAYS AS IDENTITY,
    assessment_id   bigint        NOT NULL,
    student_id      varchar(20)   NOT NULL,
    obtained_marks  numeric(6,2),
    is_absent       boolean       NOT NULL DEFAULT false,
    created_at      timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_student_assessment_marks_pkey PRIMARY KEY (mark_id),
    CONSTRAINT fk_tb_sam_assessment FOREIGN KEY (assessment_id) REFERENCES public.tb_class_assessments (assessment_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_sam_student    FOREIGN KEY (student_id)    REFERENCES public.tb_students (student_id),
    CONSTRAINT uq_tb_sam_assessment_student UNIQUE (assessment_id, student_id),
    CONSTRAINT chk_tb_sam_marks_or_absent CHECK ((is_absent AND obtained_marks IS NULL) OR (NOT is_absent AND obtained_marks IS NOT NULL)),
    CONSTRAINT chk_tb_sam_non_negative    CHECK (obtained_marks IS NULL OR obtained_marks >= 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_sam_student ON public.tb_student_assessment_marks (student_id);

COMMIT;
