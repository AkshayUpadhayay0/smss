-- Examination / Report Card, STAGE 5a, part 2: one branding / settings row per school for the report cards.
--
-- Adds: tb_school_report_card_settings. Additive and idempotent (safe to run twice). Needs tb_schools and tb_users.
-- Run BEFORE starting the API build that contains this entity.
--
-- Decisions (same pattern as tb_school_id_card_settings):
--  * ONE ROW PER SCHOOL, so school_id is the primary key. NO ROW = the defaults (navy and gold, cover and rules pages off, attendance and grade legend on): a school that has
--    never opened the settings screen keeps printing cards exactly as before. The app creates the row the first time a school saves (or uploads a signature).
--  * The DESIGN is not here: it is chosen per report scheme (tb_report_schemes.template_key, which already exists). The list of designs is code constants.
--  * primary_color / secondary_color: #rrggbb, the same check as the ID card settings.
--  * principal_signature_path is a key into the PRIVATE file store. Every upload gets a NEW file name; a published card stores the key it was drawn with, so replacing the
--    signature never changes an old card. The application never deletes a signature file that any publication still refers to.
--  * The school's name, address, phone and LOGO come from the school record (tb_schools.logo_url); nothing is copied here. recognition_text and udise_code are new because
--    the school record has no such fields.
--  * rules_text is the text of the optional rules page (up to 1500 characters, lines separated by a newline).
--  * Nothing is ever deleted from here; a school that wants the defaults back saves the defaults.

BEGIN;

CREATE TABLE IF NOT EXISTS public.tb_school_report_card_settings
(
    school_id                 varchar(100)  NOT NULL,
    primary_color             varchar(7)    NOT NULL DEFAULT '#1F3A5F',
    secondary_color           varchar(7)    NOT NULL DEFAULT '#C8963E',
    header_line               varchar(150),
    recognition_text          varchar(150),
    udise_code                varchar(20),
    footer_text               varchar(200),
    rules_text                varchar(1500),
    principal_signature_path  varchar(300),
    show_cover                boolean       NOT NULL DEFAULT false,
    show_rules                boolean       NOT NULL DEFAULT false,
    show_attendance           boolean       NOT NULL DEFAULT true,
    show_grade_legend         boolean       NOT NULL DEFAULT true,
    updated_by                bigint,
    created_at                timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at                timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_school_report_card_settings_pkey PRIMARY KEY (school_id),
    CONSTRAINT fk_tb_rcs_school  FOREIGN KEY (school_id)  REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_rcs_updater FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_rcs_primary   CHECK (primary_color   ~ '^#[0-9A-Fa-f]{6}$'),
    CONSTRAINT chk_tb_rcs_secondary CHECK (secondary_color ~ '^#[0-9A-Fa-f]{6}$')
);

COMMIT;
