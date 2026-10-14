-- Student Fee Assignment (Docs/roles-workflow.md section 27, the part that was deferred until the Student module existed):
--   tb_student_fee_structure  - which fee structure a student pays under, per academic year
--   tb_student_fee_concessions - the concessions applied to a student
--
-- Needs Docs/smss-fee-setup.sql and Docs/smss-student-id-varchar.sql applied first (tb_students.student_id is a varchar).
-- Run BEFORE starting the API build that contains these entities. Safe to run twice.
--
-- Where a student's class and academic year live: NOT on tb_students. They are on tb_student_enrollments
-- (academic_year_id + class_section_id -> tb_sections.class_id). The assignment therefore takes its academic year from the
-- enrollment it was made for.

BEGIN;

CREATE TABLE IF NOT EXISTS public.tb_student_fee_structure
(
    student_fee_id    bigint       GENERATED ALWAYS AS IDENTITY,
    student_id        varchar(20)  NOT NULL,
    fee_structure_id  bigint       NOT NULL,
    school_id         varchar(100) NOT NULL,
    academic_year_id  bigint       NOT NULL,
    created_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_student_fee_structure_pkey PRIMARY KEY (student_fee_id),
    CONSTRAINT fk_tb_student_fee_structure_student   FOREIGN KEY (student_id)       REFERENCES public.tb_students (student_id),
    CONSTRAINT fk_tb_student_fee_structure_structure FOREIGN KEY (fee_structure_id) REFERENCES public.tb_fee_structures (fee_structure_id),
    CONSTRAINT fk_tb_student_fee_structure_school    FOREIGN KEY (school_id)        REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_student_fee_structure_year      FOREIGN KEY (academic_year_id) REFERENCES public.tb_academic_years (academic_year_id),
    -- one fee structure per student per academic year
    CONSTRAINT uq_tb_student_fee_structure_student_year UNIQUE (student_id, academic_year_id)
);
CREATE INDEX IF NOT EXISTS idx_tb_student_fee_structure_school_id ON public.tb_student_fee_structure (school_id);
CREATE INDEX IF NOT EXISTS idx_tb_student_fee_structure_structure ON public.tb_student_fee_structure (fee_structure_id);

CREATE TABLE IF NOT EXISTS public.tb_student_fee_concessions
(
    student_fee_concession_id  bigint       GENERATED ALWAYS AS IDENTITY,
    student_id                 varchar(20)  NOT NULL,
    concession_id              bigint       NOT NULL,
    approved_by                bigint,
    remarks                    text,
    created_at                 timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at                 timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_student_fee_concessions_pkey PRIMARY KEY (student_fee_concession_id),
    CONSTRAINT fk_tb_student_fee_concessions_student    FOREIGN KEY (student_id)    REFERENCES public.tb_students (student_id),
    CONSTRAINT fk_tb_student_fee_concessions_concession FOREIGN KEY (concession_id) REFERENCES public.tb_fee_concessions (concession_id),
    CONSTRAINT fk_tb_student_fee_concessions_approved   FOREIGN KEY (approved_by)   REFERENCES public.tb_users (user_id),
    -- the same concession cannot be applied to a student twice
    CONSTRAINT uq_tb_student_fee_concessions_student_concession UNIQUE (student_id, concession_id)
);

COMMIT;
