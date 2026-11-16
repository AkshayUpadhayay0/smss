-- =====================================================================================================================
-- RUN-ME: Fee concession chosen at payment time  (one self-contained file for pgAdmin's Query Tool)
--
-- WHAT IT DOES  (additive only: nothing is dropped, no existing value is changed)
--   1. Adds to tb_fee_payments:  concession_id (FK tb_fee_concessions), concession_name_snapshot, concession_code_snapshot,
--      concession_type_snapshot, concession_value_snapshot, concession_amount (NOT NULL, default 0), concession_reason,
--      concession_applied_by (FK tb_users).
--   2. Adds to tb_fee_payment_allocations:  concession_amount (NOT NULL, default 0): the part of that allocation that was discounted.
--   3. Adds CHECKs:
--        tb_fee_payments            concession_amount >= 0
--                                   concession_amount = 0  OR  (concession_id, the four snapshots and applied_by are set AND the reason is not blank)
--        tb_fee_payment_allocations concession_amount >= 0 AND concession_amount <= allocated_amount
--   4. Existing rows are backfilled with 0 (they were taken without a concession). Money columns are NOT touched:
--        amount      = what was paid in cash (fee_amount + tax_amount)               - unchanged
--        fee_amount  = the cash fee, i.e. the gross applied to installments MINUS the concession - for old rows: equal to the gross
--      From now on:  sum(allocations) = fee_amount + concession_amount.
--   5. FULL WAIVER (added in a later revision of this file): relaxes the existing check chk_tb_fee_payments_amount from  amount > 0  to
--        amount >= 0 AND (amount > 0 OR concession_amount > 0)
--      so a payment whose whole amount is discounted (cash total 0) can be recorded WHEN it carries a concession. A payment with amount 0 and no concession,
--      and a negative amount, stay refused. The check is dropped and re-added in the same transaction (only when its definition is not already the new one),
--      so running this file again on a database that already ran the first revision applies just this step and leaves everything else as it is.
--   6. Verifies (the whole file is ONE transaction; any failed check RAISES and rolls EVERYTHING back):
--        - per-school counts and sums of payments (amount) and of allocations (allocated_amount) are exactly what they were before
--        - every payment that existed before this run has concession_amount = 0, and so does every allocation
--        - per payment, the sum of its allocations' concession_amount = the payment's concession_amount
--        - the amount check has the new definition, and no payment has a negative amount or an amount of 0 without a concession
--        - per payment, allocations add up to fee_amount + concession_amount. Exceptions are only REPORTED (WARNING in the Messages tab),
--          never fixed and never hidden: they existed before this script and are not something a migration should silently repair.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-concession.dump smss-db
--   2. Run Docs/smss-fee-tax-types-RUN-ME.sql FIRST if it has not been run (this file needs fee_amount / tax_amount). The pre-checks stop here if not.
--   3. Make sure you are connected to the RIGHT database. The script prints current_database() as its first message: read it.
--   4. Stop (or accept a brief pause of) the API while it runs; start the new API build only after it says VERIFIED. The old API build keeps
--      working against the migrated tables (the new columns have defaults), but it cannot record a concession.
--
-- HOW TO RUN IT IN pgAdmin
--   Open this file in the Query Tool of the database you want to change (right-click the database > Query Tool > open file),
--   run the whole file with F5 (Execute script), and read the "Messages" tab: you should see "VERIFIED" near the end and, in the
--   "Data Output" tab, the final table (per school: payments, total, cash fee part, concession part). If something fails, pgAdmin shows an
--   error and the transaction is rolled back (if the Query Tool is left in a failed state, click Rollback). Do not edit between BEGIN and COMMIT.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and says "ALREADY MIGRATED: nothing changed" in the Messages tab.
--
-- HOW TO ROLL BACK
--   Simplest and exact: restore the pg_dump taken in step 1. (Or, before any concession has been given:
--   ALTER TABLE tb_fee_payments DROP CONSTRAINT chk_tb_fee_payments_amount;  ALTER TABLE tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_amount CHECK (amount > 0);
--   ALTER TABLE tb_fee_payment_allocations DROP COLUMN concession_amount;
--   ALTER TABLE tb_fee_payments DROP COLUMN concession_applied_by, DROP COLUMN concession_reason, DROP COLUMN concession_amount,
--     DROP COLUMN concession_value_snapshot, DROP COLUMN concession_type_snapshot, DROP COLUMN concession_code_snapshot,
--     DROP COLUMN concession_name_snapshot, DROP COLUMN concession_id;   - the constraints go with their columns.)
-- =====================================================================================================================

BEGIN;

-- ---------------------------------------------------------------------------------------------------------------------
-- PRE-CHECKS (nothing has been changed yet)
-- ---------------------------------------------------------------------------------------------------------------------
DO $pre$
DECLARE
    v_missing text;
    t         text;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_users', 'tb_fee_concessions', 'tb_fee_payments', 'tb_fee_payment_allocations'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist. This is not the database this script is meant for. Nothing was changed.', t;
        END IF;
    END LOOP;

    SELECT string_agg(c, ', ') INTO v_missing FROM unnest(ARRAY['payment_id', 'school_id', 'amount', 'fee_amount', 'tax_amount', 'status']) c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments' AND column_name = c);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'tb_fee_payments is missing column(s): %. Run Docs/smss-fee-tax-types-RUN-ME.sql first (or this is not the expected schema). Nothing was changed.', v_missing;
    END IF;

    SELECT string_agg(c, ', ') INTO v_missing FROM unnest(ARRAY['allocation_id', 'payment_id', 'fee_due_id', 'allocated_amount']) c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payment_allocations' AND column_name = c);
    IF v_missing IS NOT NULL THEN RAISE EXCEPTION 'tb_fee_payment_allocations is missing column(s): %. Not the expected schema. Nothing was changed.', v_missing; END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_concessions' AND column_name = 'concession_id') THEN
        RAISE EXCEPTION 'tb_fee_concessions has no concession_id column. Not the expected schema. Nothing was changed.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_users' AND column_name = 'user_id') THEN
        RAISE EXCEPTION 'tb_users has no user_id column. Not the expected schema. Nothing was changed.';
    END IF;

    -- an existing concession_amount column must have the right type (a half-applied earlier attempt)
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments'
               AND column_name = 'concession_amount' AND data_type <> 'numeric') THEN
        RAISE EXCEPTION 'tb_fee_payments.concession_amount already exists with a type other than numeric. Nothing was changed; look at that column first.';
    END IF;

    RAISE NOTICE 'Pre-checks passed.';
END
$pre$;

-- ---------------------------------------------------------------------------------------------------------------------
-- BEFORE SNAPSHOT. Temp tables vanish at COMMIT.
-- ---------------------------------------------------------------------------------------------------------------------
CREATE TEMP TABLE _cn_state    (migrating boolean NOT NULL, amount_rule boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _cn_before_p (school_id text, payments bigint, total numeric) ON COMMIT DROP;
CREATE TEMP TABLE _cn_before_a (school_id text, allocations bigint, total numeric) ON COMMIT DROP;
CREATE TEMP TABLE _cn_old_pay  (payment_id bigint) ON COMMIT DROP;
CREATE TEMP TABLE _cn_old_alc  (allocation_id bigint) ON COMMIT DROP;

INSERT INTO _cn_state
SELECT EXISTS (
    SELECT 1 FROM unnest(ARRAY['concession_id', 'concession_name_snapshot', 'concession_code_snapshot', 'concession_type_snapshot',
                               'concession_value_snapshot', 'concession_amount', 'concession_reason', 'concession_applied_by']) c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments' AND column_name = c))
    OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payment_allocations' AND column_name = 'concession_amount')
    OR (SELECT count(*) FROM pg_constraint WHERE conrelid IN ('public.tb_fee_payments'::regclass, 'public.tb_fee_payment_allocations'::regclass)
            AND conname IN ('chk_tb_fee_payments_concession_nonneg', 'chk_tb_fee_payments_concession_snapshot', 'chk_tb_fee_payment_allocations_concession',
                            'fk_tb_fee_payments_concession', 'fk_tb_fee_payments_concession_user')) < 5,
       -- the full-waiver amount rule still has to be applied (the check is missing or still the old "amount > 0")
       coalesce((SELECT pg_get_constraintdef(oid) FROM pg_constraint
                 WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname = 'chk_tb_fee_payments_amount'), '') NOT LIKE '%concession_amount%';

INSERT INTO _cn_before_p SELECT school_id::text, count(*), coalesce(sum(amount), 0) FROM public.tb_fee_payments GROUP BY school_id;
INSERT INTO _cn_before_a SELECT p.school_id::text, count(*), coalesce(sum(a.allocated_amount), 0)
                         FROM public.tb_fee_payment_allocations a JOIN public.tb_fee_payments p ON p.payment_id = a.payment_id GROUP BY p.school_id;
INSERT INTO _cn_old_pay SELECT payment_id FROM public.tb_fee_payments;
INSERT INTO _cn_old_alc SELECT allocation_id FROM public.tb_fee_payment_allocations;

DO $snap$
DECLARE r record;
BEGIN
    IF (SELECT migrating FROM _cn_state) THEN RAISE NOTICE 'MIGRATING: the concession columns / checks are not all in place yet.';
    ELSE RAISE NOTICE 'ALREADY MIGRATED: the concession columns and checks are in place (the checks below still run).'; END IF;
    IF (SELECT amount_rule FROM _cn_state) THEN RAISE NOTICE 'FULL WAIVER: the amount check will be relaxed to allow amount = 0 when a concession is given.';
    ELSE RAISE NOTICE 'FULL WAIVER: the amount check already allows it.'; END IF;
    FOR r IN SELECT p.school_id, p.payments, p.total, coalesce(a.allocations, 0) AS allocations, coalesce(a.total, 0) AS alloc_total
             FROM _cn_before_p p LEFT JOIN _cn_before_a a ON a.school_id = p.school_id ORDER BY p.school_id LOOP
        RAISE NOTICE 'before: school % | % payment(s), total % | % allocation(s), total %', r.school_id, r.payments, r.total, r.allocations, r.alloc_total;
    END LOOP;
END
$snap$;

-- ---------------------------------------------------------------------------------------------------------------------
-- 1. the new columns
-- ---------------------------------------------------------------------------------------------------------------------
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS concession_id             BIGINT        NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS concession_name_snapshot  VARCHAR(100)  NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS concession_code_snapshot  VARCHAR(30)   NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS concession_type_snapshot  VARCHAR(20)   NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS concession_value_snapshot NUMERIC(10,2) NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS concession_amount         NUMERIC(12,2) NOT NULL DEFAULT 0;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS concession_reason         TEXT          NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS concession_applied_by     BIGINT        NULL;

ALTER TABLE public.tb_fee_payment_allocations ADD COLUMN IF NOT EXISTS concession_amount NUMERIC(12,2) NOT NULL DEFAULT 0;

-- backfill: every existing row was taken without a concession (a NOT NULL DEFAULT 0 column already reads 0; this also covers a half-applied earlier run)
UPDATE public.tb_fee_payments            SET concession_amount = 0 WHERE concession_amount IS NULL;
UPDATE public.tb_fee_payment_allocations SET concession_amount = 0 WHERE concession_amount IS NULL;

-- foreign keys (a payment keeps pointing at the concession even after it is deactivated; neither row can be deleted from under it)
DO $fk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname = 'fk_tb_fee_payments_concession') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT fk_tb_fee_payments_concession
            FOREIGN KEY (concession_id) REFERENCES public.tb_fee_concessions (concession_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname = 'fk_tb_fee_payments_concession_user') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT fk_tb_fee_payments_concession_user
            FOREIGN KEY (concession_applied_by) REFERENCES public.tb_users (user_id);
    END IF;
END
$fk$;
CREATE INDEX IF NOT EXISTS idx_tb_fee_payments_concession ON public.tb_fee_payments (school_id, payment_date DESC) WHERE concession_id IS NOT NULL;

-- ---------------------------------------------------------------------------------------------------------------------
-- 2. VERIFY THE DATA BEFORE the CHECKs are added, so a bad row gives a clear message instead of a bare constraint error
-- ---------------------------------------------------------------------------------------------------------------------
DO $verify_data$
DECLARE
    v_bad bigint;
    v_ids text;
BEGIN
    SELECT count(*), string_agg(payment_id::text, ', ' ORDER BY payment_id) INTO v_bad, v_ids FROM public.tb_fee_payments WHERE concession_amount < 0;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % payment(s) have a negative concession_amount (payment ids: %). Rolled back.', v_bad, v_ids; END IF;

    SELECT count(*), string_agg(payment_id::text, ', ' ORDER BY payment_id) INTO v_bad, v_ids
    FROM public.tb_fee_payments
    WHERE concession_amount <> 0
      AND (concession_id IS NULL OR concession_name_snapshot IS NULL OR concession_code_snapshot IS NULL OR concession_type_snapshot IS NULL
           OR concession_value_snapshot IS NULL OR concession_applied_by IS NULL OR concession_reason IS NULL OR length(btrim(concession_reason)) = 0);
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % payment(s) carry a concession_amount without the concession, its snapshots, who applied it or a reason (payment ids: %). Rolled back.', v_bad, v_ids; END IF;

    SELECT count(*), string_agg(allocation_id::text, ', ' ORDER BY allocation_id) INTO v_bad, v_ids
    FROM public.tb_fee_payment_allocations WHERE concession_amount < 0 OR concession_amount > allocated_amount;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % allocation(s) have a concession_amount outside 0..allocated_amount (allocation ids: %). Rolled back.', v_bad, v_ids; END IF;
END
$verify_data$;

-- ---------------------------------------------------------------------------------------------------------------------
-- 3. the CHECKs
-- ---------------------------------------------------------------------------------------------------------------------
DO $chk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname = 'chk_tb_fee_payments_concession_nonneg') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_concession_nonneg CHECK (concession_amount >= 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname = 'chk_tb_fee_payments_concession_snapshot') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_concession_snapshot CHECK (
            concession_amount = 0
            OR (concession_id IS NOT NULL AND concession_name_snapshot IS NOT NULL AND concession_code_snapshot IS NOT NULL
                AND concession_type_snapshot IS NOT NULL AND concession_value_snapshot IS NOT NULL AND concession_applied_by IS NOT NULL
                AND concession_reason IS NOT NULL AND length(btrim(concession_reason)) > 0));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_fee_payment_allocations'::regclass AND conname = 'chk_tb_fee_payment_allocations_concession') THEN
        ALTER TABLE public.tb_fee_payment_allocations ADD CONSTRAINT chk_tb_fee_payment_allocations_concession
            CHECK (concession_amount >= 0 AND concession_amount <= allocated_amount);
    END IF;
END
$chk$;

-- ---------------------------------------------------------------------------------------------------------------------
-- 4. FULL WAIVER: the amount check. Existing rows were all taken with amount > 0, so they satisfy the new rule too; verified first anyway.
-- ---------------------------------------------------------------------------------------------------------------------
DO $amount$
DECLARE
    v_bad bigint;
    v_ids text;
BEGIN
    SELECT count(*), string_agg(payment_id::text, ', ' ORDER BY payment_id) INTO v_bad, v_ids
    FROM public.tb_fee_payments WHERE amount < 0 OR (amount = 0 AND concession_amount = 0);
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % payment(s) have a negative amount, or an amount of 0 without a concession (payment ids: %). Rolled back.', v_bad, v_ids; END IF;

    IF coalesce((SELECT pg_get_constraintdef(oid) FROM pg_constraint
                 WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname = 'chk_tb_fee_payments_amount'), '') NOT LIKE '%concession_amount%' THEN
        ALTER TABLE public.tb_fee_payments DROP CONSTRAINT IF EXISTS chk_tb_fee_payments_amount;
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_amount CHECK (amount >= 0 AND (amount > 0 OR concession_amount > 0));
    END IF;
END
$amount$;

-- =====================================================================================================================
-- FINAL VERIFICATION (same transaction). Any failure RAISES, which rolls back EVERYTHING above.
-- =====================================================================================================================
DO $verify$
DECLARE
    v_migrating boolean;
    v_bad       bigint;
    r           record;
BEGIN
    SELECT migrating INTO v_migrating FROM _cn_state;

    -- structure
    IF (SELECT count(*) FROM pg_constraint WHERE conrelid IN ('public.tb_fee_payments'::regclass, 'public.tb_fee_payment_allocations'::regclass)
            AND conname IN ('chk_tb_fee_payments_concession_nonneg', 'chk_tb_fee_payments_concession_snapshot', 'chk_tb_fee_payment_allocations_concession',
                            'fk_tb_fee_payments_concession', 'fk_tb_fee_payments_concession_user')) <> 5 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: one of the new checks / foreign keys is missing. Rolled back.';
    END IF;
    IF coalesce((SELECT pg_get_constraintdef(oid) FROM pg_constraint
                 WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname = 'chk_tb_fee_payments_amount'), '') NOT LIKE '%concession_amount%' THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: chk_tb_fee_payments_amount does not have the full-waiver definition. Rolled back.';
    END IF;
    SELECT count(*) INTO v_bad FROM public.tb_fee_payments WHERE amount < 0 OR (amount = 0 AND concession_amount = 0);
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % payment(s) with a negative amount, or an amount of 0 without a concession. Rolled back.', v_bad; END IF;
    IF EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attname = 'concession_amount' AND NOT a.attnotnull
               AND a.attrelid IN ('public.tb_fee_payments'::regclass, 'public.tb_fee_payment_allocations'::regclass)) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a concession_amount column is nullable. Rolled back.';
    END IF;

    -- per-school payment counts / sums of amount, and allocation counts / sums of allocated_amount: unchanged by this run
    SELECT count(*) INTO v_bad FROM (
        (SELECT school_id, payments, total FROM _cn_before_p
         EXCEPT SELECT school_id::text, count(*), coalesce(sum(amount), 0) FROM public.tb_fee_payments WHERE payment_id IN (SELECT payment_id FROM _cn_old_pay) GROUP BY school_id)
        UNION ALL
        (SELECT school_id::text, count(*), coalesce(sum(amount), 0) FROM public.tb_fee_payments WHERE payment_id IN (SELECT payment_id FROM _cn_old_pay) GROUP BY school_id
         EXCEPT SELECT school_id, payments, total FROM _cn_before_p)
    ) d;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: per-school payment counts / sums differ from before (% group(s)). Rolled back.', v_bad; END IF;

    SELECT count(*) INTO v_bad FROM (
        (SELECT school_id, allocations, total FROM _cn_before_a
         EXCEPT SELECT p.school_id::text, count(*), coalesce(sum(a.allocated_amount), 0)
                FROM public.tb_fee_payment_allocations a JOIN public.tb_fee_payments p ON p.payment_id = a.payment_id
                WHERE a.allocation_id IN (SELECT allocation_id FROM _cn_old_alc) GROUP BY p.school_id)
        UNION ALL
        (SELECT p.school_id::text, count(*), coalesce(sum(a.allocated_amount), 0)
         FROM public.tb_fee_payment_allocations a JOIN public.tb_fee_payments p ON p.payment_id = a.payment_id
         WHERE a.allocation_id IN (SELECT allocation_id FROM _cn_old_alc) GROUP BY p.school_id
         EXCEPT SELECT school_id, allocations, total FROM _cn_before_a)
    ) d;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: per-school allocation counts / sums differ from before (% group(s)). Rolled back.', v_bad; END IF;

    -- everything that existed before this run has no concession
    SELECT count(*) INTO v_bad FROM public.tb_fee_payments
    WHERE payment_id IN (SELECT payment_id FROM _cn_old_pay) AND v_migrating AND (concession_amount <> 0 OR concession_id IS NOT NULL);
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % existing payment(s) do not have concession_amount = 0. Rolled back.', v_bad; END IF;
    SELECT count(*) INTO v_bad FROM public.tb_fee_payment_allocations
    WHERE allocation_id IN (SELECT allocation_id FROM _cn_old_alc) AND v_migrating AND concession_amount <> 0;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % existing allocation(s) do not have concession_amount = 0. Rolled back.', v_bad; END IF;

    -- per payment: allocations' concession_amount add up to the payment's concession_amount
    SELECT count(*) INTO v_bad FROM (
        SELECT p.payment_id FROM public.tb_fee_payments p LEFT JOIN public.tb_fee_payment_allocations a ON a.payment_id = p.payment_id
        GROUP BY p.payment_id, p.concession_amount HAVING coalesce(sum(a.concession_amount), 0) <> p.concession_amount) d;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % payment(s) whose allocations'' concession_amount does not add up to the payment''s. Rolled back.', v_bad; END IF;

    -- allocations vs fee_amount + concession_amount: REPORT, never fix
    v_bad := 0;
    FOR r IN
        SELECT p.payment_id, p.school_id, p.receipt_number, p.status, p.fee_amount, p.concession_amount, coalesce(sum(a.allocated_amount), 0) AS allocated
        FROM public.tb_fee_payments p LEFT JOIN public.tb_fee_payment_allocations a ON a.payment_id = p.payment_id
        GROUP BY p.payment_id, p.school_id, p.receipt_number, p.status, p.fee_amount, p.concession_amount
        HAVING coalesce(sum(a.allocated_amount), 0) <> p.fee_amount + p.concession_amount
        ORDER BY p.school_id, p.payment_id
    LOOP
        v_bad := v_bad + 1;
        RAISE WARNING 'ALLOCATION EXCEPTION (reported, NOT fixed): school % | payment % (%) | % | fee_amount % + concession % but allocations add up to %',
            r.school_id, r.payment_id, r.receipt_number, r.status, r.fee_amount, r.concession_amount, r.allocated;
    END LOOP;
    IF v_bad = 0 THEN RAISE NOTICE 'allocations add up to fee_amount + concession_amount for every payment (no exceptions).'; END IF;

    IF v_migrating THEN
        RAISE NOTICE 'VERIFIED: % payment(s) and % allocation(s) unchanged in count and sum, all with concession_amount = 0, checks added, full-waiver amount rule in place.',
            (SELECT count(*) FROM public.tb_fee_payments), (SELECT count(*) FROM public.tb_fee_payment_allocations);
    ELSIF (SELECT amount_rule FROM _cn_state) THEN
        RAISE NOTICE 'VERIFIED: the full-waiver amount rule was applied; nothing else changed. (% payment(s), % allocation(s) unchanged)',
            (SELECT count(*) FROM public.tb_fee_payments), (SELECT count(*) FROM public.tb_fee_payment_allocations);
    ELSE
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed. (structure and invariants verified)';
    END IF;
END
$verify$;

COMMIT;

-- =====================================================================================================================
-- RESULT: per school, payments and how the money splits (cash fee, concession, tax)
-- =====================================================================================================================
SELECT p.school_id,
       count(*)                                  AS payments,
       sum(p.amount)                             AS total_paid,
       sum(p.fee_amount)                         AS cash_fee_part,
       sum(p.concession_amount)                  AS concession_part,
       sum(p.tax_amount)                         AS tax_part,
       count(*) FILTER (WHERE p.concession_id IS NOT NULL) AS payments_with_concession
FROM public.tb_fee_payments p
GROUP BY p.school_id
ORDER BY p.school_id;
