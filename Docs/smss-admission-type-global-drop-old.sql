-- Do not run this yet. The pgAdmin-ready file for step 1 is Docs/smss-admission-type-global-RUN-ME.sql.
-- =====================================================================================================================
-- Admission Type global master - STEP 2 of 2: drop the old school-level table tb_admission_types.
-- Run ONLY after Docs/smss-admission-type-global.sql has been applied, the new API has been live, and the Admission form + student pages
-- were checked. Take a backup first: after this step the only way back is that backup.
--
-- It refuses to run unless step 1 finished cleanly:
--   * every row of the old table is in mig_admission_type_map and was repointed;
--   * no row of tb_admissions / tb_students / tb_student_enrollments points at an id missing from lut_admission_type;
--   * nothing else still references tb_admission_types (any FK from another table makes the drop fail on purpose - no CASCADE).
-- mig_admission_type_map is KEPT (it is the record of old id -> new id); drop it yourself when you no longer want it.
--
-- ROLLBACK: restore the backup taken before this step (the table is gone).   psql -v ON_ERROR_STOP=1 -f smss-admission-type-global-drop-old.sql
-- =====================================================================================================================

BEGIN;

DO $drop$
DECLARE
    v_bad bigint;
    t     text;
BEGIN
    IF to_regclass('public.tb_admission_types') IS NULL THEN
        RAISE NOTICE 'tb_admission_types is already dropped: nothing to do.';
        RETURN;
    END IF;
    IF to_regclass('public.lut_admission_type') IS NULL OR to_regclass('public.mig_admission_type_map') IS NULL THEN
        RAISE EXCEPTION 'Step 1 (smss-admission-type-global.sql) has not been applied. Refusing to drop.';
    END IF;

    SELECT count(*) INTO v_bad FROM public.tb_admission_types o
    WHERE NOT EXISTS (SELECT 1 FROM public.mig_admission_type_map m WHERE m.school_id = o.school_id AND m.old_id = o.admission_type_id AND m.repointed_at IS NOT NULL);
    IF v_bad > 0 THEN RAISE EXCEPTION '% old admission type row(s) were never mapped/repointed. Refusing to drop.', v_bad; END IF;

    FOREACH t IN ARRAY ARRAY['tb_admissions', 'tb_students', 'tb_student_enrollments'] LOOP
        EXECUTE format('SELECT count(*) FROM public.%I x WHERE NOT EXISTS (SELECT 1 FROM public.lut_admission_type l WHERE l.admission_type_id = x.admission_type_id)', t) INTO v_bad;
        IF v_bad > 0 THEN RAISE EXCEPTION '% orphan row(s) in %. Refusing to drop.', v_bad, t; END IF;
    END LOOP;

    DROP TABLE public.tb_admission_types;   -- no CASCADE: a stray dependency must stop this, not be silently removed
    RAISE NOTICE 'tb_admission_types dropped.';
END
$drop$;

COMMIT;
