-- Examination, Results & Report Card: STAGE 3 (results and publishing). Adds the only stored results: versioned publications. No other templates, no bulk PDFs, no settings yet.
--
-- Adds:
--   tb_report_publications           one row per published version of (section, scheme, scope, term): who, when, the config snapshot, who was left out
--   tb_report_publication_students   one row per student per version: the summary numbers and the whole card as jsonb (card_model). NO binary data: a logo or signature is a storage KEY.
-- Changes (both relax, nothing is removed):
--   tb_report_audit gets two plain columns (publication_id) and the event list gains PUBLISH.
-- Needs: Stage 1, Stage 2 and Stage 2b. Safe to run twice.
--
-- Decisions (approved design, Phase A + reviews):
--  * Immutability. A published card never changes. tb_report_publication_students rejects UPDATE and DELETE (a tiny trigger, the project's second). tb_report_publications rejects DELETE and
--    rejects every UPDATE except the status moving PUBLISHED -> SUPERSEDED. Clean-up scripts for tests disable these triggers INSIDE their own transaction only; the application never does.
--  * A re-publish is a NEW version; the previous version is flipped to SUPERSEDED in the same transaction. At most one PUBLISHED version per (section, scheme, scope, term): a partial unique index.
--  * The publication only holds students who were COMPLETE. Students left out (INCOMPLETE, including late joiners) are listed by id in excluded_students and picked up by a later version.
--  * result_status here is PASS or FAIL only (an INCOMPLETE student is never published).
--  * card_model is the exact model the template draws, so re-rendering a published card can never drift from its first render.

BEGIN;

-- ============================================================ publications
CREATE TABLE IF NOT EXISTS public.tb_report_publications
(
    publication_id    bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    school_id         varchar(100) NOT NULL,
    scheme_id         bigint       NOT NULL,
    section_id        bigint       NOT NULL,
    class_id          bigint       NOT NULL,
    scope             varchar(4)   NOT NULL,
    term_id           bigint,
    version           integer      NOT NULL,
    status            varchar(10)  NOT NULL DEFAULT 'PUBLISHED',
    published_by      bigint       NOT NULL,
    published_at      timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    note              varchar(300),
    config_snapshot   jsonb        NOT NULL,
    excluded_students text,                                   -- comma-separated ids of the students left out of this version
    student_count     integer      NOT NULL,
    CONSTRAINT tb_report_publications_pkey PRIMARY KEY (publication_id),
    CONSTRAINT uq_tb_rp_id_school UNIQUE (publication_id, school_id),
    CONSTRAINT fk_tb_rp_scheme  FOREIGN KEY (scheme_id, school_id) REFERENCES public.tb_report_schemes (scheme_id, school_id),
    CONSTRAINT fk_tb_rp_term    FOREIGN KEY (term_id, scheme_id)   REFERENCES public.tb_report_terms (term_id, scheme_id),
    CONSTRAINT fk_tb_rp_class   FOREIGN KEY (scheme_id, class_id)  REFERENCES public.tb_report_scheme_classes (scheme_id, class_id),
    CONSTRAINT fk_tb_rp_section FOREIGN KEY (section_id, class_id) REFERENCES public.tb_sections (section_id, class_id),
    CONSTRAINT fk_tb_rp_user    FOREIGN KEY (published_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_rp_scope CHECK ((scope = 'TERM' AND term_id IS NOT NULL) OR (scope = 'YEAR' AND term_id IS NULL)),
    CONSTRAINT chk_tb_rp_status CHECK (status IN ('PUBLISHED', 'SUPERSEDED')),
    CONSTRAINT chk_tb_rp_version CHECK (version >= 1),
    CONSTRAINT chk_tb_rp_count CHECK (student_count >= 0)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_rp_version ON public.tb_report_publications (section_id, scheme_id, scope, COALESCE(term_id, 0), version);
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_rp_current ON public.tb_report_publications (section_id, scheme_id, scope, COALESCE(term_id, 0)) WHERE status = 'PUBLISHED';
CREATE INDEX IF NOT EXISTS idx_tb_rp_school_section ON public.tb_report_publications (school_id, section_id);

CREATE TABLE IF NOT EXISTS public.tb_report_publication_students
(
    pub_student_id   bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    publication_id   bigint       NOT NULL,
    school_id        varchar(100) NOT NULL,
    student_id       varchar(20)  NOT NULL,
    student_name     varchar(200) NOT NULL,
    admission_number varchar(50)  NOT NULL,
    roll_number      varchar(30),
    result_status    varchar(10)  NOT NULL,
    total_obtained   numeric(10,2),
    total_max        numeric(10,2),
    percentage       numeric(6,2),
    grade_code       varchar(10),
    rank_position    integer,
    card_model       jsonb        NOT NULL,
    CONSTRAINT tb_report_publication_students_pkey PRIMARY KEY (pub_student_id),
    CONSTRAINT uq_tb_rps_student UNIQUE (publication_id, student_id),
    -- NO ACTION: a version with cards can never be deleted from under them
    CONSTRAINT fk_tb_rps_publication FOREIGN KEY (publication_id, school_id) REFERENCES public.tb_report_publications (publication_id, school_id),
    CONSTRAINT fk_tb_rps_student     FOREIGN KEY (student_id, school_id)     REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT chk_tb_rps_status CHECK (result_status IN ('PASS', 'FAIL')),
    CONSTRAINT chk_tb_rps_rank CHECK (rank_position IS NULL OR rank_position >= 1),
    CONSTRAINT chk_tb_rps_percent CHECK (percentage IS NULL OR (percentage >= 0 AND percentage <= 100)),
    CONSTRAINT chk_tb_rps_model CHECK (jsonb_typeof(card_model) = 'object')
);
CREATE INDEX IF NOT EXISTS idx_tb_rps_student ON public.tb_report_publication_students (school_id, student_id);

-- ============================================================ immutability (two tiny triggers)
CREATE OR REPLACE FUNCTION public.tb_report_publication_students_immutable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'tb_report_publication_students is immutable: % is not allowed (publish a new version instead)', TG_OP USING ERRCODE = 'restrict_violation';
END;
$$;
DROP TRIGGER IF EXISTS trg_tb_rps_immutable ON public.tb_report_publication_students;
CREATE TRIGGER trg_tb_rps_immutable BEFORE UPDATE OR DELETE ON public.tb_report_publication_students
    FOR EACH ROW EXECUTE FUNCTION public.tb_report_publication_students_immutable();

CREATE OR REPLACE FUNCTION public.tb_report_publications_guard() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'tb_report_publications rows are never deleted' USING ERRCODE = 'restrict_violation';
    END IF;
    -- the only change allowed: status PUBLISHED -> SUPERSEDED, with every other column untouched
    IF OLD.status = 'PUBLISHED' AND NEW.status = 'SUPERSEDED'
       AND NEW.publication_id = OLD.publication_id AND NEW.school_id = OLD.school_id AND NEW.scheme_id = OLD.scheme_id AND NEW.section_id = OLD.section_id AND NEW.class_id = OLD.class_id
       AND NEW.scope = OLD.scope AND NEW.term_id IS NOT DISTINCT FROM OLD.term_id AND NEW.version = OLD.version AND NEW.published_by = OLD.published_by AND NEW.published_at = OLD.published_at
       AND NEW.note IS NOT DISTINCT FROM OLD.note AND NEW.config_snapshot = OLD.config_snapshot AND NEW.excluded_students IS NOT DISTINCT FROM OLD.excluded_students AND NEW.student_count = OLD.student_count THEN
        RETURN NEW;
    END IF;
    RAISE EXCEPTION 'tb_report_publications is immutable except for the status moving PUBLISHED to SUPERSEDED' USING ERRCODE = 'restrict_violation';
END;
$$;
DROP TRIGGER IF EXISTS trg_tb_rp_guard ON public.tb_report_publications;
CREATE TRIGGER trg_tb_rp_guard BEFORE UPDATE OR DELETE ON public.tb_report_publications
    FOR EACH ROW EXECUTE FUNCTION public.tb_report_publications_guard();

-- ============================================================ audit: PUBLISH
ALTER TABLE public.tb_report_audit ADD COLUMN IF NOT EXISTS publication_id bigint;     -- a plain id (history survives anything), only set on PUBLISH
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_ra_event' AND pg_get_constraintdef(oid) NOT LIKE '%PUBLISH%') THEN
        ALTER TABLE public.tb_report_audit DROP CONSTRAINT chk_tb_ra_event;
        ALTER TABLE public.tb_report_audit ADD CONSTRAINT chk_tb_ra_event CHECK (event IN ('SUBMIT', 'ACCEPT', 'REOPEN', 'LOCK', 'UNLOCK', 'PUBLISH'));
    END IF;
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_ra_target' AND pg_get_constraintdef(oid) NOT LIKE '%PUBLISH%') THEN
        ALTER TABLE public.tb_report_audit DROP CONSTRAINT chk_tb_ra_target;
        ALTER TABLE public.tb_report_audit ADD CONSTRAINT chk_tb_ra_target CHECK (
            (event IN ('SUBMIT', 'ACCEPT', 'REOPEN') AND exam_subject_id IS NOT NULL) OR (event IN ('LOCK', 'UNLOCK') AND term_id IS NOT NULL) OR (event = 'PUBLISH' AND publication_id IS NOT NULL));
    END IF;
END $$;

COMMIT;
