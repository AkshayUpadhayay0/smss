-- Examination / Report Card, STAGE 5a, part 1: per-subject remarks.
--
-- Adds: tb_report_subject_remarks. Additive and idempotent (safe to run twice); nothing existing is altered.
-- Needs: the Stage 1 and Stage 2 migrations (tb_report_terms, tb_report_scheme_subjects, tb_report_schemes), tb_students (UNIQUE (student_id, school_id)), tb_users.
-- Run BEFORE starting the API build that contains this entity.
--
-- Decisions:
--  * One remark per (term, scheme subject, student): the teacher's short comment on that subject for that term ("Writes neatly; practise tables"). It is NOT a mark, so there is
--    no submit / accept workflow: it is editable while the term-section is OPEN and locked together with the term-section (the app checks tb_report_term_section, the one lock
--    authority; there is deliberately no second lock flag here).
--  * Nothing can cross schools or schemes: the term and the scheme subject must belong to the SAME scheme (composite foreign keys on the existing UNIQUE (term_id, scheme_id) and
--    UNIQUE (scheme_subject_id, scheme_id) keys), the scheme to the school, and the student to the school.
--  * A blank remark is simply no row (the app deletes the row when a remark is cleared), so a row always carries text.
--  * Published cards never read this table: a publication stores its own copy of the remark inside the card model (immutable).

BEGIN;

CREATE TABLE IF NOT EXISTS public.tb_report_subject_remarks
(
    term_id           bigint       NOT NULL,
    scheme_subject_id bigint       NOT NULL,
    student_id        varchar(20)  NOT NULL,
    scheme_id         bigint       NOT NULL,
    school_id         varchar(100) NOT NULL,
    remark            varchar(200) NOT NULL,
    updated_by        bigint       NOT NULL,
    created_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_report_subject_remarks_pkey PRIMARY KEY (term_id, scheme_subject_id, student_id),
    CONSTRAINT fk_tb_rsr_term    FOREIGN KEY (term_id, scheme_id)           REFERENCES public.tb_report_terms (term_id, scheme_id),
    CONSTRAINT fk_tb_rsr_subject FOREIGN KEY (scheme_subject_id, scheme_id) REFERENCES public.tb_report_scheme_subjects (scheme_subject_id, scheme_id),
    CONSTRAINT fk_tb_rsr_scheme  FOREIGN KEY (scheme_id, school_id)         REFERENCES public.tb_report_schemes (scheme_id, school_id),
    CONSTRAINT fk_tb_rsr_student FOREIGN KEY (student_id, school_id)        REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_rsr_updater FOREIGN KEY (updated_by)                   REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_rsr_remark CHECK (length(btrim(remark)) > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_rsr_term_student ON public.tb_report_subject_remarks (term_id, student_id);
CREATE INDEX IF NOT EXISTS idx_tb_rsr_subject      ON public.tb_report_subject_remarks (scheme_subject_id);

COMMIT;
