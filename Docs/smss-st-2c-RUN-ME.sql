-- =====================================================================================================================
-- RUN-ME: Student and admission changes, stage 2c = ADMISSION IN TWO STEPS  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/admission-docs-latefee-registration-design.md (section 2), decided 24 Nov 2026.
--
-- THE NEW FLOW
--   Application ADDED (status code APPLICATION_SUBMITTED) -> Application CONFIRMED (status code ADMITTED, creates the student). Reject and Withdraw are exits, not steps.
--   The status CODES do not change (only the labels shown to people do). UNDER_REVIEW and APPROVED are no longer used by the application; their lut_status rows stay.
--
-- WHAT IT DOES
--   1. tb_admissions.confirmed_by  bigint (FK tb_users)   WHO confirmed the application (the user that created the student). NULL until confirmed.
--   2. tb_admissions.confirmed_at  timestamptz            WHEN it was confirmed.
--   3. Backfill: every application that is already ADMITTED gets confirmed_by = approved_by and confirmed_at = approved_at (the only actor and time the old flow recorded).
--   4. Every application still UNDER_REVIEW or APPROVED moves to APPLICATION_SUBMITTED (it is simply "added" now). reviewed_by / reviewed_at / review_notes / approved_by /
--      approved_at are NOT touched: the history stays.
--   5. Check chk_tb_admissions_confirmed: an ADMITTED application always records who confirmed it and when. ("ADMITTED implies a linked student" and "reject needs a reason" stay.)
--   Pre-check (raises, nothing is changed): an ADMITTED application without approved_by / approved_at could not be backfilled; the script lists them and stops.
--   Verification (raises, so everything rolls back, on any mismatch): the row count is identical; the number of ADMITTED, REJECTED and CANCELLED applications is identical;
--   APPLICATION_SUBMITTED after = APPLICATION_SUBMITTED before + UNDER_REVIEW before + APPROVED before; no application is UNDER_REVIEW or APPROVED; every ADMITTED row has its
--   confirmer copied from its approver; no reviewer/approver value changed.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-2c.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK (the moved applications cannot be told apart afterwards: restore the dump if you need them back under review)
--   ALTER TABLE tb_admissions DROP CONSTRAINT chk_tb_admissions_confirmed;  ALTER TABLE tb_admissions DROP COLUMN confirmed_by, DROP COLUMN confirmed_at;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st2c_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st2c_before (total bigint, submitted bigint, under_review bigint, approved bigint, admitted bigint, rejected bigint, cancelled bigint,
                                reviewed_by_set bigint, approved_by_set bigint) ON COMMIT DROP;

DO $st2c$
DECLARE
    v_mig boolean;
    v_sub int; v_rev int; v_app int; v_adm int; v_rej int; v_can int;
    v_bad text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    IF to_regclass('public.tb_admissions') IS NULL THEN
        RAISE EXCEPTION 'Table tb_admissions does not exist. Nothing was changed.';
    END IF;

    SELECT sid INTO v_sub FROM public.lut_status WHERE sname = 'APPLICATION_SUBMITTED' AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_rev FROM public.lut_status WHERE sname = 'UNDER_REVIEW'          AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_app FROM public.lut_status WHERE sname = 'APPROVED'              AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_adm FROM public.lut_status WHERE sname = 'ADMITTED'              AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_rej FROM public.lut_status WHERE sname = 'REJECTED'              AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_can FROM public.lut_status WHERE sname = 'CANCELLED'             AND stype = 'ADMISSION_STATUS';
    IF v_sub IS NULL OR v_rev IS NULL OR v_app IS NULL OR v_adm IS NULL OR v_rej IS NULL OR v_can IS NULL THEN
        RAISE EXCEPTION 'lut_status is missing an ADMISSION_STATUS row. Nothing was changed.';
    END IF;

    v_mig := NOT (
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_admissions' AND column_name = 'confirmed_by')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_admissions' AND column_name = 'confirmed_at')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_admissions_confirmed' AND conrelid = 'public.tb_admissions'::regclass)
        AND NOT EXISTS (SELECT 1 FROM public.tb_admissions WHERE status_id IN (v_rev, v_app)));
    INSERT INTO _st2c_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    -- pre-check: an ADMITTED application must have the approver columns the backfill copies
    SELECT string_agg(application_number, ', ' ORDER BY admission_id) INTO v_bad
      FROM public.tb_admissions WHERE status_id = v_adm AND (approved_by IS NULL OR approved_at IS NULL);
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'PRE-CHECK FAILED: these ADMITTED applications have no approver (approved_by / approved_at), so who confirmed them cannot be backfilled: %. Nothing was changed.', v_bad;
    END IF;

    INSERT INTO _st2c_before
    SELECT count(*), count(*) FILTER (WHERE status_id = v_sub), count(*) FILTER (WHERE status_id = v_rev), count(*) FILTER (WHERE status_id = v_app),
           count(*) FILTER (WHERE status_id = v_adm), count(*) FILTER (WHERE status_id = v_rej), count(*) FILTER (WHERE status_id = v_can),
           count(*) FILTER (WHERE reviewed_by IS NOT NULL), count(*) FILTER (WHERE approved_by IS NOT NULL)
      FROM public.tb_admissions;
    RAISE NOTICE 'To move: % UNDER_REVIEW and % APPROVED application(s) -> APPLICATION_SUBMITTED; to backfill: % ADMITTED application(s).',
        (SELECT under_review FROM _st2c_before), (SELECT approved FROM _st2c_before), (SELECT admitted FROM _st2c_before);
END
$st2c$;

ALTER TABLE public.tb_admissions ADD COLUMN IF NOT EXISTS confirmed_by bigint      NULL;
ALTER TABLE public.tb_admissions ADD COLUMN IF NOT EXISTS confirmed_at timestamptz NULL;

DO $st2cm$
DECLARE
    v_sub int; v_rev int; v_app int; v_adm int;
BEGIN
    IF NOT (SELECT migrating FROM _st2c_state) THEN RETURN; END IF;
    SELECT sid INTO v_sub FROM public.lut_status WHERE sname = 'APPLICATION_SUBMITTED' AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_rev FROM public.lut_status WHERE sname = 'UNDER_REVIEW'          AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_app FROM public.lut_status WHERE sname = 'APPROVED'              AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_adm FROM public.lut_status WHERE sname = 'ADMITTED'              AND stype = 'ADMISSION_STATUS';

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_admissions_confirmed_by' AND conrelid = 'public.tb_admissions'::regclass) THEN
        ALTER TABLE public.tb_admissions ADD CONSTRAINT fk_tb_admissions_confirmed_by FOREIGN KEY (confirmed_by) REFERENCES public.tb_users (user_id);
    END IF;

    -- 3. who confirmed the already ADMITTED applications: the only actor the old flow recorded
    UPDATE public.tb_admissions SET confirmed_by = approved_by, confirmed_at = approved_at
     WHERE status_id = v_adm AND confirmed_by IS NULL;

    -- 4. an application under review or approved is simply "added" now (reviewer / approver columns stay as history)
    UPDATE public.tb_admissions SET status_id = v_sub WHERE status_id IN (v_rev, v_app);

    -- 5. every confirmation records who and when
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_admissions_confirmed' AND conrelid = 'public.tb_admissions'::regclass) THEN
        -- a CHECK cannot look the status up by name, so the id resolved above is written into it (the same way chk_tb_admissions_converted is written)
        EXECUTE format('ALTER TABLE public.tb_admissions ADD CONSTRAINT chk_tb_admissions_confirmed CHECK (status_id <> %s OR (confirmed_by IS NOT NULL AND confirmed_at IS NOT NULL))', v_adm);
    END IF;
END
$st2cm$;

DO $st2cv$
DECLARE
    b _st2c_before;
    v_sub int; v_rev int; v_app int; v_adm int; v_rej int; v_can int;
    n_total bigint; n_sub bigint; n_adm bigint; n_rej bigint; n_can bigint; n_rb bigint; n_ab bigint;
BEGIN
    IF NOT (SELECT migrating FROM _st2c_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st2c_before;
    SELECT sid INTO v_sub FROM public.lut_status WHERE sname = 'APPLICATION_SUBMITTED' AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_rev FROM public.lut_status WHERE sname = 'UNDER_REVIEW'          AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_app FROM public.lut_status WHERE sname = 'APPROVED'              AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_adm FROM public.lut_status WHERE sname = 'ADMITTED'              AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_rej FROM public.lut_status WHERE sname = 'REJECTED'              AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO v_can FROM public.lut_status WHERE sname = 'CANCELLED'             AND stype = 'ADMISSION_STATUS';

    SELECT count(*), count(*) FILTER (WHERE status_id = v_sub), count(*) FILTER (WHERE status_id = v_adm), count(*) FILTER (WHERE status_id = v_rej),
           count(*) FILTER (WHERE status_id = v_can), count(*) FILTER (WHERE reviewed_by IS NOT NULL), count(*) FILTER (WHERE approved_by IS NOT NULL)
      INTO n_total, n_sub, n_adm, n_rej, n_can, n_rb, n_ab FROM public.tb_admissions;

    IF n_total <> b.total THEN RAISE EXCEPTION 'VERIFICATION FAILED: the number of applications changed. Everything is rolled back.'; END IF;
    IF n_adm <> b.admitted OR n_rej <> b.rejected OR n_can <> b.cancelled THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: the number of ADMITTED / REJECTED / CANCELLED applications changed. Everything is rolled back.';
    END IF;
    IF n_sub <> b.submitted + b.under_review + b.approved THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: APPLICATION_SUBMITTED is % but should be % + % + %. Everything is rolled back.', n_sub, b.submitted, b.under_review, b.approved;
    END IF;
    IF EXISTS (SELECT 1 FROM public.tb_admissions WHERE status_id IN (v_rev, v_app)) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an application is still UNDER_REVIEW or APPROVED. Everything is rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM public.tb_admissions WHERE status_id = v_adm AND (confirmed_by IS DISTINCT FROM approved_by OR confirmed_at IS DISTINCT FROM approved_at)) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an ADMITTED application did not get its confirmer copied from its approver. Everything is rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM public.tb_admissions WHERE status_id <> v_adm AND (confirmed_by IS NOT NULL OR confirmed_at IS NOT NULL)) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a not-confirmed application got a confirmer. Everything is rolled back.';
    END IF;
    IF n_rb <> b.reviewed_by_set OR n_ab <> b.approved_by_set THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a reviewer / approver value changed. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % applications before and after; % moved from UNDER_REVIEW and % from APPROVED to APPLICATION_SUBMITTED (now %); % ADMITTED applications got their confirmer from the approver; reviewer and approver history untouched.',
        n_total, b.under_review, b.approved, n_sub, n_adm;
END
$st2cv$;

COMMIT;

SELECT s.sname AS status, count(a.admission_id) AS applications
  FROM public.lut_status s LEFT JOIN public.tb_admissions a ON a.status_id = s.sid
 WHERE s.stype = 'ADMISSION_STATUS' GROUP BY s.sid, s.sname ORDER BY s.sid;
