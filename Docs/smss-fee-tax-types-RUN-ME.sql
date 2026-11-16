-- =====================================================================================================================
-- RUN-ME: Tax Types for fee payments  (one self-contained file for pgAdmin's Query Tool)
--
-- WHAT IT DOES  (additive only: nothing is dropped, no existing value is changed)
--   1. Creates tb_tax_types (a school's own tax types: name, code, rate %) with case-insensitive unique name and code per school.
--   2. Adds to tb_fee_payments:  fee_amount, tax_amount (default 0), tax_type_id, tax_name_snapshot, tax_code_snapshot, tax_rate_snapshot.
--      `amount` stays and now means the TOTAL paid (fee_amount + tax_amount). Existing rows are backfilled: fee_amount = amount, tax_amount = 0.
--   3. Makes fee_amount NOT NULL and adds three CHECKs:  amount = fee_amount + tax_amount,  tax_amount >= 0,
--      tax_type_id IS NULL OR tax_name_snapshot IS NOT NULL.
--   4. Verifies (the whole file is ONE transaction; any failed check RAISES and rolls EVERYTHING back):
--        - no payment has amount <> fee_amount + tax_amount
--        - per-school payment counts and sums of `amount` are exactly what they were before
--        - every payment that existed before this run has fee_amount = amount and tax_amount = 0
--        - allocations of every payment add up to its fee_amount. Exceptions are only REPORTED (WARNING in the Messages tab), never fixed
--          and never hidden: they existed before this script and are not something a migration should silently repair.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-tax-types.dump smss-db
--   2. Make sure you are connected to the RIGHT database. The script prints current_database() as its first message: read it.
--   3. Stop (or accept a brief pause of) the API while it runs; start the new API build only after it says VERIFIED.
--      The old API build keeps working against the migrated table (it just never sets the new columns: fee_amount would then be NULL,
--      so deploy the new API build right after this script).
--
-- HOW TO RUN IT IN pgAdmin
--   Open this file in the Query Tool of the database you want to change (right-click the database > Query Tool > open file),
--   run the whole file with F5 (Execute script), and read the "Messages" tab: you should see "VERIFIED" near the end and, in the
--   "Data Output" tab, the final table (per school: payments, total, fee part, tax part). If something fails, pgAdmin shows an error and
--   the transaction is rolled back (if the Query Tool is left in a failed state, click Rollback). Do not edit between BEGIN and COMMIT.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and says "ALREADY MIGRATED: nothing changed" in the Messages tab.
--
-- HOW TO ROLL BACK
--   Simplest and exact: restore the pg_dump taken in step 1. (Or, before any tax has been collected:
--   ALTER TABLE tb_fee_payments DROP COLUMN tax_rate_snapshot, DROP COLUMN tax_code_snapshot, DROP COLUMN tax_name_snapshot, DROP COLUMN tax_type_id,
--   DROP COLUMN tax_amount, DROP COLUMN fee_amount;  DROP TABLE tb_tax_types;  - the CHECK constraints go with their columns.)
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

    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_fee_payments', 'tb_fee_payment_allocations'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist. This is not the database this script is meant for. Nothing was changed.', t;
        END IF;
    END LOOP;

    SELECT string_agg(c, ', ') INTO v_missing FROM unnest(ARRAY['payment_id', 'school_id', 'amount', 'status']) c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments' AND column_name = c);
    IF v_missing IS NOT NULL THEN RAISE EXCEPTION 'tb_fee_payments is missing column(s): %. Not the expected schema. Nothing was changed.', v_missing; END IF;

    SELECT string_agg(c, ', ') INTO v_missing FROM unnest(ARRAY['payment_id', 'allocated_amount']) c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payment_allocations' AND column_name = c);
    IF v_missing IS NOT NULL THEN RAISE EXCEPTION 'tb_fee_payment_allocations is missing column(s): %. Not the expected schema. Nothing was changed.', v_missing; END IF;

    -- tb_tax_types, if it already exists, must have the right shape
    IF to_regclass('public.tb_tax_types') IS NOT NULL THEN
        SELECT string_agg(e.col, ', ') INTO v_missing
        FROM (VALUES ('tax_type_id', 'bigint'), ('school_id', 'character varying'), ('tax_name', 'character varying'), ('tax_code', 'character varying'),
                     ('rate_percent', 'numeric'), ('description', 'text'), ('is_active', 'boolean'),
                     ('created_at', 'timestamp with time zone'), ('updated_at', 'timestamp with time zone')) AS e(col, typ)
        WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns c WHERE c.table_schema = 'public' AND c.table_name = 'tb_tax_types'
                          AND c.column_name = e.col AND c.data_type = e.typ);
        IF v_missing IS NOT NULL THEN
            RAISE EXCEPTION 'tb_tax_types already exists but with the wrong shape (missing or wrong type: %). Nothing was changed; look at that table first.', v_missing;
        END IF;
    END IF;

    RAISE NOTICE 'Pre-checks passed.';
END
$pre$;

-- ---------------------------------------------------------------------------------------------------------------------
-- BEFORE SNAPSHOT. Temp tables vanish at COMMIT.
-- ---------------------------------------------------------------------------------------------------------------------
CREATE TEMP TABLE _tax_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _tax_before (school_id text, payments bigint, total numeric) ON COMMIT DROP;
CREATE TEMP TABLE _tax_old_ids (payment_id bigint) ON COMMIT DROP;

INSERT INTO _tax_state
SELECT to_regclass('public.tb_tax_types') IS NULL
    OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments' AND column_name = 'fee_amount')
    OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments' AND column_name = 'tax_amount')
    OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments' AND column_name = 'tax_type_id')
    OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments' AND column_name = 'tax_name_snapshot')
    OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments' AND column_name = 'tax_code_snapshot')
    OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments' AND column_name = 'tax_rate_snapshot')
    OR EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = 'public.tb_fee_payments'::regclass AND a.attname = 'fee_amount' AND NOT a.attnotnull)
    OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_fee_payments'::regclass
            AND conname IN ('chk_tb_fee_payments_total', 'chk_tb_fee_payments_tax_nonneg', 'chk_tb_fee_payments_tax_snapshot')) < 3;

INSERT INTO _tax_before SELECT school_id::text, count(*), coalesce(sum(amount), 0) FROM public.tb_fee_payments GROUP BY school_id;
INSERT INTO _tax_old_ids SELECT payment_id FROM public.tb_fee_payments;

DO $snap$
DECLARE r record;
BEGIN
    IF (SELECT migrating FROM _tax_state) THEN RAISE NOTICE 'MIGRATING: the tax columns / table / checks are not all in place yet.';
    ELSE RAISE NOTICE 'ALREADY MIGRATED: nothing to change (the checks below still run).'; END IF;
    FOR r IN SELECT * FROM _tax_before ORDER BY school_id LOOP
        RAISE NOTICE 'before: school % | % payment(s) | total %', r.school_id, r.payments, r.total;
    END LOOP;
END
$snap$;

-- ---------------------------------------------------------------------------------------------------------------------
-- 1. tb_tax_types
-- ---------------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tb_tax_types (
    tax_type_id   BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    school_id     VARCHAR(100)  NOT NULL REFERENCES public.tb_schools (school_id),
    tax_name      VARCHAR(100)  NOT NULL,
    tax_code      VARCHAR(30)   NOT NULL,
    rate_percent  NUMERIC(5,2)  NOT NULL,
    description   TEXT          NULL,
    is_active     BOOLEAN       NOT NULL DEFAULT TRUE,
    created_at    TIMESTAMPTZ   NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT chk_tb_tax_types_rate CHECK (rate_percent >= 0 AND rate_percent <= 100),
    CONSTRAINT chk_tb_tax_types_name CHECK (length(btrim(tax_name)) > 0),
    CONSTRAINT chk_tb_tax_types_code CHECK (length(btrim(tax_code)) > 0)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_tax_types_school_code ON public.tb_tax_types (school_id, lower(tax_code));
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_tax_types_school_name ON public.tb_tax_types (school_id, lower(tax_name));
CREATE INDEX IF NOT EXISTS idx_tb_tax_types_school ON public.tb_tax_types (school_id);

-- ---------------------------------------------------------------------------------------------------------------------
-- 2. the new tb_fee_payments columns, then the backfill (only rows that have no fee_amount yet)
-- ---------------------------------------------------------------------------------------------------------------------
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS fee_amount         NUMERIC(12,2) NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS tax_amount         NUMERIC(12,2) NOT NULL DEFAULT 0;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS tax_type_id        BIGINT        NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS tax_name_snapshot  VARCHAR(100)  NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS tax_code_snapshot  VARCHAR(30)   NULL;
ALTER TABLE public.tb_fee_payments ADD COLUMN IF NOT EXISTS tax_rate_snapshot  NUMERIC(5,2)  NULL;

UPDATE public.tb_fee_payments SET fee_amount = amount WHERE fee_amount IS NULL;   -- old rows: no tax, the whole amount is fee

-- the foreign key to the tax type (a payment keeps pointing at it even after the type is deactivated; it can never be deleted)
DO $fk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname = 'fk_tb_fee_payments_tax_type') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT fk_tb_fee_payments_tax_type FOREIGN KEY (tax_type_id) REFERENCES public.tb_tax_types (tax_type_id);
    END IF;
END
$fk$;
CREATE INDEX IF NOT EXISTS idx_tb_fee_payments_tax_type ON public.tb_fee_payments (tax_type_id) WHERE tax_type_id IS NOT NULL;

-- ---------------------------------------------------------------------------------------------------------------------
-- 3. VERIFY THE DATA BEFORE the CHECKs are added, so a bad row gives a clear message instead of a bare constraint error
-- ---------------------------------------------------------------------------------------------------------------------
DO $verify_data$
DECLARE
    v_bad  bigint;
    v_ids  text;
BEGIN
    SELECT count(*), string_agg(payment_id::text, ', ' ORDER BY payment_id) INTO v_bad, v_ids
    FROM public.tb_fee_payments WHERE fee_amount IS NULL OR amount <> fee_amount + tax_amount;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % payment(s) have amount <> fee_amount + tax_amount (payment ids: %). Rolled back.', v_bad, v_ids; END IF;

    SELECT count(*), string_agg(payment_id::text, ', ' ORDER BY payment_id) INTO v_bad, v_ids FROM public.tb_fee_payments WHERE tax_amount < 0;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % payment(s) have a negative tax_amount (payment ids: %). Rolled back.', v_bad, v_ids; END IF;

    SELECT count(*), string_agg(payment_id::text, ', ' ORDER BY payment_id) INTO v_bad, v_ids
    FROM public.tb_fee_payments WHERE tax_type_id IS NOT NULL AND tax_name_snapshot IS NULL;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % payment(s) point at a tax type without a name snapshot (payment ids: %). Rolled back.', v_bad, v_ids; END IF;
END
$verify_data$;

-- ---------------------------------------------------------------------------------------------------------------------
-- 4. NOT NULL and the CHECKs
-- ---------------------------------------------------------------------------------------------------------------------
ALTER TABLE public.tb_fee_payments ALTER COLUMN fee_amount SET NOT NULL;

DO $chk$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname = 'chk_tb_fee_payments_total') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_total CHECK (amount = fee_amount + tax_amount);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname = 'chk_tb_fee_payments_tax_nonneg') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_tax_nonneg CHECK (tax_amount >= 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_fee_payments'::regclass AND conname = 'chk_tb_fee_payments_tax_snapshot') THEN
        ALTER TABLE public.tb_fee_payments ADD CONSTRAINT chk_tb_fee_payments_tax_snapshot CHECK (tax_type_id IS NULL OR tax_name_snapshot IS NOT NULL);
    END IF;
END
$chk$;

-- =====================================================================================================================
-- FINAL VERIFICATION (same transaction). Any failure RAISES, which rolls back EVERYTHING above.
-- =====================================================================================================================
DO $verify$
DECLARE
    v_migrating boolean;
    v_bad       bigint;
    r           record;
BEGIN
    SELECT migrating INTO v_migrating FROM _tax_state;

    -- structure
    IF to_regclass('public.tb_tax_types') IS NULL THEN RAISE EXCEPTION 'VERIFICATION FAILED: tb_tax_types is missing. Rolled back.'; END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_fee_payments'::regclass
            AND conname IN ('chk_tb_fee_payments_total', 'chk_tb_fee_payments_tax_nonneg', 'chk_tb_fee_payments_tax_snapshot', 'fk_tb_fee_payments_tax_type')) <> 4 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: one of the tb_fee_payments checks / foreign key is missing. Rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = 'public.tb_fee_payments'::regclass AND a.attname = 'fee_amount' AND NOT a.attnotnull) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: fee_amount is still nullable. Rolled back.';
    END IF;

    -- every payment: amount = fee + tax
    SELECT count(*) INTO v_bad FROM public.tb_fee_payments WHERE amount <> fee_amount + tax_amount;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % payment(s) with amount <> fee_amount + tax_amount. Rolled back.', v_bad; END IF;

    -- per-school counts and sums of amount: unchanged by this run
    SELECT count(*) INTO v_bad FROM (
        (SELECT school_id, payments, total FROM _tax_before
         EXCEPT SELECT school_id::text, count(*), coalesce(sum(amount), 0) FROM public.tb_fee_payments GROUP BY school_id)
        UNION ALL
        (SELECT school_id::text, count(*), coalesce(sum(amount), 0) FROM public.tb_fee_payments WHERE payment_id IN (SELECT payment_id FROM _tax_old_ids) GROUP BY school_id
         EXCEPT SELECT school_id, payments, total FROM _tax_before)
    ) d;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: per-school payment counts / sums differ from before (% group(s)). Rolled back.', v_bad; END IF;

    -- rows that existed before this run and have no tax type: untouched money, fee_amount = amount, tax_amount = 0
    SELECT count(*) INTO v_bad FROM public.tb_fee_payments p
    WHERE p.payment_id IN (SELECT payment_id FROM _tax_old_ids) AND p.tax_type_id IS NULL AND (p.fee_amount <> p.amount OR p.tax_amount <> 0);
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % existing payment(s) without a tax type are not fee_amount = amount, tax_amount = 0. Rolled back.', v_bad; END IF;

    -- allocations vs fee_amount: REPORT, never fix
    v_bad := 0;
    FOR r IN
        SELECT p.payment_id, p.school_id, p.receipt_number, p.status, p.fee_amount, coalesce(sum(a.allocated_amount), 0) AS allocated
        FROM public.tb_fee_payments p LEFT JOIN public.tb_fee_payment_allocations a ON a.payment_id = p.payment_id
        GROUP BY p.payment_id, p.school_id, p.receipt_number, p.status, p.fee_amount
        HAVING coalesce(sum(a.allocated_amount), 0) <> p.fee_amount
        ORDER BY p.school_id, p.payment_id
    LOOP
        v_bad := v_bad + 1;
        RAISE WARNING 'ALLOCATION EXCEPTION (reported, NOT fixed): school % | payment % (%) | % | fee_amount % but allocations add up to %',
            r.school_id, r.payment_id, r.receipt_number, r.status, r.fee_amount, r.allocated;
    END LOOP;
    IF v_bad = 0 THEN RAISE NOTICE 'allocations add up to fee_amount for every payment (no exceptions).'; END IF;

    IF v_migrating THEN
        RAISE NOTICE 'VERIFIED: tb_tax_types in place, % payment(s) unchanged in count and sum, every old payment has fee_amount = amount and tax_amount = 0, checks added.',
            (SELECT count(*) FROM public.tb_fee_payments);
    ELSE
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed. (structure and invariants verified)';
    END IF;
END
$verify$;

COMMIT;

-- =====================================================================================================================
-- RESULT: per school, payments and how the total splits into fee and tax
-- =====================================================================================================================
SELECT p.school_id,
       count(*)                  AS payments,
       sum(p.amount)             AS total_amount,
       sum(p.fee_amount)         AS fee_part,
       sum(p.tax_amount)         AS tax_part,
       (SELECT count(*) FROM public.tb_tax_types t WHERE t.school_id = p.school_id) AS tax_types
FROM public.tb_fee_payments p
GROUP BY p.school_id
ORDER BY p.school_id;
