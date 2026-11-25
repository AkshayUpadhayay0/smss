-- =====================================================================================================================
-- RUN-ME: Guardian foundation, stage 3a = SCHEMA FOUNDATION  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/guardian-portal-design.md (section 6) and the Stage 3 design summary approved on 25 Nov 2026 (Docs/stage-2-build-log.md wins where they differ).
--
-- THIS STEP CHANGES NO BEHAVIOUR except one rule: a student can have only ONE primary guardian. Everything else is empty structure that the next steps (3b to 3e) use.
-- No existing guardian row is changed: mobile_number is NOT rewritten (decision: matching uses the derived fn_phone_key, not a new format).
--
-- WHAT IT DOES
--   1. public.fn_phone_key(text)  IMMUTABLE: the 10-digit Indian mobile number inside "9876543210", "+91 98765 43210", "919876543210" or "09876543210"; NULL for anything else.
--      Used for matching and duplicate detection. Index: idx_tb_guardians_school_phone_key on tb_guardians (school_id, fn_phone_key(mobile_number)).
--   2. tb_student_guardians.portal_access  boolean NOT NULL DEFAULT true (every existing link becomes true). A child with access off will not show for that guardian.
--      Unique index uq_tb_student_guardians_one_primary: ONE primary guardian per student. PRE-CHECK: a student with two primaries stops the script and is listed.
--   3. tb_guardians.user_id  (one login per guardian, nullable, FK tb_users, unique where set; nothing fills it yet) and tb_guardians.merged_into_guardian_id (set when a duplicate is merged; FK to itself).
--   4. tb_admissions.guardian_id  (the existing guardian an application reuses as its primary one; nullable FK) and the new table tb_admission_guardians (extra existing guardians ticked on an application).
--   5. tb_guardian_changes  (log of changes to a guardian's mobile, WhatsApp and email: who, when, old, new, and whether it was a single edit, a bulk change or a merge).
--   6. tb_guardian_merges   (audit of every merge: who, survivor, absorbed, the links moved, the fields copied, a snapshot of the absorbed row). The merge itself is never run by a migration.
--   7. tb_school_whatsapp_senders  (one row per school: its WhatsApp number, the Meta phone-number id, and the sender status NOT_REGISTERED / PENDING / ACTIVE). One row is created for every existing school
--      (status NOT_REGISTERED, the school's own WhatsApp number when it has one). A school with no row counts as NOT_REGISTERED.
--   Verification (raises, so everything rolls back, on any mismatch): the row counts of tb_guardians, tb_student_guardians and tb_admissions are unchanged, no guardian was changed (a fingerprint of the
--   original guardian columns is equal before and after), every link has portal_access = true, every school has exactly one sender row, the new tables are empty, and every new index and check exists.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-3a.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK (all of it is new structure; nothing was rewritten)
--   DROP TABLE tb_school_whatsapp_senders, tb_guardian_merges, tb_guardian_changes, tb_admission_guardians;
--   ALTER TABLE tb_admissions DROP COLUMN guardian_id;  ALTER TABLE tb_guardians DROP COLUMN user_id, DROP COLUMN merged_into_guardian_id;
--   DROP INDEX uq_tb_student_guardians_one_primary;  ALTER TABLE tb_student_guardians DROP COLUMN portal_access;  DROP INDEX idx_tb_guardians_school_phone_key;  DROP FUNCTION fn_phone_key(text);
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st3a_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st3a_before (guardians bigint, links bigint, admissions bigint, schools bigint, guardian_fp text) ON COMMIT DROP;

DO $st3a$
DECLARE
    v_mig boolean;
    v_bad text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    IF to_regclass('public.tb_guardians') IS NULL OR to_regclass('public.tb_student_guardians') IS NULL OR to_regclass('public.tb_admissions') IS NULL
       OR to_regclass('public.tb_schools') IS NULL OR to_regclass('public.tb_users') IS NULL THEN
        RAISE EXCEPTION 'A required table (tb_guardians, tb_student_guardians, tb_admissions, tb_schools or tb_users) does not exist. Nothing was changed.';
    END IF;

    v_mig := NOT (
        to_regprocedure('public.fn_phone_key(text)') IS NOT NULL
        AND to_regclass('public.idx_tb_guardians_school_phone_key') IS NOT NULL
        AND to_regclass('public.uq_tb_student_guardians_one_primary') IS NOT NULL
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_guardians' AND column_name = 'portal_access')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_guardians' AND column_name = 'user_id')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_guardians' AND column_name = 'merged_into_guardian_id')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_admissions' AND column_name = 'guardian_id')
        AND to_regclass('public.tb_admission_guardians') IS NOT NULL
        AND to_regclass('public.tb_guardian_changes') IS NOT NULL
        AND to_regclass('public.tb_guardian_merges') IS NOT NULL
        AND to_regclass('public.tb_school_whatsapp_senders') IS NOT NULL);
    IF NOT v_mig THEN
        -- everything exists: it is only "already migrated" when every school also has its sender row (dynamic SQL: the table may not exist yet on a first run)
        EXECUTE 'SELECT NOT EXISTS (SELECT 1 FROM public.tb_schools s WHERE NOT EXISTS (SELECT 1 FROM public.tb_school_whatsapp_senders w WHERE w.school_id = s.school_id))' INTO v_mig;
        v_mig := NOT v_mig;
    END IF;
    INSERT INTO _st3a_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    -- PRE-CHECK: one primary guardian per student. Any student with two or more primaries is listed and nothing is changed.
    SELECT string_agg(student_id || ' (' || n || ' primaries)', ', ' ORDER BY student_id) INTO v_bad
      FROM (SELECT student_id, count(*) n FROM public.tb_student_guardians WHERE is_primary GROUP BY student_id HAVING count(*) > 1) x;
    IF v_bad IS NOT NULL THEN
        RAISE EXCEPTION 'PRE-CHECK FAILED: these students have more than one primary guardian, so the one-primary rule cannot be added: %. Nothing was changed. Fix them (keep one primary) and run again.', v_bad;
    END IF;
    RAISE NOTICE 'Pre-check passed: no student has more than one primary guardian.';

    INSERT INTO _st3a_before
    SELECT (SELECT count(*) FROM public.tb_guardians), (SELECT count(*) FROM public.tb_student_guardians), (SELECT count(*) FROM public.tb_admissions), (SELECT count(*) FROM public.tb_schools),
           (SELECT md5(coalesce(string_agg(row(guardian_id, school_id, first_name, last_name, relationship, mobile_number, email, whatsapp_number, occupation, status_id)::text, '|' ORDER BY guardian_id), ''))
              FROM public.tb_guardians);
END
$st3a$;

-- 1. the derived phone key (an Indian mobile number in any of the usual spellings -> its 10 digits; anything else -> NULL)
CREATE OR REPLACE FUNCTION public.fn_phone_key(p text) RETURNS text
LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS
$fn$
    SELECT CASE
             WHEN d ~ '^[6-9][0-9]{9}$'    THEN d
             WHEN d ~ '^91[6-9][0-9]{9}$'  THEN substr(d, 3)
             WHEN d ~ '^0[6-9][0-9]{9}$'   THEN substr(d, 2)
             ELSE NULL
           END
      FROM (SELECT regexp_replace(p, '[^0-9]', '', 'g') AS d) x
$fn$;

DO $st3am$
BEGIN
    IF NOT (SELECT migrating FROM _st3a_state) THEN RETURN; END IF;

    CREATE INDEX IF NOT EXISTS idx_tb_guardians_school_phone_key ON public.tb_guardians (school_id, public.fn_phone_key(mobile_number));

    -- 2. portal access per link, and one primary guardian per student
    ALTER TABLE public.tb_student_guardians ADD COLUMN IF NOT EXISTS portal_access boolean NOT NULL DEFAULT true;
    CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_student_guardians_one_primary ON public.tb_student_guardians (student_id) WHERE is_primary;

    -- 3. the login link and the merge pointer on the guardian
    ALTER TABLE public.tb_guardians ADD COLUMN IF NOT EXISTS user_id bigint NULL;
    ALTER TABLE public.tb_guardians ADD COLUMN IF NOT EXISTS merged_into_guardian_id bigint NULL;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_guardians_user' AND conrelid = 'public.tb_guardians'::regclass) THEN
        ALTER TABLE public.tb_guardians ADD CONSTRAINT fk_tb_guardians_user FOREIGN KEY (user_id) REFERENCES public.tb_users (user_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_guardians_merged_into' AND conrelid = 'public.tb_guardians'::regclass) THEN
        ALTER TABLE public.tb_guardians ADD CONSTRAINT fk_tb_guardians_merged_into FOREIGN KEY (merged_into_guardian_id) REFERENCES public.tb_guardians (guardian_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_guardians_merged_not_self' AND conrelid = 'public.tb_guardians'::regclass) THEN
        ALTER TABLE public.tb_guardians ADD CONSTRAINT chk_tb_guardians_merged_not_self CHECK (merged_into_guardian_id IS NULL OR merged_into_guardian_id <> guardian_id);
    END IF;
    CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_guardians_user ON public.tb_guardians (user_id) WHERE user_id IS NOT NULL;

    -- 4. an application can reuse existing guardians
    ALTER TABLE public.tb_admissions ADD COLUMN IF NOT EXISTS guardian_id bigint NULL;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_admissions_guardian' AND conrelid = 'public.tb_admissions'::regclass) THEN
        ALTER TABLE public.tb_admissions ADD CONSTRAINT fk_tb_admissions_guardian FOREIGN KEY (guardian_id) REFERENCES public.tb_guardians (guardian_id);
    END IF;

    CREATE TABLE IF NOT EXISTS public.tb_admission_guardians (
        admission_id  bigint       NOT NULL,
        guardian_id   bigint       NOT NULL,
        school_id     varchar(100) NOT NULL,
        relationship  varchar(30)  NULL,
        created_at    timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        CONSTRAINT pk_tb_admission_guardians PRIMARY KEY (admission_id, guardian_id),
        CONSTRAINT fk_tb_admission_guardians_admission FOREIGN KEY (admission_id) REFERENCES public.tb_admissions (admission_id),
        CONSTRAINT fk_tb_admission_guardians_guardian  FOREIGN KEY (guardian_id)  REFERENCES public.tb_guardians (guardian_id),
        CONSTRAINT fk_tb_admission_guardians_school    FOREIGN KEY (school_id)    REFERENCES public.tb_schools (school_id)
    );
    CREATE INDEX IF NOT EXISTS idx_tb_admission_guardians_guardian ON public.tb_admission_guardians (guardian_id);

    -- 5. who changed a guardian's contact details
    CREATE TABLE IF NOT EXISTS public.tb_guardian_changes (
        change_id    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        school_id    varchar(100) NOT NULL,
        guardian_id  bigint       NOT NULL,
        field_name   varchar(20)  NOT NULL,
        old_value    varchar(150) NULL,
        new_value    varchar(150) NULL,
        source       varchar(10)  NOT NULL,
        changed_by   bigint       NULL,
        changed_at   timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        CONSTRAINT fk_tb_guardian_changes_school   FOREIGN KEY (school_id)   REFERENCES public.tb_schools (school_id),
        CONSTRAINT fk_tb_guardian_changes_guardian FOREIGN KEY (guardian_id) REFERENCES public.tb_guardians (guardian_id),
        CONSTRAINT fk_tb_guardian_changes_user     FOREIGN KEY (changed_by)  REFERENCES public.tb_users (user_id),
        CONSTRAINT chk_tb_guardian_changes_field   CHECK (field_name IN ('mobile_number', 'whatsapp_number', 'email')),
        CONSTRAINT chk_tb_guardian_changes_source  CHECK (source IN ('EDIT', 'BULK', 'MERGE'))
    );
    CREATE INDEX IF NOT EXISTS idx_tb_guardian_changes_guardian ON public.tb_guardian_changes (school_id, guardian_id, changed_at DESC);

    -- 6. the audit of every merge
    CREATE TABLE IF NOT EXISTS public.tb_guardian_merges (
        merge_id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        school_id          varchar(100) NOT NULL,
        survivor_id        bigint       NOT NULL,
        absorbed_id        bigint       NOT NULL,
        merged_by          bigint       NULL,
        merged_at          timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        moved_links        jsonb        NOT NULL,
        copied_fields      jsonb        NOT NULL,
        absorbed_snapshot  jsonb        NOT NULL,
        CONSTRAINT fk_tb_guardian_merges_school   FOREIGN KEY (school_id)   REFERENCES public.tb_schools (school_id),
        CONSTRAINT fk_tb_guardian_merges_survivor FOREIGN KEY (survivor_id) REFERENCES public.tb_guardians (guardian_id),
        CONSTRAINT fk_tb_guardian_merges_absorbed FOREIGN KEY (absorbed_id) REFERENCES public.tb_guardians (guardian_id),
        CONSTRAINT fk_tb_guardian_merges_user     FOREIGN KEY (merged_by)   REFERENCES public.tb_users (user_id),
        CONSTRAINT chk_tb_guardian_merges_distinct CHECK (survivor_id <> absorbed_id)
    );
    CREATE INDEX IF NOT EXISTS idx_tb_guardian_merges_school ON public.tb_guardian_merges (school_id, merged_at DESC);

    -- 7. the school's WhatsApp sender (status NOT_REGISTERED / PENDING / ACTIVE; a school with no row counts as NOT_REGISTERED)
    CREATE TABLE IF NOT EXISTS public.tb_school_whatsapp_senders (
        sender_id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        school_id        varchar(100) NOT NULL,
        whatsapp_number  varchar(20)  NULL,
        phone_number_id  varchar(50)  NULL,
        status           varchar(15)  NOT NULL DEFAULT 'NOT_REGISTERED',
        verified_at      timestamptz  NULL,
        updated_by       bigint       NULL,
        created_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at       timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        CONSTRAINT uq_tb_school_whatsapp_senders_school UNIQUE (school_id),
        CONSTRAINT fk_tb_school_whatsapp_senders_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
        CONSTRAINT fk_tb_school_whatsapp_senders_user   FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id),
        CONSTRAINT chk_tb_school_whatsapp_senders_status CHECK (status IN ('NOT_REGISTERED', 'PENDING', 'ACTIVE')),
        CONSTRAINT chk_tb_school_whatsapp_senders_number CHECK (whatsapp_number IS NULL OR whatsapp_number ~ '^\+[1-9][0-9]{7,14}$'),
        -- a sender is ACTIVE only with its Meta phone-number id and the time it was verified
        CONSTRAINT chk_tb_school_whatsapp_senders_active CHECK (status <> 'ACTIVE' OR (phone_number_id IS NOT NULL AND verified_at IS NOT NULL))
    );
    INSERT INTO public.tb_school_whatsapp_senders (school_id, whatsapp_number, status)
    SELECT s.school_id, s.whatsapp_number, 'NOT_REGISTERED' FROM public.tb_schools s
     WHERE NOT EXISTS (SELECT 1 FROM public.tb_school_whatsapp_senders w WHERE w.school_id = s.school_id);
END
$st3am$;

DO $st3av$
DECLARE
    b _st3a_before; g bigint; l bigint; a bigint; s bigint; fp text;
BEGIN
    IF NOT (SELECT migrating FROM _st3a_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st3a_before;
    SELECT count(*) INTO g FROM public.tb_guardians;
    SELECT count(*) INTO l FROM public.tb_student_guardians;
    SELECT count(*) INTO a FROM public.tb_admissions;
    SELECT count(*) INTO s FROM public.tb_schools;
    IF g <> b.guardians OR l <> b.links OR a <> b.admissions OR s <> b.schools THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a row count changed. Everything is rolled back.';
    END IF;
    SELECT md5(coalesce(string_agg(row(guardian_id, school_id, first_name, last_name, relationship, mobile_number, email, whatsapp_number, occupation, status_id)::text, '|' ORDER BY guardian_id), ''))
      INTO fp FROM public.tb_guardians;
    IF fp IS DISTINCT FROM b.guardian_fp THEN RAISE EXCEPTION 'VERIFICATION FAILED: a guardian row was changed. Everything is rolled back.'; END IF;
    IF EXISTS (SELECT 1 FROM public.tb_student_guardians WHERE NOT portal_access) THEN RAISE EXCEPTION 'VERIFICATION FAILED: a link has portal_access off. Everything is rolled back.'; END IF;
    IF EXISTS (SELECT 1 FROM public.tb_guardians WHERE user_id IS NOT NULL OR merged_into_guardian_id IS NOT NULL) OR EXISTS (SELECT 1 FROM public.tb_admissions WHERE guardian_id IS NOT NULL) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a new column was filled. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM public.tb_school_whatsapp_senders) <> s OR EXISTS (SELECT 1 FROM public.tb_schools x WHERE NOT EXISTS (SELECT 1 FROM public.tb_school_whatsapp_senders w WHERE w.school_id = x.school_id)) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: every school must have exactly one sender row. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM public.tb_admission_guardians) + (SELECT count(*) FROM public.tb_guardian_changes) + (SELECT count(*) FROM public.tb_guardian_merges) <> 0 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a new audit table is not empty. Everything is rolled back.';
    END IF;
    IF to_regclass('public.uq_tb_student_guardians_one_primary') IS NULL OR to_regclass('public.uq_tb_guardians_user') IS NULL OR to_regclass('public.idx_tb_guardians_school_phone_key') IS NULL THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an index is missing. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % guardians, % links, % applications and % schools before and after; no guardian row changed; every link has portal_access on; % sender rows (NOT_REGISTERED); new tables empty; indexes and checks exist.',
        g, l, a, s, (SELECT count(*) FROM public.tb_school_whatsapp_senders);
END
$st3av$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_guardians) AS guardians, (SELECT count(*) FROM public.tb_student_guardians WHERE is_primary) AS primary_links,
       (SELECT count(*) FROM public.tb_school_whatsapp_senders) AS senders;
