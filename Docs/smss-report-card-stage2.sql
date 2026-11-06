-- Examination, Results & Report Card: STAGE 2 (marks entry and class-teacher inputs). No results, no publishing, no PDFs yet.
--
-- Adds (new tables; the only change to existing tables is a handful of UNIQUE keys so composite foreign keys can anchor on them):
--   tb_report_entry_status                 one row per (exam subject, section): DRAFT / SUBMITTED / ACCEPTED + a version token for concurrent saves
--   tb_report_marks                        the entries: marks or a grade, or ABSENT / EXEMPT, per (exam subject, [skill], student)
--   tb_report_term_section                 per (term, section): OPEN / LOCKED. THE ONLY lock authority.
--   tb_report_student_activity_grades      co-scholastic / discipline grades per (term, student, area)
--   tb_report_term_student                 per (term, student): remark, conduct, rank text, manual working / present days
--   tb_report_year_student                 per (scheme, student): promotion decision, promoted-to text, new session date
--   tb_report_audit                        append-only history of submit / accept / reopen / lock / unlock
-- Needs: the Stage 1 migration, tb_students, tb_sections (UNIQUE (section_id, class_id)), tb_users. Run BEFORE starting the API build that contains these entities. Safe to run twice.
--
-- Decisions (approved design, Phase A + review):
--  * status_id is not used here: the states are short text values with CHECKs (the project's lut_status describes general "Active/Inactive" rows, not workflow states).
--  * EXIT CRITERION from Stage 1: the foreign keys from tb_report_marks and tb_report_entry_status to tb_report_exam_subjects and tb_report_subject_skills are plain
--    (NO ACTION), NOT cascading. The Stage 1 configuration tables cascade downwards, so deleting an exam, exam subject, scheme subject, skill or class that has entries
--    is refused by the database itself even if the service guard were bypassed. The service guard (IReportEntryGuard) answers first, with a clear 409.
--  * ONE source of truth for locking: tb_report_term_section.status. The entry status has only DRAFT / SUBMITTED / ACCEPTED; "locked" is derived from the term-section
--    row, so the two can never contradict each other. Lock = one transaction that requires every exam subject of the term ACCEPTED and writes the row + an audit row.
--    Unlock = School Admin only, reason required, audited; entries STAY ACCEPTED, so changing one still needs an audited reopen with a reason.
--  * Concurrency: tb_report_entry_status.version is a token. A save / submit / accept / reopen carries the version it read and only succeeds if it is still current
--    (the row is locked FOR UPDATE for the check), otherwise the service answers 409 "someone else saved first".
--  * A student who left the section keeps their marks: marks are unique per (exam subject, skill, student) and carry the section they were entered under, not a live roster link.
--  * Skills apply to GRADE entry only; a MARKS cell has a single value per student (skill_id NULL).
--  * Entry value rules a CHECK can express are here (state vs value columns, 0 <= marks <= 1000). Rules that need another row (marks <= THIS exam subject's max, grade in
--    the scheme's scale, student on the section's roster for the scheme's year) are the service's.
--  * tb_report_audit is append-only: a tiny trigger refuses UPDATE and DELETE. It is not a generic audit framework and must not grow into one. It keeps plain ids (no foreign
--    keys to configuration rows) so history survives later setup changes. Test clean-up scripts disable the trigger explicitly inside their own transaction, never app code.

BEGIN;

-- ============================================================ anchors on existing tables (UNIQUE keys only; nothing is altered or rewritten)
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_res_id_school') THEN
        ALTER TABLE public.tb_report_exam_subjects ADD CONSTRAINT uq_tb_res_id_school UNIQUE (exam_subject_id, school_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_res_id_subject') THEN
        ALTER TABLE public.tb_report_exam_subjects ADD CONSTRAINT uq_tb_res_id_subject UNIQUE (exam_subject_id, scheme_subject_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_rsk_id_subject') THEN
        ALTER TABLE public.tb_report_subject_skills ADD CONSTRAINT uq_tb_rsk_id_subject UNIQUE (skill_id, scheme_subject_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_raa_id_school') THEN
        ALTER TABLE public.tb_report_activity_areas ADD CONSTRAINT uq_tb_raa_id_school UNIQUE (area_id, school_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_students_id_school') THEN
        ALTER TABLE public.tb_students ADD CONSTRAINT uq_tb_students_id_school UNIQUE (student_id, school_id);
    END IF;
END $$;

-- ============================================================ entry status per (exam subject, section)
CREATE TABLE IF NOT EXISTS public.tb_report_entry_status
(
    exam_subject_id bigint       NOT NULL,
    section_id      bigint       NOT NULL,
    class_id        bigint       NOT NULL,
    school_id       varchar(100) NOT NULL,
    status          varchar(10)  NOT NULL DEFAULT 'DRAFT',
    version         integer      NOT NULL DEFAULT 1,
    submitted_by    bigint,
    submitted_at    timestamptz,
    accepted_by     bigint,
    accepted_at     timestamptz,
    created_at      timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_report_entry_status_pkey PRIMARY KEY (exam_subject_id, section_id),
    -- NO ACTION on purpose: an exam subject that has an entry status can not be deleted from under it
    CONSTRAINT fk_tb_res_status_examsubject_class  FOREIGN KEY (exam_subject_id, class_id)  REFERENCES public.tb_report_exam_subjects (exam_subject_id, class_id),
    CONSTRAINT fk_tb_res_status_examsubject_school FOREIGN KEY (exam_subject_id, school_id) REFERENCES public.tb_report_exam_subjects (exam_subject_id, school_id),
    CONSTRAINT fk_tb_res_status_section FOREIGN KEY (section_id, class_id) REFERENCES public.tb_sections (section_id, class_id),
    CONSTRAINT fk_tb_res_status_submitter FOREIGN KEY (submitted_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_res_status_accepter  FOREIGN KEY (accepted_by)  REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_res_status_values CHECK (status IN ('DRAFT', 'SUBMITTED', 'ACCEPTED')),
    CONSTRAINT chk_tb_res_status_version CHECK (version >= 1),
    -- a submitted or accepted grid says who submitted it and when; an accepted one also who accepted it
    CONSTRAINT chk_tb_res_status_submitted CHECK (status = 'DRAFT' OR (submitted_by IS NOT NULL AND submitted_at IS NOT NULL)),
    CONSTRAINT chk_tb_res_status_accepted CHECK (status <> 'ACCEPTED' OR (accepted_by IS NOT NULL AND accepted_at IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS idx_tb_res_status_section ON public.tb_report_entry_status (school_id, section_id);

-- ============================================================ the entries
CREATE TABLE IF NOT EXISTS public.tb_report_marks
(
    mark_id           bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    school_id         varchar(100) NOT NULL,
    exam_subject_id   bigint       NOT NULL,
    scheme_subject_id bigint       NOT NULL,
    skill_id          bigint,                          -- NULL for a MARKS cell or a subject without skills
    student_id        varchar(20)  NOT NULL,
    section_id        bigint       NOT NULL,           -- the section the entry was made under (history: the student may leave later)
    entry_state       varchar(8)   NOT NULL,
    marks             numeric(6,2),
    grade_code        varchar(10),
    entered_by        bigint,
    created_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_report_marks_pkey PRIMARY KEY (mark_id),
    -- NO ACTION on purpose (see the header): entries can never be deleted by a cascade from setup
    CONSTRAINT fk_tb_rm_examsubject_subject FOREIGN KEY (exam_subject_id, scheme_subject_id) REFERENCES public.tb_report_exam_subjects (exam_subject_id, scheme_subject_id),
    CONSTRAINT fk_tb_rm_examsubject_school  FOREIGN KEY (exam_subject_id, school_id)         REFERENCES public.tb_report_exam_subjects (exam_subject_id, school_id),
    CONSTRAINT fk_tb_rm_skill   FOREIGN KEY (skill_id, scheme_subject_id) REFERENCES public.tb_report_subject_skills (skill_id, scheme_subject_id),
    CONSTRAINT fk_tb_rm_student FOREIGN KEY (student_id, school_id)       REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_rm_grid    FOREIGN KEY (exam_subject_id, section_id) REFERENCES public.tb_report_entry_status (exam_subject_id, section_id),
    CONSTRAINT fk_tb_rm_enteredby FOREIGN KEY (entered_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_rm_state CHECK (entry_state IN ('ENTERED', 'ABSENT', 'EXEMPT')),
    -- an ENTERED row carries exactly one of marks / grade; ABSENT and EXEMPT carry neither
    CONSTRAINT chk_tb_rm_values CHECK (
        (entry_state = 'ENTERED' AND ((marks IS NOT NULL)::int + (grade_code IS NOT NULL)::int) = 1)
        OR (entry_state <> 'ENTERED' AND marks IS NULL AND grade_code IS NULL)),
    CONSTRAINT chk_tb_rm_marks_range CHECK (marks IS NULL OR (marks >= 0 AND marks <= 1000)),
    CONSTRAINT chk_tb_rm_grade CHECK (grade_code IS NULL OR length(btrim(grade_code)) > 0)
);
-- one entry per (exam subject, skill, student); NULL skill needs its own partial index because NULLs are distinct in a plain unique key
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_rm_noskill ON public.tb_report_marks (exam_subject_id, student_id) WHERE skill_id IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_rm_skill   ON public.tb_report_marks (exam_subject_id, skill_id, student_id) WHERE skill_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_tb_rm_grid    ON public.tb_report_marks (exam_subject_id, section_id);
CREATE INDEX IF NOT EXISTS idx_tb_rm_student ON public.tb_report_marks (student_id);

-- ============================================================ lock per (term, section): the only lock authority
CREATE TABLE IF NOT EXISTS public.tb_report_term_section
(
    term_id       bigint       NOT NULL,
    section_id    bigint       NOT NULL,
    scheme_id     bigint       NOT NULL,
    class_id      bigint       NOT NULL,
    school_id     varchar(100) NOT NULL,
    status        varchar(6)   NOT NULL DEFAULT 'OPEN',
    locked_by     bigint,
    locked_at     timestamptz,
    unlocked_by   bigint,
    unlocked_at   timestamptz,
    unlock_reason varchar(300),
    updated_at    timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_report_term_section_pkey PRIMARY KEY (term_id, section_id),
    CONSTRAINT fk_tb_rts_term    FOREIGN KEY (term_id, scheme_id)   REFERENCES public.tb_report_terms (term_id, scheme_id),
    CONSTRAINT fk_tb_rts_scheme  FOREIGN KEY (scheme_id, school_id) REFERENCES public.tb_report_schemes (scheme_id, school_id),
    CONSTRAINT fk_tb_rts_class   FOREIGN KEY (scheme_id, class_id)  REFERENCES public.tb_report_scheme_classes (scheme_id, class_id),
    CONSTRAINT fk_tb_rts_section FOREIGN KEY (section_id, class_id) REFERENCES public.tb_sections (section_id, class_id),
    CONSTRAINT fk_tb_rts_locker   FOREIGN KEY (locked_by)   REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_rts_unlocker FOREIGN KEY (unlocked_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_rts_status CHECK (status IN ('OPEN', 'LOCKED')),
    CONSTRAINT chk_tb_rts_locked CHECK (status <> 'LOCKED' OR (locked_by IS NOT NULL AND locked_at IS NOT NULL)),
    CONSTRAINT chk_tb_rts_unlock_reason CHECK (unlocked_at IS NULL OR (unlock_reason IS NOT NULL AND length(btrim(unlock_reason)) >= 3))
);
CREATE INDEX IF NOT EXISTS idx_tb_rts_scheme_section ON public.tb_report_term_section (scheme_id, section_id);

-- ============================================================ class-teacher inputs
CREATE TABLE IF NOT EXISTS public.tb_report_student_activity_grades
(
    term_id    bigint       NOT NULL,
    student_id varchar(20)  NOT NULL,
    area_id    bigint       NOT NULL,
    scheme_id  bigint       NOT NULL,
    section_id bigint       NOT NULL,
    school_id  varchar(100) NOT NULL,
    grade_code varchar(10)  NOT NULL,
    updated_by bigint,
    updated_at timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_report_student_activity_grades_pkey PRIMARY KEY (term_id, student_id, area_id),
    CONSTRAINT fk_tb_rsag_term    FOREIGN KEY (term_id, scheme_id)   REFERENCES public.tb_report_terms (term_id, scheme_id),
    CONSTRAINT fk_tb_rsag_scheme  FOREIGN KEY (scheme_id, school_id) REFERENCES public.tb_report_schemes (scheme_id, school_id),
    CONSTRAINT fk_tb_rsag_area    FOREIGN KEY (area_id, school_id)   REFERENCES public.tb_report_activity_areas (area_id, school_id),
    CONSTRAINT fk_tb_rsag_student FOREIGN KEY (student_id, school_id) REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_rsag_section FOREIGN KEY (section_id) REFERENCES public.tb_sections (section_id),
    CONSTRAINT fk_tb_rsag_updater FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_rsag_grade CHECK (length(btrim(grade_code)) > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_rsag_section ON public.tb_report_student_activity_grades (term_id, section_id);

CREATE TABLE IF NOT EXISTS public.tb_report_term_student
(
    term_id             bigint       NOT NULL,
    student_id          varchar(20)  NOT NULL,
    scheme_id           bigint       NOT NULL,
    section_id          bigint       NOT NULL,
    school_id           varchar(100) NOT NULL,
    remark              varchar(500),
    conduct             varchar(60),
    rank_text           varchar(30),                  -- GRADES schemes: the class teacher types the rank (MARKS schemes compute it)
    manual_working_days smallint,                     -- used only when the scheme's attendance source is MANUAL
    manual_present_days smallint,
    updated_by          bigint,
    updated_at          timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_report_term_student_pkey PRIMARY KEY (term_id, student_id),
    CONSTRAINT fk_tb_rts2_term    FOREIGN KEY (term_id, scheme_id)   REFERENCES public.tb_report_terms (term_id, scheme_id),
    CONSTRAINT fk_tb_rts2_scheme  FOREIGN KEY (scheme_id, school_id) REFERENCES public.tb_report_schemes (scheme_id, school_id),
    CONSTRAINT fk_tb_rts2_student FOREIGN KEY (student_id, school_id) REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_rts2_section FOREIGN KEY (section_id) REFERENCES public.tb_sections (section_id),
    CONSTRAINT fk_tb_rts2_updater FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_rts2_days CHECK (
        (manual_working_days IS NULL OR (manual_working_days >= 0 AND manual_working_days <= 366))
        AND (manual_present_days IS NULL OR manual_present_days >= 0)
        AND (manual_present_days IS NULL OR manual_working_days IS NULL OR manual_present_days <= manual_working_days))
);
CREATE INDEX IF NOT EXISTS idx_tb_rts2_section ON public.tb_report_term_student (term_id, section_id);

CREATE TABLE IF NOT EXISTS public.tb_report_year_student
(
    scheme_id          bigint       NOT NULL,
    student_id         varchar(20)  NOT NULL,
    section_id         bigint       NOT NULL,
    school_id          varchar(100) NOT NULL,
    promotion_decision varchar(12),
    promoted_to        varchar(60),
    new_session_date   date,
    updated_by         bigint,
    updated_at         timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_report_year_student_pkey PRIMARY KEY (scheme_id, student_id),
    CONSTRAINT fk_tb_rys_scheme  FOREIGN KEY (scheme_id, school_id)  REFERENCES public.tb_report_schemes (scheme_id, school_id),
    CONSTRAINT fk_tb_rys_student FOREIGN KEY (student_id, school_id) REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_rys_section FOREIGN KEY (section_id) REFERENCES public.tb_sections (section_id),
    CONSTRAINT fk_tb_rys_updater FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_rys_decision CHECK (promotion_decision IS NULL OR promotion_decision IN ('PROMOTED', 'NOT_PROMOTED', 'DETAINED'))
);
CREATE INDEX IF NOT EXISTS idx_tb_rys_section ON public.tb_report_year_student (scheme_id, section_id);

-- ============================================================ audit (append-only)
CREATE TABLE IF NOT EXISTS public.tb_report_audit
(
    audit_id        bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    school_id       varchar(100) NOT NULL,
    event           varchar(10)  NOT NULL,
    scheme_id       bigint       NOT NULL,
    term_id         bigint,
    exam_subject_id bigint,
    section_id      bigint       NOT NULL,
    from_status     varchar(10),
    to_status       varchar(10),
    reason          varchar(300),
    actor_user_id   bigint       NOT NULL,
    created_at      timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_report_audit_pkey PRIMARY KEY (audit_id),
    CONSTRAINT fk_tb_ra_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_ra_actor  FOREIGN KEY (actor_user_id) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_ra_event CHECK (event IN ('SUBMIT', 'ACCEPT', 'REOPEN', 'LOCK', 'UNLOCK')),
    -- a reopen and an unlock always say why
    CONSTRAINT chk_tb_ra_reason CHECK (event NOT IN ('REOPEN', 'UNLOCK') OR (reason IS NOT NULL AND length(btrim(reason)) >= 3)),
    -- grid events name their exam subject; lock events name their term
    CONSTRAINT chk_tb_ra_target CHECK ((event IN ('SUBMIT', 'ACCEPT', 'REOPEN') AND exam_subject_id IS NOT NULL) OR (event IN ('LOCK', 'UNLOCK') AND term_id IS NOT NULL))
);
CREATE INDEX IF NOT EXISTS idx_tb_ra_section ON public.tb_report_audit (school_id, section_id, created_at);

CREATE OR REPLACE FUNCTION public.tb_report_audit_append_only() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'tb_report_audit is append-only: % is not allowed', TG_OP USING ERRCODE = 'restrict_violation';
END;
$$;
DROP TRIGGER IF EXISTS trg_tb_report_audit_append_only ON public.tb_report_audit;
CREATE TRIGGER trg_tb_report_audit_append_only BEFORE UPDATE OR DELETE ON public.tb_report_audit
    FOR EACH ROW EXECUTE FUNCTION public.tb_report_audit_append_only();

COMMIT;
