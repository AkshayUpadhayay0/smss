-- Student ID Card templates: one branding / settings row per school (tb_school_id_card_settings).
--
-- Adds: tb_school_id_card_settings. Needs tb_schools and tb_users. Run BEFORE starting the API build that contains this entity. Safe to run twice.
--
-- Decisions:
--  * ONE ROW PER SCHOOL, so school_id is the primary key (no identity column). NO ROW = the defaults (template "classic-blue" with that template's
--    own default colours): existing schools keep printing cards with no setup. The app creates the row the first time a school saves.
--  * template_code is one of the built-in designs ("classic-blue", "teal-geometric", "red-curve", "playful-blob", "modern-navy", "formal-classic").
--    The list is code constants, NOT a lookup table, so it is checked by the app (adding a design must not need a migration); the database only
--    guarantees the value looks like a code.
--  * primary_color / secondary_color: #rrggbb, the same check as lut_event_category.color_hex.
--  * principal_signature_path is a key into the PRIVATE file store (never a public URL), like employee / homework / admission documents.
--  * terms_text: a few short lines printed on the back of templates that have a terms area (max 300 characters, lines separated by a newline).
--  * show_* flags switch OPTIONAL lines off; a template only shows a line it has room for. Defaults: all on.
--  * Nothing is ever deleted from here; a school that wants the defaults back saves the defaults.

BEGIN;

CREATE TABLE IF NOT EXISTS public.tb_school_id_card_settings
(
    school_id                 varchar(100)  NOT NULL,
    template_code             varchar(30)   NOT NULL DEFAULT 'classic-blue',
    primary_color             varchar(7)    NOT NULL DEFAULT '#1E5BD8',
    secondary_color           varchar(7)    NOT NULL DEFAULT '#F5A623',
    tagline                   varchar(120),
    website                   varchar(150),
    email                     varchar(150),
    principal_signature_path  varchar(300),
    terms_text                varchar(300),
    show_dob                  boolean       NOT NULL DEFAULT true,
    show_address              boolean       NOT NULL DEFAULT true,
    show_blood_group          boolean       NOT NULL DEFAULT true,
    show_mother_name          boolean       NOT NULL DEFAULT true,
    updated_by                bigint,                          -- tb_users.user_id of the School Admin who saved last, from the token
    created_at                timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at                timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_school_id_card_settings_pkey PRIMARY KEY (school_id),
    CONSTRAINT fk_tb_idcard_settings_school  FOREIGN KEY (school_id)  REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_idcard_settings_updater FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_idcard_settings_template  CHECK (template_code ~ '^[a-z0-9]+(-[a-z0-9]+)*$'),
    CONSTRAINT chk_tb_idcard_settings_primary   CHECK (primary_color   ~ '^#[0-9A-Fa-f]{6}$'),
    CONSTRAINT chk_tb_idcard_settings_secondary CHECK (secondary_color ~ '^#[0-9A-Fa-f]{6}$')
);

COMMIT;
