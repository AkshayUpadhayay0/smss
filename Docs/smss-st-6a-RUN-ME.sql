-- =====================================================================================================================
-- RUN-ME: Parent accounts and messaging, stage 6a = ALL OF THE SCHEMA  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/guardian-portal-design.md as changed by the Stage 6-7 decisions approved on 26 Nov 2026 (one-time set-password link, hybrid sender, generated guardian login id only, consent on record).
--
-- WHY A MIGRATION: a parent account is set up by a ONE-TIME LINK (no password is ever sent), the message that carries it is logged (and can be resent), the parent's consent is on record, and the generated guardian
-- login id needs a lock-safe counter. NOTHING IS CREATED OR SENT BY THIS SCRIPT: no account, no token, no message. Every existing row keeps its exact values.
--
-- WHAT IT DOES
--   1. tb_user_action_tokens        one row per issued link: purpose SET_PASSWORD / RESET_PASSWORD, the SHA-256 HASH of the token (the token itself is never stored), expires_at, used_at, revoked_at, who issued it.
--                                   A hash is unique, so one link can never be two rows. Single use = used_at set once; a newer link for the same user and purpose revokes the older ones (done by the API).
--   2. tb_message_deliveries        the delivery log: one row per message attempt (school, guardian / user, template code, channel WHATSAPP or EMAIL, status, provider message id, attempts, a short sanitised error,
--                                   sender kind LOG / CLOUD / SMTP and scope PLATFORM / SCHOOL). It holds NO token, link, password or message text; the recipient is stored masked. Check constraints keep the shape honest.
--   3. tb_guardian_login_counters   one row per school: the last guardian login number handed out (the id is school short code + GD + number). The API takes it under a lock.
--   4. uq_tb_users_guardian_login   a unique index on lower(org_user_id) for Guardian users: a guardian login id is unique across the whole platform, case-insensitively.
--   5. tb_guardians                 consent_given_at, consent_recorded_by, consent_source (ADMISSION_FORM / BULK_CONFIRMED / MANUAL). All NULL for every existing guardian.
--      tb_admissions                guardian_consent_at, guardian_consent_recorded_by: the consent tick on the admission form, copied to the guardian when the application is confirmed. NULL for every existing application.
--
-- PRE-CHECK (violators): the new unique index needs no two Guardian users with the same lower(org_user_id) (today there are no Guardian users at all); the script counts them and stops if any. Nothing else
--   can be violated: every other change adds a nullable column or a new empty table.
--   Verification (raises, everything rolls back, on any mismatch): the guardian, admission and user row counts and a fingerprint of every pre-existing column of tb_guardians / tb_admissions / tb_users are identical
--   before and after; every new column is NULL on every existing row; the three new tables are empty; every constraint and index exists.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-6a.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   DEPLOY ORDER: run this BEFORE the API build that contains the parent-account foundations (the build maps these tables).
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK (only while no link has been issued and no message logged)
--   DROP TABLE tb_message_deliveries; DROP TABLE tb_user_action_tokens; DROP TABLE tb_guardian_login_counters; DROP INDEX uq_tb_users_guardian_login;
--   ALTER TABLE tb_guardians DROP COLUMN consent_given_at, DROP COLUMN consent_recorded_by, DROP COLUMN consent_source;
--   ALTER TABLE tb_admissions DROP COLUMN guardian_consent_at, DROP COLUMN guardian_consent_recorded_by;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st6a_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st6a_before (guardians bigint, admissions bigint, users bigint, guardian_fp text, admission_fp text, user_fp text) ON COMMIT DROP;

DO $st6a$
DECLARE
    v_mig boolean;
    v_bad bigint;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    IF to_regclass('public.tb_guardians') IS NULL OR to_regclass('public.tb_admissions') IS NULL OR to_regclass('public.tb_users') IS NULL OR to_regclass('public.tb_schools') IS NULL THEN
        RAISE EXCEPTION 'A table the parent accounts hang on is missing (guardians, admissions, users or schools). Nothing was changed.';
    END IF;

    v_mig := NOT (
        to_regclass('public.tb_user_action_tokens') IS NOT NULL AND to_regclass('public.tb_message_deliveries') IS NOT NULL AND to_regclass('public.tb_guardian_login_counters') IS NOT NULL
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_guardians' AND column_name = 'consent_given_at')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_guardians' AND column_name = 'consent_recorded_by')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_guardians' AND column_name = 'consent_source')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_admissions' AND column_name = 'guardian_consent_at')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_admissions' AND column_name = 'guardian_consent_recorded_by')
        AND EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'uq_tb_users_guardian_login'));
    INSERT INTO _st6a_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    SELECT count(*) INTO v_bad FROM (
        SELECT lower(org_user_id) FROM public.tb_users WHERE user_type = 'Guardian' AND org_user_id IS NOT NULL GROUP BY 1 HAVING count(*) > 1) d;
    IF v_bad <> 0 THEN
        RAISE EXCEPTION 'PRE-CHECK FAILED: % guardian login ids are not unique ignoring case. Nothing was changed.', v_bad;
    END IF;

    INSERT INTO _st6a_before
    SELECT (SELECT count(*) FROM public.tb_guardians), (SELECT count(*) FROM public.tb_admissions), (SELECT count(*) FROM public.tb_users),
           (SELECT coalesce(md5(string_agg(to_jsonb(g)::text, E'\n' ORDER BY guardian_id)), '') FROM public.tb_guardians g),
           (SELECT coalesce(md5(string_agg(to_jsonb(a)::text, E'\n' ORDER BY a.admission_id)), '') FROM public.tb_admissions a),
           (SELECT coalesce(md5(string_agg(to_jsonb(u)::text, E'\n' ORDER BY user_id)), '') FROM public.tb_users u);
    RAISE NOTICE 'PRE-CHECK: 0 violators (% Guardian users today). % guardians, % admissions and % users will keep every value.',
        (SELECT count(*) FROM public.tb_users WHERE user_type = 'Guardian'), (SELECT guardians FROM _st6a_before), (SELECT admissions FROM _st6a_before), (SELECT users FROM _st6a_before);
END
$st6a$;

-- ------------------------------------------------------------------------------------------------ 5. consent (nullable: nobody has given it yet as far as the database knows)
ALTER TABLE public.tb_guardians  ADD COLUMN IF NOT EXISTS consent_given_at    timestamptz NULL;
ALTER TABLE public.tb_guardians  ADD COLUMN IF NOT EXISTS consent_recorded_by bigint      NULL;
ALTER TABLE public.tb_guardians  ADD COLUMN IF NOT EXISTS consent_source      varchar(20) NULL;
ALTER TABLE public.tb_admissions ADD COLUMN IF NOT EXISTS guardian_consent_at          timestamptz NULL;
ALTER TABLE public.tb_admissions ADD COLUMN IF NOT EXISTS guardian_consent_recorded_by bigint      NULL;

DO $st6ac$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_guardians_consent_user') THEN
        ALTER TABLE public.tb_guardians ADD CONSTRAINT fk_tb_guardians_consent_user FOREIGN KEY (consent_recorded_by) REFERENCES public.tb_users (user_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_guardians_consent') THEN
        -- consent is all or nothing: the date, who recorded it and how, or none of them
        ALTER TABLE public.tb_guardians ADD CONSTRAINT chk_tb_guardians_consent CHECK (
            (consent_given_at IS NULL AND consent_recorded_by IS NULL AND consent_source IS NULL)
            OR (consent_given_at IS NOT NULL AND consent_recorded_by IS NOT NULL AND consent_source IN ('ADMISSION_FORM', 'BULK_CONFIRMED', 'MANUAL')));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_admissions_consent_user') THEN
        ALTER TABLE public.tb_admissions ADD CONSTRAINT fk_tb_admissions_consent_user FOREIGN KEY (guardian_consent_recorded_by) REFERENCES public.tb_users (user_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_admissions_consent') THEN
        ALTER TABLE public.tb_admissions ADD CONSTRAINT chk_tb_admissions_consent CHECK ((guardian_consent_at IS NULL) = (guardian_consent_recorded_by IS NULL));
    END IF;
END
$st6ac$;

-- ------------------------------------------------------------------------------------------------ 1. one-time links (only the hash of the token is stored)
CREATE TABLE IF NOT EXISTS public.tb_user_action_tokens (
    token_id      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id       bigint       NOT NULL,
    school_id     varchar(100) NULL,
    purpose       varchar(20)  NOT NULL,
    token_hash    char(64)     NOT NULL,                    -- lower-case hex SHA-256 of the token; the token itself is never stored
    created_at    timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at    timestamptz  NOT NULL,
    used_at       timestamptz  NULL,
    revoked_at    timestamptz  NULL,
    created_by    bigint       NULL,                        -- who asked for the link (an admin / teacher); NULL when the parent asked for it
    CONSTRAINT uq_tb_user_action_tokens_hash UNIQUE (token_hash),
    CONSTRAINT fk_tb_user_action_tokens_user    FOREIGN KEY (user_id)    REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_user_action_tokens_school  FOREIGN KEY (school_id)  REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_user_action_tokens_creator FOREIGN KEY (created_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_user_action_tokens_purpose CHECK (purpose IN ('SET_PASSWORD', 'RESET_PASSWORD')),
    CONSTRAINT chk_tb_user_action_tokens_hash    CHECK (token_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT chk_tb_user_action_tokens_expiry  CHECK (expires_at > created_at),
    CONSTRAINT chk_tb_user_action_tokens_one_end CHECK (used_at IS NULL OR revoked_at IS NULL)   -- a link is either used or revoked, never both
);
CREATE INDEX IF NOT EXISTS idx_tb_user_action_tokens_user ON public.tb_user_action_tokens (user_id, purpose, created_at DESC);

-- ------------------------------------------------------------------------------------------------ 2. the delivery log (never a token, link, password or message text)
CREATE TABLE IF NOT EXISTS public.tb_message_deliveries (
    delivery_id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    school_id            varchar(100) NOT NULL,
    guardian_id          bigint       NULL,
    user_id              bigint       NULL,
    template_code        varchar(50)  NOT NULL,             -- parent_account_ready, parent_child_added, parent_login_code, parent_student_login_ready, ...
    purpose              varchar(30)  NOT NULL,             -- SET_PASSWORD, RESET_PASSWORD, CHILD_ADDED, STUDENT_LOGIN, ... (what the resend limit counts)
    channel              varchar(10)  NOT NULL,
    recipient_masked     varchar(60)  NULL,                 -- e.g. +91******3210 / a***@gmail.com
    status               varchar(10)  NOT NULL,
    sender_kind          varchar(10)  NOT NULL,             -- LOG (nothing was sent), CLOUD (Meta), SMTP (email)
    sender_scope         varchar(10)  NULL,                 -- PLATFORM or SCHOOL (WhatsApp only)
    provider_message_id  varchar(200) NULL,
    attempts             integer      NOT NULL DEFAULT 1,
    error_code           varchar(40)  NULL,
    error_message        varchar(300) NULL,                 -- short and sanitised; never the provider's raw response
    resend_of_delivery_id bigint      NULL,
    created_by           bigint       NULL,
    created_at           timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    sent_at              timestamptz  NULL,
    delivered_at         timestamptz  NULL,
    CONSTRAINT fk_tb_message_deliveries_school   FOREIGN KEY (school_id)   REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_message_deliveries_guardian FOREIGN KEY (guardian_id) REFERENCES public.tb_guardians (guardian_id),
    CONSTRAINT fk_tb_message_deliveries_user     FOREIGN KEY (user_id)     REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_message_deliveries_creator  FOREIGN KEY (created_by)  REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_message_deliveries_resend   FOREIGN KEY (resend_of_delivery_id) REFERENCES public.tb_message_deliveries (delivery_id),
    CONSTRAINT chk_tb_message_deliveries_channel CHECK (channel IN ('WHATSAPP', 'EMAIL')),
    CONSTRAINT chk_tb_message_deliveries_status  CHECK (status IN ('LOGGED', 'SENT', 'DELIVERED', 'READ', 'FAILED', 'SKIPPED')),
    CONSTRAINT chk_tb_message_deliveries_kind    CHECK (sender_kind IN ('LOG', 'CLOUD', 'SMTP')),
    CONSTRAINT chk_tb_message_deliveries_scope   CHECK (sender_scope IS NULL OR sender_scope IN ('PLATFORM', 'SCHOOL')),
    CONSTRAINT chk_tb_message_deliveries_attempts CHECK (attempts >= 1),
    CONSTRAINT chk_tb_message_deliveries_target  CHECK (guardian_id IS NOT NULL OR user_id IS NOT NULL),
    CONSTRAINT chk_tb_message_deliveries_failure CHECK (status <> 'FAILED' OR error_code IS NOT NULL)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_message_deliveries_provider_id ON public.tb_message_deliveries (provider_message_id) WHERE provider_message_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_tb_message_deliveries_guardian ON public.tb_message_deliveries (school_id, guardian_id, purpose, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_tb_message_deliveries_school   ON public.tb_message_deliveries (school_id, created_at DESC);

-- ------------------------------------------------------------------------------------------------ 3. the guardian login number counter (one row per school)
CREATE TABLE IF NOT EXISTS public.tb_guardian_login_counters (
    school_id    varchar(100) PRIMARY KEY,
    last_number  integer      NOT NULL DEFAULT 0,
    updated_at   timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_tb_guardian_login_counters_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT chk_tb_guardian_login_counters_number CHECK (last_number >= 0)
);

-- ------------------------------------------------------------------------------------------------ 4. a guardian login id is unique across the platform, ignoring case
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_users_guardian_login ON public.tb_users (lower(org_user_id)) WHERE user_type = 'Guardian' AND org_user_id IS NOT NULL;

-- ------------------------------------------------------------------------------------------------ verification
DO $st6av$
DECLARE
    b _st6a_before;
    gf text; af text; uf text;
BEGIN
    IF NOT (SELECT migrating FROM _st6a_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st6a_before;
    SELECT coalesce(md5(string_agg((to_jsonb(g) - 'consent_given_at' - 'consent_recorded_by' - 'consent_source')::text, E'\n' ORDER BY guardian_id)), '') INTO gf FROM public.tb_guardians g;
    SELECT coalesce(md5(string_agg((to_jsonb(a) - 'guardian_consent_at' - 'guardian_consent_recorded_by')::text, E'\n' ORDER BY a.admission_id)), '') INTO af FROM public.tb_admissions a;
    SELECT coalesce(md5(string_agg(to_jsonb(u)::text, E'\n' ORDER BY user_id)), '') INTO uf FROM public.tb_users u;
    IF (SELECT count(*) FROM public.tb_guardians) <> b.guardians OR (SELECT count(*) FROM public.tb_admissions) <> b.admissions OR (SELECT count(*) FROM public.tb_users) <> b.users
       OR gf <> b.guardian_fp OR af <> b.admission_fp OR uf <> b.user_fp THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a guardian, admission or user row changed. Everything is rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM public.tb_guardians WHERE consent_given_at IS NOT NULL OR consent_recorded_by IS NOT NULL OR consent_source IS NOT NULL)
       OR EXISTS (SELECT 1 FROM public.tb_admissions WHERE guardian_consent_at IS NOT NULL OR guardian_consent_recorded_by IS NOT NULL) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a consent column was filled. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM public.tb_user_action_tokens) <> 0 OR (SELECT count(*) FROM public.tb_message_deliveries) <> 0 OR (SELECT count(*) FROM public.tb_guardian_login_counters) <> 0 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a new table is not empty. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_user_action_tokens'::regclass AND contype = 'c') <> 4
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_message_deliveries'::regclass AND contype = 'c') <> 7
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_guardians'::regclass AND conname IN ('chk_tb_guardians_consent', 'fk_tb_guardians_consent_user')) <> 2
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_admissions'::regclass AND conname IN ('chk_tb_admissions_consent', 'fk_tb_admissions_consent_user')) <> 2
       OR NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'uq_tb_users_guardian_login')
       OR NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'uq_tb_message_deliveries_provider_id') THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a constraint or index is missing. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % guardians, % admissions and % users unchanged; every consent column is NULL; the three new tables are empty; every constraint and index exists.', b.guardians, b.admissions, b.users;
END
$st6av$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_user_action_tokens) AS tokens, (SELECT count(*) FROM public.tb_message_deliveries) AS deliveries, (SELECT count(*) FROM public.tb_guardian_login_counters) AS counters;
