-- Examination, Results & Report Card: STAGE 1 (setup only). No marks, no results, no PDFs yet.
--
-- Adds (all new, nothing existing is altered):
--   tb_grade_scales, tb_grade_scale_bands                         grade scales (percent bands or ratings)
--   tb_report_schemes, tb_report_scheme_classes                   one scheme per class group per academic year
--   tb_report_terms, tb_report_exams, tb_report_exam_class_dates  terms, exams, optional per-class exam dates
--   tb_report_scheme_subjects, tb_report_exam_subjects            subjects of the scheme and the exam x subject matrix
--   tb_report_subject_skills                                      skills of a subject (Reading, Writing, Oral ...)
--   tb_report_activity_areas                                      co-scholastic / discipline areas (school level)
-- Needs: tb_schools, tb_academic_years, tb_classes, tb_subjects, lut_status. Run BEFORE starting the API build that contains these entities. Safe to run twice.
--
-- Decisions (approved design, Phase A):
--  * status_id has EXACTLY the type of lut_status's key. That column is lut_status.sid and it is INTEGER (every status_id in the project is integer), so integer it is.
--  * Bands store only min_percent (lower edge, INCLUSIVE). The upper edge is the next band's min_percent, so with two-decimal percentages there is never a gap
--    (91-100 / 81-90 would leave 90.01-90.99 unclassified). First band must start at 0, strictly ascending: checked by the app (a rule across rows).
--  * Presets (grade scales, schemes, activity areas, card templates) are CODE CONSTANTS copied into the school's own rows on request: no seed data here.
--  * include_in_total lives on the scheme subject (a subject is in or out of the totals for the whole year, not exam by exam). show_rank lives on the scheme only.
--  * Every child table carries school_id and is tied to its parent with a composite FK, so a row can never point at another school's parent.
--    (A class is tied to its scheme through tb_report_scheme_classes, so a subject can only be added for a class that is in the scheme.)
--  * The configuration children (terms, exams, subjects, matrix, skills) are hard-deleted when replaced; they carry no history of their own. Stage 2's marks table will
--    reference tb_report_exam_subjects WITHOUT cascade, so a cell that has entries can never be deleted from under them. Scales, schemes and areas are soft-deleted (status_id).
--  * A class belongs to at most one ACTIVE scheme per academic year: UNIQUE (academic_year_id, class_id). Deactivating a scheme removes its class rows (app).
--  * Carried forward to Stage 2 (so nobody has to rediscover it): there is ONE source of truth for locking, tb_report_term_section.status. The entry status per
--    (exam subject, section) has only DRAFT / SUBMITTED / ACCEPTED; "locked" is derived from the term-section row, so the two can never contradict each other.
--    Lock = one transaction that requires every entry ACCEPTED and writes the term-section row + an audit row. Unlock = School Admin only, reason required, audited;
--    entries stay ACCEPTED, published versions are untouched, and re-publishing needs a lock again.

BEGIN;

-- ============================================================ grade scales
CREATE TABLE IF NOT EXISTS public.tb_grade_scales
(
    grade_scale_id      bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    school_id           varchar(100) NOT NULL,
    scale_name          varchar(80)  NOT NULL,
    scale_kind          varchar(15)  NOT NULL,
    source_preset_code  varchar(40),
    status_id           integer      NOT NULL,
    created_at          timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_grade_scales_pkey PRIMARY KEY (grade_scale_id),
    CONSTRAINT uq_tb_grade_scales_id_school UNIQUE (grade_scale_id, school_id),
    CONSTRAINT fk_tb_grade_scales_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_grade_scales_status FOREIGN KEY (status_id) REFERENCES public.lut_status (sid),
    CONSTRAINT chk_tb_grade_scales_kind CHECK (scale_kind IN ('PERCENT_BANDS', 'RATING')),
    CONSTRAINT chk_tb_grade_scales_name CHECK (length(btrim(scale_name)) > 0)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_grade_scales_school_name ON public.tb_grade_scales (school_id, lower(scale_name)) WHERE status_id = 1;
CREATE INDEX IF NOT EXISTS idx_tb_grade_scales_school ON public.tb_grade_scales (school_id);

CREATE TABLE IF NOT EXISTS public.tb_grade_scale_bands
(
    band_id        bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    grade_scale_id bigint       NOT NULL,
    school_id      varchar(100) NOT NULL,
    sequence       smallint     NOT NULL,
    grade_code     varchar(10)  NOT NULL,
    label          varchar(60)  NOT NULL,
    min_percent    numeric(5,2),                      -- PERCENT_BANDS: lower edge, inclusive. RATING: NULL (ordered by sequence).
    grade_point    numeric(4,1),
    is_fail        boolean      NOT NULL DEFAULT false,
    description    varchar(200),
    CONSTRAINT tb_grade_scale_bands_pkey PRIMARY KEY (band_id),
    CONSTRAINT fk_tb_gsb_scale FOREIGN KEY (grade_scale_id, school_id) REFERENCES public.tb_grade_scales (grade_scale_id, school_id) ON DELETE CASCADE,
    CONSTRAINT uq_tb_gsb_sequence UNIQUE (grade_scale_id, sequence),
    CONSTRAINT uq_tb_gsb_code UNIQUE (grade_scale_id, grade_code),
    CONSTRAINT chk_tb_gsb_percent CHECK (min_percent IS NULL OR (min_percent >= 0 AND min_percent <= 100)),
    CONSTRAINT chk_tb_gsb_code CHECK (length(btrim(grade_code)) > 0),
    CONSTRAINT chk_tb_gsb_label CHECK (length(btrim(label)) > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_gsb_scale ON public.tb_grade_scale_bands (grade_scale_id);

-- ============================================================ report schemes (one per class group per academic year)
CREATE TABLE IF NOT EXISTS public.tb_report_schemes
(
    scheme_id             bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    school_id             varchar(100) NOT NULL,
    academic_year_id      bigint       NOT NULL,
    scheme_name           varchar(80)  NOT NULL,
    evaluation_mode       varchar(10)  NOT NULL,
    grade_scale_id        bigint,
    year_combine          varchar(15)  NOT NULL DEFAULT 'SUM_OF_TERMS',
    subject_pass_percent  numeric(5,2) NOT NULL DEFAULT 33,
    overall_pass_percent  numeric(5,2) NOT NULL DEFAULT 33,
    show_rank             boolean      NOT NULL DEFAULT false,
    rank_scope            varchar(10)  NOT NULL DEFAULT 'SECTION',
    attendance_source     varchar(10)  NOT NULL DEFAULT 'AUTO',
    percentage_rounding   varchar(15)  NOT NULL DEFAULT 'ROUND_HALF_UP',
    template_key          varchar(30)  NOT NULL,
    status_id             integer      NOT NULL,
    created_by            bigint,
    updated_by            bigint,
    created_at            timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at            timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_report_schemes_pkey PRIMARY KEY (scheme_id),
    CONSTRAINT uq_tb_report_schemes_id_school UNIQUE (scheme_id, school_id),
    CONSTRAINT uq_tb_report_schemes_id_school_year UNIQUE (scheme_id, school_id, academic_year_id),
    CONSTRAINT fk_tb_report_schemes_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_report_schemes_year   FOREIGN KEY (academic_year_id) REFERENCES public.tb_academic_years (academic_year_id),
    CONSTRAINT fk_tb_report_schemes_scale  FOREIGN KEY (grade_scale_id, school_id) REFERENCES public.tb_grade_scales (grade_scale_id, school_id),
    CONSTRAINT fk_tb_report_schemes_status FOREIGN KEY (status_id) REFERENCES public.lut_status (sid),
    CONSTRAINT fk_tb_report_schemes_creator FOREIGN KEY (created_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_report_schemes_updater FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_report_schemes_name CHECK (length(btrim(scheme_name)) > 0),
    CONSTRAINT chk_tb_report_schemes_mode CHECK (evaluation_mode IN ('MARKS', 'GRADES')),
    CONSTRAINT chk_tb_report_schemes_combine CHECK (year_combine IN ('SUM_OF_TERMS', 'WEIGHTED')),
    CONSTRAINT chk_tb_report_schemes_rank_scope CHECK (rank_scope IN ('SECTION', 'CLASS')),
    CONSTRAINT chk_tb_report_schemes_attendance CHECK (attendance_source IN ('AUTO', 'MANUAL')),
    CONSTRAINT chk_tb_report_schemes_rounding CHECK (percentage_rounding IN ('ROUND_HALF_UP', 'TRUNCATE')),
    CONSTRAINT chk_tb_report_schemes_pass CHECK (subject_pass_percent BETWEEN 0 AND 100 AND overall_pass_percent BETWEEN 0 AND 100),
    CONSTRAINT chk_tb_report_schemes_template CHECK (template_key ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_report_schemes_year_name ON public.tb_report_schemes (school_id, academic_year_id, lower(scheme_name)) WHERE status_id = 1;
CREATE INDEX IF NOT EXISTS idx_tb_report_schemes_school_year ON public.tb_report_schemes (school_id, academic_year_id);

CREATE TABLE IF NOT EXISTS public.tb_report_scheme_classes
(
    scheme_id        bigint       NOT NULL,
    school_id        varchar(100) NOT NULL,
    academic_year_id bigint       NOT NULL,        -- copied from the scheme (composite FK) so "one scheme per class per year" is a plain unique key
    class_id         bigint       NOT NULL,
    created_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_report_scheme_classes_pkey PRIMARY KEY (scheme_id, class_id),
    CONSTRAINT uq_tb_report_scheme_classes_year_class UNIQUE (academic_year_id, class_id),
    CONSTRAINT fk_tb_rsc_scheme FOREIGN KEY (scheme_id, school_id, academic_year_id) REFERENCES public.tb_report_schemes (scheme_id, school_id, academic_year_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_rsc_class  FOREIGN KEY (class_id) REFERENCES public.tb_classes (class_id)
);

-- ============================================================ terms, exams
CREATE TABLE IF NOT EXISTS public.tb_report_terms
(
    term_id      bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    scheme_id    bigint       NOT NULL,
    school_id    varchar(100) NOT NULL,
    term_name    varchar(60)  NOT NULL,
    sequence     smallint     NOT NULL,
    start_date   date         NOT NULL,           -- the date range attendance is counted over
    end_date     date         NOT NULL,
    year_weight  numeric(6,2),                    -- only used when the scheme combines the year WEIGHTED
    CONSTRAINT tb_report_terms_pkey PRIMARY KEY (term_id),
    CONSTRAINT uq_tb_report_terms_id_scheme UNIQUE (term_id, scheme_id),
    CONSTRAINT uq_tb_report_terms_seq UNIQUE (scheme_id, sequence),
    CONSTRAINT fk_tb_report_terms_scheme FOREIGN KEY (scheme_id, school_id) REFERENCES public.tb_report_schemes (scheme_id, school_id) ON DELETE CASCADE,
    CONSTRAINT chk_tb_report_terms_name CHECK (length(btrim(term_name)) > 0),
    CONSTRAINT chk_tb_report_terms_dates CHECK (end_date >= start_date),
    CONSTRAINT chk_tb_report_terms_weight CHECK (year_weight IS NULL OR year_weight > 0)
);

CREATE TABLE IF NOT EXISTS public.tb_report_exams
(
    exam_id            bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    term_id            bigint       NOT NULL,
    scheme_id          bigint       NOT NULL,
    school_id          varchar(100) NOT NULL,
    exam_name          varchar(60)  NOT NULL,
    sequence           smallint     NOT NULL,
    default_max_marks  numeric(6,2) NOT NULL,
    weight_points      numeric(6,2) NOT NULL,     -- what this exam adds to the term total (default = its max marks)
    exam_date          date,
    CONSTRAINT tb_report_exams_pkey PRIMARY KEY (exam_id),
    CONSTRAINT uq_tb_report_exams_id_scheme UNIQUE (exam_id, scheme_id),
    CONSTRAINT uq_tb_report_exams_seq UNIQUE (term_id, sequence),
    CONSTRAINT fk_tb_report_exams_term   FOREIGN KEY (term_id, scheme_id) REFERENCES public.tb_report_terms (term_id, scheme_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_report_exams_scheme FOREIGN KEY (scheme_id, school_id) REFERENCES public.tb_report_schemes (scheme_id, school_id) ON DELETE CASCADE,
    CONSTRAINT chk_tb_report_exams_name CHECK (length(btrim(exam_name)) > 0),
    CONSTRAINT chk_tb_report_exams_max CHECK (default_max_marks > 0 AND default_max_marks <= 1000),
    CONSTRAINT chk_tb_report_exams_weight CHECK (weight_points > 0 AND weight_points <= 1000)
);

CREATE TABLE IF NOT EXISTS public.tb_report_exam_class_dates
(
    exam_id    bigint       NOT NULL,
    scheme_id  bigint       NOT NULL,
    school_id  varchar(100) NOT NULL,
    class_id   bigint       NOT NULL,
    exam_date  date         NOT NULL,
    CONSTRAINT tb_report_exam_class_dates_pkey PRIMARY KEY (exam_id, class_id),
    CONSTRAINT fk_tb_recd_exam  FOREIGN KEY (exam_id, scheme_id) REFERENCES public.tb_report_exams (exam_id, scheme_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_recd_class FOREIGN KEY (scheme_id, class_id) REFERENCES public.tb_report_scheme_classes (scheme_id, class_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_recd_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id)
);

-- ============================================================ subjects of a scheme, and the exam x subject matrix
CREATE TABLE IF NOT EXISTS public.tb_report_scheme_subjects
(
    scheme_subject_id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    scheme_id         bigint       NOT NULL,
    school_id         varchar(100) NOT NULL,
    class_id          bigint       NOT NULL,
    subject_id        bigint       NOT NULL,
    display_order     smallint     NOT NULL DEFAULT 0,
    label_override    varchar(60),
    include_in_total  boolean      NOT NULL DEFAULT true,
    CONSTRAINT tb_report_scheme_subjects_pkey PRIMARY KEY (scheme_subject_id),
    CONSTRAINT uq_tb_rss_unit UNIQUE (scheme_id, class_id, subject_id),
    CONSTRAINT uq_tb_rss_id_scheme UNIQUE (scheme_subject_id, scheme_id),
    CONSTRAINT uq_tb_rss_id_scheme_class UNIQUE (scheme_subject_id, scheme_id, class_id),
    CONSTRAINT fk_tb_rss_scheme  FOREIGN KEY (scheme_id, school_id) REFERENCES public.tb_report_schemes (scheme_id, school_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_rss_class   FOREIGN KEY (scheme_id, class_id) REFERENCES public.tb_report_scheme_classes (scheme_id, class_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_rss_subject FOREIGN KEY (subject_id) REFERENCES public.tb_subjects (subject_id),
    CONSTRAINT chk_tb_rss_order CHECK (display_order >= 0),
    CONSTRAINT chk_tb_rss_label CHECK (label_override IS NULL OR length(btrim(label_override)) > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_rss_scheme ON public.tb_report_scheme_subjects (scheme_id);

CREATE TABLE IF NOT EXISTS public.tb_report_exam_subjects
(
    exam_subject_id   bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    exam_id           bigint       NOT NULL,
    scheme_id         bigint       NOT NULL,
    school_id         varchar(100) NOT NULL,
    scheme_subject_id bigint       NOT NULL,
    class_id          bigint       NOT NULL,
    max_marks         numeric(6,2),               -- NULL = the exam's default
    weight_points     numeric(6,2),               -- NULL = the exam's weight
    entry_mode        varchar(5)   NOT NULL,      -- MARKS or GRADE. In a MARKS scheme a GRADE cell is DISPLAY-ONLY: shown, never summed, ranked or failed.
    CONSTRAINT tb_report_exam_subjects_pkey PRIMARY KEY (exam_subject_id),
    CONSTRAINT uq_tb_res_cell UNIQUE (exam_id, scheme_subject_id),
    CONSTRAINT uq_tb_res_id_class UNIQUE (exam_subject_id, class_id),
    CONSTRAINT fk_tb_res_exam    FOREIGN KEY (exam_id, scheme_id) REFERENCES public.tb_report_exams (exam_id, scheme_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_res_subject FOREIGN KEY (scheme_subject_id, scheme_id, class_id) REFERENCES public.tb_report_scheme_subjects (scheme_subject_id, scheme_id, class_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_res_scheme  FOREIGN KEY (scheme_id, school_id) REFERENCES public.tb_report_schemes (scheme_id, school_id) ON DELETE CASCADE,
    CONSTRAINT chk_tb_res_mode CHECK (entry_mode IN ('MARKS', 'GRADE')),
    CONSTRAINT chk_tb_res_max CHECK (max_marks IS NULL OR (max_marks > 0 AND max_marks <= 1000)),
    CONSTRAINT chk_tb_res_weight CHECK (weight_points IS NULL OR (weight_points > 0 AND weight_points <= 1000)),
    CONSTRAINT chk_tb_res_grade_has_no_numbers CHECK (entry_mode = 'MARKS' OR (max_marks IS NULL AND weight_points IS NULL))
);
CREATE INDEX IF NOT EXISTS idx_tb_res_scheme_class ON public.tb_report_exam_subjects (scheme_id, class_id);

CREATE TABLE IF NOT EXISTS public.tb_report_subject_skills
(
    skill_id          bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    scheme_subject_id bigint       NOT NULL,
    scheme_id         bigint       NOT NULL,
    school_id         varchar(100) NOT NULL,
    skill_name        varchar(60)  NOT NULL,
    sequence          smallint     NOT NULL,
    CONSTRAINT tb_report_subject_skills_pkey PRIMARY KEY (skill_id),
    CONSTRAINT uq_tb_rsk_name UNIQUE (scheme_subject_id, skill_name),
    CONSTRAINT uq_tb_rsk_seq UNIQUE (scheme_subject_id, sequence),
    CONSTRAINT fk_tb_rsk_subject FOREIGN KEY (scheme_subject_id, scheme_id) REFERENCES public.tb_report_scheme_subjects (scheme_subject_id, scheme_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_rsk_scheme  FOREIGN KEY (scheme_id, school_id) REFERENCES public.tb_report_schemes (scheme_id, school_id) ON DELETE CASCADE,
    CONSTRAINT chk_tb_rsk_name CHECK (length(btrim(skill_name)) > 0)
);

-- ============================================================ activity areas (school level)
CREATE TABLE IF NOT EXISTS public.tb_report_activity_areas
(
    area_id          bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
    school_id        varchar(100) NOT NULL,
    category         varchar(15)  NOT NULL,
    area_name        varchar(60)  NOT NULL,
    sequence         smallint     NOT NULL DEFAULT 0,
    rating_scale_id  bigint       NOT NULL,
    status_id        integer      NOT NULL,
    created_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_report_activity_areas_pkey PRIMARY KEY (area_id),
    CONSTRAINT fk_tb_raa_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_raa_scale  FOREIGN KEY (rating_scale_id, school_id) REFERENCES public.tb_grade_scales (grade_scale_id, school_id),
    CONSTRAINT fk_tb_raa_status FOREIGN KEY (status_id) REFERENCES public.lut_status (sid),
    CONSTRAINT chk_tb_raa_category CHECK (category IN ('CO_SCHOLASTIC', 'DISCIPLINE', 'OTHER')),
    CONSTRAINT chk_tb_raa_name CHECK (length(btrim(area_name)) > 0)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_raa_name ON public.tb_report_activity_areas (school_id, category, lower(area_name)) WHERE status_id = 1;

COMMIT;
