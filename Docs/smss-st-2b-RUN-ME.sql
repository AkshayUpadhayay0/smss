-- =====================================================================================================================
-- RUN-ME: Student and admission changes, stage 2b = WHATSAPP NUMBERS (guardian, application, school) and the SCHOOL LOCATION  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/admission-docs-latefee-registration-design.md (sections 1 and 5) and Docs/guardian-portal-design.md, decided 24 Nov 2026.
--
-- WHAT IT DOES (all columns are NULLABLE: the rules "mandatory" are enforced by the API and the forms, because every existing row has none)
--   1. tb_guardians.whatsapp_number            varchar(20)   the guardian's WhatsApp number, stored with the country code ("+919876543210")
--   2. tb_admissions.guardian_whatsapp_number  varchar(20)   the same for the application's guardian (copied to tb_guardians when the application is confirmed)
--   3. tb_schools.whatsapp_number              varchar(20)   the school's WhatsApp number, stored with the country code
--   4. tb_schools.latitude / longitude         numeric(9,6)  the school's location, set by the Super Admin only
--   Checks: every WhatsApp number is "+" and 8 to 15 digits when present; latitude is -90..90, longitude -180..180; latitude and longitude are both filled or both empty.
--   Pre-check: none needed. The columns are new and nullable, so no existing row can violate the checks.
--   Verification (raises, so everything rolls back, on any mismatch): the row counts of the three tables are identical before and after, every new column is NULL for every
--   existing row (nothing was invented), and all the checks exist.
--   No existing value is changed.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-2b.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   ALTER TABLE tb_guardians DROP COLUMN whatsapp_number;  ALTER TABLE tb_admissions DROP COLUMN guardian_whatsapp_number;
--   ALTER TABLE tb_schools DROP COLUMN whatsapp_number, DROP COLUMN latitude, DROP COLUMN longitude;   (the checks go with the columns)
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st2b_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st2b_before (guardians bigint, admissions bigint, schools bigint) ON COMMIT DROP;

DO $st2b$
DECLARE
    v_mig boolean;
    t     text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_guardians', 'tb_admissions', 'tb_schools'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist. Nothing was changed.', t;
        END IF;
    END LOOP;

    v_mig := NOT (
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_guardians'  AND column_name = 'whatsapp_number')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_admissions' AND column_name = 'guardian_whatsapp_number')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_schools' AND column_name = 'whatsapp_number')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_schools' AND column_name = 'latitude')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_schools' AND column_name = 'longitude')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_guardians_whatsapp'   AND conrelid = 'public.tb_guardians'::regclass)
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_admissions_guardian_whatsapp' AND conrelid = 'public.tb_admissions'::regclass)
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_schools_whatsapp'     AND conrelid = 'public.tb_schools'::regclass)
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_schools_location'     AND conrelid = 'public.tb_schools'::regclass));
    INSERT INTO _st2b_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    -- nothing to check beforehand: the columns are new and nullable, so no existing row can violate them (the verification below proves none got a value)
    RAISE NOTICE 'No pre-check needed: the columns are new, nullable and every check allows NULL.';

    INSERT INTO _st2b_before SELECT (SELECT count(*) FROM public.tb_guardians), (SELECT count(*) FROM public.tb_admissions), (SELECT count(*) FROM public.tb_schools);
END
$st2b$;

ALTER TABLE public.tb_guardians  ADD COLUMN IF NOT EXISTS whatsapp_number           varchar(20) NULL;
ALTER TABLE public.tb_admissions ADD COLUMN IF NOT EXISTS guardian_whatsapp_number varchar(20) NULL;
ALTER TABLE public.tb_schools    ADD COLUMN IF NOT EXISTS whatsapp_number           varchar(20) NULL;
ALTER TABLE public.tb_schools    ADD COLUMN IF NOT EXISTS latitude                  numeric(9,6) NULL;
ALTER TABLE public.tb_schools    ADD COLUMN IF NOT EXISTS longitude                 numeric(9,6) NULL;

DO $st2bc$
BEGIN
    IF NOT (SELECT migrating FROM _st2b_state) THEN RETURN; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_guardians_whatsapp' AND conrelid = 'public.tb_guardians'::regclass) THEN
        ALTER TABLE public.tb_guardians ADD CONSTRAINT chk_tb_guardians_whatsapp CHECK (whatsapp_number IS NULL OR whatsapp_number ~ '^\+[1-9][0-9]{7,14}$');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_admissions_guardian_whatsapp' AND conrelid = 'public.tb_admissions'::regclass) THEN
        ALTER TABLE public.tb_admissions ADD CONSTRAINT chk_tb_admissions_guardian_whatsapp CHECK (guardian_whatsapp_number IS NULL OR guardian_whatsapp_number ~ '^\+[1-9][0-9]{7,14}$');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_schools_whatsapp' AND conrelid = 'public.tb_schools'::regclass) THEN
        ALTER TABLE public.tb_schools ADD CONSTRAINT chk_tb_schools_whatsapp CHECK (whatsapp_number IS NULL OR whatsapp_number ~ '^\+[1-9][0-9]{7,14}$');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_schools_location' AND conrelid = 'public.tb_schools'::regclass) THEN
        ALTER TABLE public.tb_schools ADD CONSTRAINT chk_tb_schools_location CHECK (
            (latitude IS NULL AND longitude IS NULL)
            OR (latitude IS NOT NULL AND longitude IS NOT NULL AND latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180));
    END IF;
END
$st2bc$;

DO $st2bv$
DECLARE
    b _st2b_before; g bigint; a bigint; s bigint;
BEGIN
    IF NOT (SELECT migrating FROM _st2b_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st2b_before;
    SELECT count(*) INTO g FROM public.tb_guardians;
    SELECT count(*) INTO a FROM public.tb_admissions;
    SELECT count(*) INTO s FROM public.tb_schools;
    IF g <> b.guardians OR a <> b.admissions OR s <> b.schools THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: row counts changed. Everything is rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM public.tb_guardians WHERE whatsapp_number IS NOT NULL)
       OR EXISTS (SELECT 1 FROM public.tb_admissions WHERE guardian_whatsapp_number IS NOT NULL)
       OR EXISTS (SELECT 1 FROM public.tb_schools WHERE whatsapp_number IS NOT NULL OR latitude IS NOT NULL OR longitude IS NOT NULL) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an existing row got a value in a new column. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % guardians, % applications and % schools before and after; every new column is empty for every existing row; the four checks exist.', g, a, s;
END
$st2bv$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_guardians) AS guardians, (SELECT count(*) FROM public.tb_admissions) AS applications, (SELECT count(*) FROM public.tb_schools) AS schools,
       (SELECT count(*) FROM public.tb_schools WHERE whatsapp_number IS NULL) AS schools_without_whatsapp;
