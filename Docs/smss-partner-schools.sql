-- Partner Schools on the public landing page.
--
-- Adds: tb_schools.show_on_website. The Super Admin decides per school whether it is listed under "Our Partner Schools".
-- A school is shown only when it is Active (lut_status, resolved by name in the API) AND show_on_website = TRUE.
-- Default FALSE: no existing school appears on the public site until the Super Admin opts it in.
-- No logo column is added: tb_schools.logo_url (and its upload/storage) already exists and is used as is.
-- Safe to run twice.

BEGIN;

ALTER TABLE public.tb_schools ADD COLUMN IF NOT EXISTS show_on_website boolean NOT NULL DEFAULT FALSE;

-- Partial index: the public list reads only opted-in rows
CREATE INDEX IF NOT EXISTS ix_tb_schools_show_on_website ON public.tb_schools (school_name) WHERE show_on_website;

COMMIT;
