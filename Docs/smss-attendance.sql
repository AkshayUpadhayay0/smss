-- Attendance (daily, per student, once per day; marked by the section's class teacher, corrected by the School Admin).
--
-- Adds: lut_attendance_status (global, seeded Present / Absent) and tb_student_attendance.
-- Needs tb_schools, tb_academic_years, tb_classes, tb_sections (with the UNIQUE (section_id, class_id) that smss-class-teacher-assignments.sql adds),
-- tb_students, tb_users. Run BEFORE starting the API build that contains these entities. Safe to run twice.
--
-- How the roster is modelled (what this module reads, nothing new): a student's section for a year is the row in tb_student_enrollments
-- (student_id, academic_year_id, class_section_id = tb_sections.section_id, status_id; UNIQUE (student_id, academic_year_id), so one section per
-- student per year). The ACTIVE roster of a section = enrollments of that year in that section with an Active enrollment AND an Active student.
--
-- Decisions:
--  * Statuses live in a lookup, so Late / Half-Day later are rows in lut_attendance_status, not a schema change.
--  * class_id (and school_id, academic_year_id) are denormalised onto each mark, like tb_student_fee_dues carries student_id, so class-wise
--    reports need no join; the composite FK to tb_sections (section_id, class_id) keeps them consistent.
--  * ONE mark per student per day, period: UNIQUE (student_id, attendance_date).
--  * corrected_by / corrected_at / correction_reason are filled only when a School Admin corrects a past day (all three together, reason non-blank).
--  * Lookup column is is_active (as lut_gender / lut_blood_group / lut_employment_status; lut_fee_frequency and lut_status spell it isactive).

BEGIN;

CREATE TABLE IF NOT EXISTS public.lut_attendance_status
(
    attendance_status_id bigint       GENERATED ALWAYS AS IDENTITY,
    status_code          varchar(30)  NOT NULL,
    status_name          varchar(50)  NOT NULL,
    is_active            boolean      NOT NULL DEFAULT true,
    created_at           timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT lut_attendance_status_pkey PRIMARY KEY (attendance_status_id),
    CONSTRAINT uq_lut_attendance_status_code UNIQUE (status_code),
    CONSTRAINT uq_lut_attendance_status_name UNIQUE (status_name)
);

INSERT INTO public.lut_attendance_status (status_code, status_name) VALUES
    ('PRESENT', 'Present'),
    ('ABSENT',  'Absent')
ON CONFLICT (status_code) DO NOTHING;

CREATE TABLE IF NOT EXISTS public.tb_student_attendance
(
    attendance_id        bigint        GENERATED ALWAYS AS IDENTITY,
    school_id            varchar(100)  NOT NULL,
    academic_year_id     bigint        NOT NULL,
    section_id           bigint        NOT NULL,
    class_id             bigint        NOT NULL,   -- denormalised from the section
    student_id           varchar(20)   NOT NULL,
    attendance_date      date          NOT NULL,
    attendance_status_id bigint        NOT NULL,
    marked_by            bigint        NOT NULL,   -- tb_users.user_id of whoever submitted (the class teacher, or a School Admin)
    marked_at            timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    corrected_by         bigint,                   -- set only when a School Admin corrects the day
    corrected_at         timestamptz,
    correction_reason    text,
    created_at           timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_student_attendance_pkey PRIMARY KEY (attendance_id),
    CONSTRAINT fk_tb_att_school        FOREIGN KEY (school_id)            REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_att_year          FOREIGN KEY (academic_year_id)     REFERENCES public.tb_academic_years (academic_year_id),
    CONSTRAINT fk_tb_att_class         FOREIGN KEY (class_id)             REFERENCES public.tb_classes (class_id),
    CONSTRAINT fk_tb_att_section       FOREIGN KEY (section_id, class_id) REFERENCES public.tb_sections (section_id, class_id),
    CONSTRAINT fk_tb_att_student       FOREIGN KEY (student_id)           REFERENCES public.tb_students (student_id),
    CONSTRAINT fk_tb_att_status        FOREIGN KEY (attendance_status_id) REFERENCES public.lut_attendance_status (attendance_status_id),
    CONSTRAINT fk_tb_att_marked_by     FOREIGN KEY (marked_by)            REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_att_corrected_by  FOREIGN KEY (corrected_by)         REFERENCES public.tb_users (user_id),
    CONSTRAINT uq_tb_att_student_date  UNIQUE (student_id, attendance_date),
    CONSTRAINT chk_tb_att_correction   CHECK ((corrected_by IS NULL AND corrected_at IS NULL AND correction_reason IS NULL)
                                              OR (corrected_by IS NOT NULL AND corrected_at IS NOT NULL AND correction_reason IS NOT NULL AND length(btrim(correction_reason)) > 0))
);
CREATE INDEX IF NOT EXISTS idx_tb_att_section_date ON public.tb_student_attendance (school_id, section_id, attendance_date);
CREATE INDEX IF NOT EXISTS idx_tb_att_class_date   ON public.tb_student_attendance (school_id, class_id, attendance_date);
CREATE INDEX IF NOT EXISTS idx_tb_att_year         ON public.tb_student_attendance (school_id, academic_year_id);

COMMIT;
