-- =====================================================================================================================
-- RUN-ME: Late fee at payment, stage 5a-1 = ALL OF THE SCHEMA  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/admission-docs-latefee-registration-design.md section 4 and the Stage 5 decisions approved on 25 Nov 2026.
--
-- WHY A MIGRATION: a late fee is charged at the payment screen, like tax and concession. It needs (1) a per-school rule, (2) a flag per fee type, (3) the late-fee figures on the payment, and (4) one row per
-- (payment, due) saying how much late fee that payment charged on that due (a due paid in two partial payments must not be charged a flat late fee twice). A late fee is NOT an allocation of money against a due:
-- dues, their amounts and their statuses are untouched. NOTHING IS CHARGED BY THIS SCRIPT; every existing payment gets late fee 0 and keeps its exact figures.
--
-- WHAT IT DOES
--   1. tb_fee_types.applies_late_fee            boolean NOT NULL DEFAULT false   (every fee type: off)
--   2. tb_school_late_fee_rules                 one row per school: enabled (off by default), FLAT / PER_DAY / PERCENT, the amount or percent, grace days, a cap per due, whether tax applies to the late fee
--   3. tb_fee_payments                          late_fee_amount (default 0), late_fee_tax_amount (default 0, part of tax_amount), late_fee_decision (APPLIED / WAIVED / NULL), late_fee_note, late_fee_decided_by,
--                                               late_fee_rule_snapshot (jsonb, the rule in force when the payment was taken); chk_tb_fee_payments_total becomes amount = fee_amount + late_fee_amount + tax_amount
--   4. tb_fee_payment_late_fees                 one row per (payment, due): amount charged, days late, days charged (beyond grace), the base, the total computed as of the payment date, what was already
--                                               collected on that due, and the rule snapshot; school_id on the row, composite foreign keys, unique (payment, due)
--   5. two unique keys that only restate what the primary keys already guarantee, so rows can point at the SAME school's payment / due: (school_id, payment_id) and (school_id, fee_due_id)
--
-- PRE-CHECK (violators): the replaced total check is the old one plus a column that is 0 on every existing row, and every new check is satisfied by "late fee 0, no decision": the script counts the rows that would
--   violate the new total check (must be 0) and prints how many existing payments each re-created check is evaluated against (every row of tb_fee_payments).
--   Verification (raises, everything rolls back, on any mismatch): the payment, fee type and due row counts and a fingerprint of every pre-existing column are identical before and after; every payment has late fee 0
--   and no decision; every fee type is off; the new tables are empty; the constraints exist.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-5a1.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   DEPLOY ORDER: run this BEFORE the API build that contains the late fee screens (the build reads these columns and tables).
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK (only while no late fee has been charged)
--   DROP TABLE tb_fee_payment_late_fees; DROP TABLE tb_school_late_fee_rules;
--   ALTER TABLE tb_fee_payments DROP CONSTRAINT chk_tb_fee_payments_total, DROP CONSTRAINT chk_tb_fee_payments_late_shape, DROP CONSTRAINT chk_tb_fee_payments_late_nonneg,
--       DROP CONSTRAINT chk_tb_fee_payments_late_decision, DROP CONSTRAINT uq_tb_fee_payments_school_payment, DROP COLUMN late_fee_amount, DROP COLUMN late_fee_tax_amount, DROP COLUMN late_fee_decision,
--       DROP COLUMN late_fee_note, DROP COLUMN late_fee_decided_by, DROP COLUMN late_fee_rule_snapshot;
--   ALTER TABLE tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_total CHECK (amount = fee_amount + tax_amount);
--   ALTER TABLE tb_student_fee_dues DROP CONSTRAINT uq_tb_student_fee_dues_school_due;  ALTER TABLE tb_fee_types DROP COLUMN applies_late_fee;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st5a1_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st5a1_before (payments bigint, fee_types bigint, dues bigint, pay_fp text, type_fp text) ON COMMIT DROP;

DO $st5a1$
DECLARE
    v_mig boolean;
    v_bad bigint;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    IF to_regclass('public.tb_fee_payments') IS NULL OR to_regclass('public.tb_fee_types') IS NULL OR to_regclass('public.tb_student_fee_dues') IS NULL
       OR to_regclass('public.tb_schools') IS NULL OR to_regclass('public.tb_users') IS NULL THEN
        RAISE EXCEPTION 'A table the late fee hangs on is missing (payments, fee types, dues, schools or users). Nothing was changed.';
    END IF;

    v_mig := NOT (
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_types' AND column_name = 'applies_late_fee')
        AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments' AND column_name = 'late_fee_amount')
        AND to_regclass('public.tb_school_late_fee_rules') IS NOT NULL AND to_regclass('public.tb_fee_payment_late_fees') IS NOT NULL
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_fee_payments_late_shape')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_student_fee_dues_school_due')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_fee_payments_school_payment'));
    INSERT INTO _st5a1_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    SELECT count(*) INTO v_bad FROM public.tb_fee_payments WHERE amount <> fee_amount + tax_amount;
    IF v_bad <> 0 THEN
        RAISE EXCEPTION 'PRE-CHECK FAILED: % existing payments do not satisfy amount = fee_amount + tax_amount. Nothing was changed.', v_bad;
    END IF;

    INSERT INTO _st5a1_before
    SELECT (SELECT count(*) FROM public.tb_fee_payments), (SELECT count(*) FROM public.tb_fee_types), (SELECT count(*) FROM public.tb_student_fee_dues),
           (SELECT coalesce(md5(string_agg(to_jsonb(p)::text, E'\n' ORDER BY payment_id)), '') FROM public.tb_fee_payments p),
           (SELECT coalesce(md5(string_agg(concat_ws('|', fee_type_id, school_id, fee_type_name, fee_type_code, description, is_optional, isactive, created_at, updated_at), E'\n' ORDER BY fee_type_id)), '') FROM public.tb_fee_types);
    RAISE NOTICE 'PRE-CHECK: 0 violators. % existing payments will each be re-evaluated by the re-created total check and by the 3 new late-fee checks (% rows checked in all).',
        (SELECT payments FROM _st5a1_before), (SELECT payments FROM _st5a1_before) * 4;
END
$st5a1$;

-- ------------------------------------------------------------------------------------------------ 1. the flag on the fee type
ALTER TABLE public.tb_fee_types ADD COLUMN IF NOT EXISTS applies_late_fee boolean NOT NULL DEFAULT false;

-- ------------------------------------------------------------------------------------------------ 5. keys that let a row point at the same school's payment / due
DO $st5a1k$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_student_fee_dues_school_due') THEN
        ALTER TABLE public.tb_student_fee_dues ADD CONSTRAINT uq_tb_student_fee_dues_school_due UNIQUE (school_id, fee_due_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_fee_payments_school_payment') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT uq_tb_fee_payments_school_payment UNIQUE (school_id, payment_id);
    END IF;
END
$st5a1k$;

-- ------------------------------------------------------------------------------------------------ 3. the late fee on the payment
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS late_fee_amount        numeric(12,2) NOT NULL DEFAULT 0;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS late_fee_tax_amount    numeric(12,2) NOT NULL DEFAULT 0;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS late_fee_decision      varchar(10)   NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS late_fee_note          text          NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS late_fee_decided_by    bigint        NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS late_fee_rule_snapshot jsonb         NULL;

DO $st5a1p$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_fee_payments_late_fee_user') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT fk_tb_fee_payments_late_fee_user FOREIGN KEY (late_fee_decided_by) REFERENCES public.tb_users (user_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_fee_payments_late_decision') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_late_decision CHECK (late_fee_decision IS NULL OR late_fee_decision IN ('APPLIED', 'WAIVED'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_fee_payments_late_nonneg') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_late_nonneg CHECK (late_fee_amount >= 0 AND late_fee_tax_amount >= 0 AND late_fee_tax_amount <= tax_amount);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_fee_payments_late_shape') THEN
        -- no decision = nothing charged; APPLIED = an amount, who decided and the rule snapshot; WAIVED = no amount, who decided and a note of at least 5 characters
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_late_shape CHECK (
            (late_fee_decision IS NULL AND late_fee_amount = 0 AND late_fee_tax_amount = 0 AND late_fee_decided_by IS NULL AND late_fee_rule_snapshot IS NULL)
            OR (late_fee_decision = 'APPLIED' AND late_fee_amount > 0 AND late_fee_decided_by IS NOT NULL AND late_fee_rule_snapshot IS NOT NULL)
            OR (late_fee_decision = 'WAIVED' AND late_fee_amount = 0 AND late_fee_tax_amount = 0 AND late_fee_decided_by IS NOT NULL AND late_fee_note IS NOT NULL AND length(btrim(late_fee_note)) >= 5));
    END IF;
    -- the total now has a late fee part; every existing row has late_fee_amount = 0, so it keeps its exact figures
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_fee_payments_total' AND pg_get_constraintdef(oid) LIKE '%late_fee_amount%') THEN
        ALTER TABLE public.tb_fee_payments DROP CONSTRAINT IF EXISTS chk_tb_fee_payments_total;
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_total CHECK (amount = fee_amount + late_fee_amount + tax_amount);
    END IF;
END
$st5a1p$;

-- ------------------------------------------------------------------------------------------------ 2. the school's rule
CREATE TABLE IF NOT EXISTS public.tb_school_late_fee_rules (
    school_id     varchar(100) PRIMARY KEY,
    is_enabled    boolean      NOT NULL DEFAULT false,
    rule_type     varchar(10)  NULL,
    amount        numeric(12,2) NULL,                      -- FLAT: the amount per late due; PER_DAY: the amount per day beyond grace
    percent       numeric(5,2) NULL,                       -- PERCENT: of the amount paid against the due in the payment
    grace_days    integer      NOT NULL DEFAULT 0,         -- calendar days from the due date; only the days beyond them are late
    cap_per_due   numeric(12,2) NULL,                      -- the most late fee one due can ever collect (NULL = no cap)
    tax_applies   boolean      NOT NULL DEFAULT false,     -- whether the chosen tax is also charged on the late fee
    created_at    timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at    timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by    bigint       NULL,
    CONSTRAINT fk_tb_school_late_fee_rules_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_school_late_fee_rules_user   FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_school_late_fee_rules_type  CHECK (rule_type IS NULL OR rule_type IN ('FLAT', 'PER_DAY', 'PERCENT')),
    CONSTRAINT chk_tb_school_late_fee_rules_grace CHECK (grace_days BETWEEN 0 AND 365),
    CONSTRAINT chk_tb_school_late_fee_rules_cap   CHECK (cap_per_due IS NULL OR cap_per_due > 0),
    CONSTRAINT chk_tb_school_late_fee_rules_value CHECK (
        NOT is_enabled OR (rule_type IN ('FLAT', 'PER_DAY') AND amount IS NOT NULL AND amount > 0) OR (rule_type = 'PERCENT' AND percent IS NOT NULL AND percent > 0 AND percent <= 100))
);

-- ------------------------------------------------------------------------------------------------ 4. one row per (payment, due)
CREATE TABLE IF NOT EXISTS public.tb_fee_payment_late_fees (
    late_fee_id       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    school_id         varchar(100) NOT NULL,
    payment_id        bigint       NOT NULL,
    fee_due_id        bigint       NOT NULL,
    amount            numeric(12,2) NOT NULL,                -- charged by THIS payment on this due (not an allocation of money against the due)
    days_late         integer      NOT NULL,                 -- calendar days from the due date to the payment date
    days_charged      integer      NOT NULL,                 -- the days beyond the grace days
    base_amount       numeric(12,2) NOT NULL,                -- the amount applied to this due by this payment
    computed_total    numeric(12,2) NOT NULL,                -- the total late fee of this due as of the payment date (capped), before taking off what was already collected
    already_collected numeric(12,2) NOT NULL,                -- late fee collected on this due by earlier non-voided payments
    rule_type         varchar(10)  NOT NULL,
    rule_amount       numeric(12,2) NULL,
    rule_percent      numeric(5,2) NULL,
    grace_days        integer      NOT NULL,
    cap_per_due       numeric(12,2) NULL,
    created_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_tb_fee_payment_late_fees_payment_due UNIQUE (payment_id, fee_due_id),
    CONSTRAINT fk_tb_fee_payment_late_fees_payment FOREIGN KEY (school_id, payment_id) REFERENCES public.tb_fee_payments (school_id, payment_id),
    CONSTRAINT fk_tb_fee_payment_late_fees_due     FOREIGN KEY (school_id, fee_due_id) REFERENCES public.tb_student_fee_dues (school_id, fee_due_id),
    CONSTRAINT chk_tb_fee_payment_late_fees_amounts CHECK (amount > 0 AND base_amount >= 0 AND computed_total >= 0 AND already_collected >= 0 AND (cap_per_due IS NULL OR cap_per_due > 0)),
    CONSTRAINT chk_tb_fee_payment_late_fees_days    CHECK (days_late >= 1 AND days_charged >= 1 AND days_charged <= days_late AND grace_days >= 0),
    CONSTRAINT chk_tb_fee_payment_late_fees_rule    CHECK (rule_type IN ('FLAT', 'PER_DAY', 'PERCENT'))
);
CREATE INDEX IF NOT EXISTS idx_tb_fee_payment_late_fees_due ON public.tb_fee_payment_late_fees (school_id, fee_due_id);

-- ------------------------------------------------------------------------------------------------ verification
DO $st5a1v$
DECLARE
    b _st5a1_before;
    pf text; tf text;
BEGIN
    IF NOT (SELECT migrating FROM _st5a1_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st5a1_before;
    -- the same columns as before the migration (the new ones are left out of the text: they are checked on their own below)
    SELECT coalesce(md5(string_agg((to_jsonb(p) - 'late_fee_amount' - 'late_fee_tax_amount' - 'late_fee_decision' - 'late_fee_note' - 'late_fee_decided_by' - 'late_fee_rule_snapshot')::text, E'\n' ORDER BY payment_id)), '') INTO pf FROM public.tb_fee_payments p;
    SELECT coalesce(md5(string_agg(concat_ws('|', fee_type_id, school_id, fee_type_name, fee_type_code, description, is_optional, isactive, created_at, updated_at), E'\n' ORDER BY fee_type_id)), '') INTO tf FROM public.tb_fee_types;
    IF (SELECT count(*) FROM public.tb_fee_payments) <> b.payments OR (SELECT count(*) FROM public.tb_fee_types) <> b.fee_types OR (SELECT count(*) FROM public.tb_student_fee_dues) <> b.dues OR tf <> b.type_fp OR pf <> b.pay_fp THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a payment, fee type or due row changed. Everything is rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM public.tb_fee_payments WHERE late_fee_amount <> 0 OR late_fee_tax_amount <> 0 OR late_fee_decision IS NOT NULL OR late_fee_note IS NOT NULL OR late_fee_decided_by IS NOT NULL OR late_fee_rule_snapshot IS NOT NULL)
       OR EXISTS (SELECT 1 FROM public.tb_fee_types WHERE applies_late_fee) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an existing payment got a late fee or a fee type got the flag. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM public.tb_school_late_fee_rules) <> 0 OR (SELECT count(*) FROM public.tb_fee_payment_late_fees) <> 0 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: the new tables are not empty. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM public.tb_fee_payments WHERE amount <> fee_amount + late_fee_amount + tax_amount) <> 0 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a payment does not satisfy the new total. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_fee_payment_late_fees'::regclass AND contype = 'f') <> 2
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_school_late_fee_rules'::regclass AND contype = 'c') <> 4
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname IN ('chk_tb_fee_payments_total', 'chk_tb_fee_payments_late_shape', 'chk_tb_fee_payments_late_nonneg', 'chk_tb_fee_payments_late_decision')) <> 4 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a constraint is missing. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % payments (every one re-evaluated by the new total and late-fee checks), % fee types and % dues unchanged; every payment has late fee 0 and no decision; every fee type is off; the new tables are empty.', b.payments, b.fee_types, b.dues;
END
$st5a1v$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_school_late_fee_rules) AS rules, (SELECT count(*) FROM public.tb_fee_payment_late_fees) AS late_fee_rows;
