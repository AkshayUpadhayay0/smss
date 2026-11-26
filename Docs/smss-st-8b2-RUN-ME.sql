-- =====================================================================================================================
-- RUN-ME: Transfer certificates follow-up, stage 8b2 = THREE OPTIONAL SCHOOL FIELDS  (one self-contained file for pgAdmin's Query Tool; single transaction)
--
-- WHAT IT DOES (additive only; no existing row is changed)
--   tb_schools gets three NULLABLE columns, NULL on every existing school:
--     affiliation_number  varchar(50)   the board's affiliation number
--     udise_code          varchar(20)   the UDISE code
--     board_school_code   varchar(30)   the board's own school code
--   They are edited on the School Profile (School Admin) and on the school form (Super Admin), and printed on a transfer certificate only when filled in.
--
-- PRE-CHECK (violators): none possible (new nullable columns, no constraint). The script prints how many schools exist and verifies that every pre-existing column of every school is identical afterwards (fingerprint).
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-8b2.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   DEPLOY ORDER: before the API build that contains this follow-up (the school entity maps the columns).
--
-- RE-RUNNING IT: safe; a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
-- HOW TO ROLL BACK: ALTER TABLE tb_schools DROP COLUMN board_school_code, DROP COLUMN udise_code, DROP COLUMN affiliation_number;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st8b2_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st8b2_before (schools bigint, fingerprint text) ON COMMIT DROP;

DO $st8b2$
DECLARE
    v_mig boolean;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';
    IF to_regclass('public.tb_schools') IS NULL THEN RAISE EXCEPTION 'tb_schools is missing. Nothing was changed.'; END IF;

    v_mig := NOT ((SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_schools'
                      AND column_name IN ('affiliation_number', 'udise_code', 'board_school_code')) = 3);
    INSERT INTO _st8b2_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    INSERT INTO _st8b2_before
    SELECT count(*), coalesce(md5(string_agg(concat_ws('|', s.school_id, s.school_code, s.school_name, s.school_short_name, s.school_type_id, s.school_level_id, s.board_type_id, s.school_establish_year,
                                                       s.school_gstin, s.school_pan, s.address_line1, s.address_line2, s.pincode, s.email, s.mobile_number, s.website, s.logo_url, s.school_status_id,
                                                       s.created_at, s.updated_at, s.school_short_code, s.show_on_website, s.whatsapp_number), E'\n' ORDER BY s.school_id)), '')
      FROM public.tb_schools s;
    RAISE NOTICE 'PRE-CHECK: % schools exist; no violators are possible (the new columns are NULL and carry no constraint).', (SELECT schools FROM _st8b2_before);
END
$st8b2$;

ALTER TABLE public.tb_schools ADD COLUMN IF NOT EXISTS affiliation_number varchar(50) NULL;
ALTER TABLE public.tb_schools ADD COLUMN IF NOT EXISTS udise_code         varchar(20) NULL;
ALTER TABLE public.tb_schools ADD COLUMN IF NOT EXISTS board_school_code  varchar(30) NULL;

DO $st8b2v$
DECLARE
    b _st8b2_before;
    f text;
BEGIN
    IF NOT (SELECT migrating FROM _st8b2_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st8b2_before;
    SELECT coalesce(md5(string_agg(concat_ws('|', s.school_id, s.school_code, s.school_name, s.school_short_name, s.school_type_id, s.school_level_id, s.board_type_id, s.school_establish_year,
                                             s.school_gstin, s.school_pan, s.address_line1, s.address_line2, s.pincode, s.email, s.mobile_number, s.website, s.logo_url, s.school_status_id,
                                             s.created_at, s.updated_at, s.school_short_code, s.show_on_website, s.whatsapp_number), E'\n' ORDER BY s.school_id)), '')
      INTO f FROM public.tb_schools s;
    IF (SELECT count(*) FROM public.tb_schools) <> b.schools OR f <> b.fingerprint THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a school changed. Everything is rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM public.tb_schools WHERE affiliation_number IS NOT NULL OR udise_code IS NOT NULL OR board_school_code IS NOT NULL) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a new column holds a value after migrating. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % schools unchanged (fingerprint identical); 3 new columns NULL everywhere.', b.schools;
END
$st8b2v$;

COMMIT;

SELECT count(*) AS schools_with_affiliation FROM public.tb_schools WHERE affiliation_number IS NOT NULL;
