-- =====================================================================================================================
-- RUN-ME: Guardian foundation, stage 3d = THE MERGE AUDIT NEEDS TWO MORE COLUMNS  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: the Stage 3 design summary approved on 25 Nov 2026 (merge with audit) and the 3d rules of the same day.
--
-- WHY A MIGRATION AT ALL: tb_guardian_merges (made in 3a) has no place for the admin's REASON for a merge and for the applications, links of other guardians and login that the merge repointed.
-- Both are small additive columns. NOTHING IS MERGED BY THIS SCRIPT (a merge is only ever done by an admin through the screen), and no existing row of any table is changed.
--
-- WHAT IT DOES
--   1. tb_guardian_merges.reason      varchar(500) NULL   the admin's optional reason for the merge
--   2. tb_guardian_merges.repointed   jsonb NOT NULL DEFAULT '{}'   what the merge repointed: applications (primary and extra), guardians that pointed at the absorbed one, the login that moved
--   Pre-check: none needed (the table is empty today and the columns are new and nullable / defaulted).
--   Verification (raises, everything rolls back, on any mismatch): the row counts of tb_guardians, tb_student_guardians and tb_guardian_merges are unchanged and both columns exist.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-3d.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   DEPLOY ORDER: run this BEFORE the API build that contains the merge (the build writes these columns).
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   ALTER TABLE tb_guardian_merges DROP COLUMN reason, DROP COLUMN repointed;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st3d_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st3d_before (guardians bigint, links bigint, merges bigint) ON COMMIT DROP;

DO $st3d$
DECLARE
    v_mig boolean;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    IF to_regclass('public.tb_guardian_merges') IS NULL OR to_regclass('public.tb_guardians') IS NULL OR to_regclass('public.tb_student_guardians') IS NULL THEN
        RAISE EXCEPTION 'tb_guardian_merges (stage 3a) is missing. Run the 3a script first. Nothing was changed.';
    END IF;

    v_mig := NOT (
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_guardian_merges' AND column_name = 'reason')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_guardian_merges' AND column_name = 'repointed'));
    INSERT INTO _st3d_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;
    RAISE NOTICE 'No pre-check needed: the columns are new and nullable or defaulted.';

    INSERT INTO _st3d_before SELECT (SELECT count(*) FROM public.tb_guardians), (SELECT count(*) FROM public.tb_student_guardians), (SELECT count(*) FROM public.tb_guardian_merges);
END
$st3d$;

ALTER TABLE public.tb_guardian_merges ADD COLUMN IF NOT EXISTS reason    varchar(500) NULL;
ALTER TABLE public.tb_guardian_merges ADD COLUMN IF NOT EXISTS repointed jsonb        NOT NULL DEFAULT '{}'::jsonb;

DO $st3dv$
DECLARE
    b _st3d_before;
BEGIN
    IF NOT (SELECT migrating FROM _st3d_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st3d_before;
    IF (SELECT count(*) FROM public.tb_guardians) <> b.guardians OR (SELECT count(*) FROM public.tb_student_guardians) <> b.links OR (SELECT count(*) FROM public.tb_guardian_merges) <> b.merges THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a row count changed. Everything is rolled back.';
    END IF;
    IF NOT (EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_guardian_merges' AND column_name = 'reason')
            AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_guardian_merges' AND column_name = 'repointed')) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a column is missing. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % guardians, % links and % merge rows before and after; tb_guardian_merges has reason and repointed.', b.guardians, b.links, b.merges;
END
$st3dv$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_guardian_merges) AS merges;
