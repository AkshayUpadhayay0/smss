-- =====================================================================================================================
-- RUN-ME: Fee "Effective From" per structure line, installment plans, and backdating  (one self-contained file for pgAdmin's Query Tool)
--
-- WHAT IT DOES  (additive: no due, payment, allocation or receipt is changed, added or removed)
--   1. tb_fee_structure_details.effective_from DATE NOT NULL: from when a fee line is charged. Backfilled with the start date of the structure's
--      academic year (so every existing line behaves exactly as before). It must lie inside the structure's academic year: that is enforced by the API
--      (the year of a structure is fixed after it is created) and CHECKED here (the final verification raises if any line is outside its year).
--   2. tb_student_fee_items.installment_plan VARCHAR(12) NOT NULL DEFAULT 'NONE' (NONE | QUARTERLY | MONTHLY): how each billing cycle's amount is split
--      into installments. The student's own frequency is gone: an item's frequency_id is always its fee line's frequency.
--      EXISTING items whose frequency differs from their line are mapped, WITHOUT touching their dues:
--          line Half-Yearly + item Monthly   -> plan MONTHLY          line Half-Yearly + item Quarterly -> plan QUARTERLY
--          line Quarterly   + item Monthly   -> plan MONTHLY          anything else (coarser / unmappable) -> plan NONE
--      and each such item's frequency_id is set to the line's. Every unmappable one is printed as a WARNING (school, student, fee head, old frequency):
--      their existing dues stay exactly as they are; a later fee sync would regenerate only their unpaid FUTURE dues from the line.
--   3. tb_student_fee_dues.source VARCHAR(12) NOT NULL DEFAULT 'AUTO' ('AUTO' | 'BACKDATED') and backdating_log_id (nullable FK).
--   4. tb_fee_backdating_log: who applied an explicit "Apply to earlier months" (or a back-dated "Charge fees from" at admission), when, for which line or
--      student, the date range, how many dues / students / what total, and the reason.
--   5. Verifies (the whole file is ONE transaction; any failed check RAISES and rolls EVERYTHING back):
--        - per school, the counts and sums of items, dues (amount_due and amount_paid), payments, allocations and receipts are exactly what they were before
--        - every fee line's effective_from lies inside its academic year
--        - every item that has a matching fee line now has that line's frequency; every plan value is valid; every back-dated due has a log row
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-effective-from.dump smss-db
--   2. Run Docs/smss-fee-tax-types-RUN-ME.sql and Docs/smss-fee-concession-at-payment-RUN-ME.sql first if they have not been run (the pre-checks stop here if
--      the fee payment tables are not in that shape).
--   3. Make sure you are connected to the RIGHT database. The script prints current_database() as its first message: read it.
--   4. Stop (or accept a brief pause of) the API while it runs; start the new API build only after it says VERIFIED. The OLD API build cannot run against the
--      migrated tables for fee generation (it knows nothing of effective_from), so deploy the new build right after this script.
--
-- HOW TO RUN IT IN pgAdmin
--   Open this file in the Query Tool of the database you want to change (right-click the database > Query Tool > open file), run the whole file with F5
--   (Execute script), and read the "Messages" tab: you should see "VERIFIED" near the end, any "WARNING: ITEM FREQUENCY NOT MAPPED" lines, and in the
--   "Data Output" tab the final table (per school: lines, items, items on a plan, dues by source). If something fails, pgAdmin shows an error and the
--   transaction is rolled back (if the Query Tool is left in a failed state, click Rollback). Do not edit between BEGIN and COMMIT.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and says "ALREADY MIGRATED: nothing changed" in the Messages tab.
--
-- HOW TO ROLL BACK
--   Simplest and exact: restore the pg_dump taken in step 1. (Or, before the new build has been used:
--   ALTER TABLE tb_student_fee_dues DROP COLUMN backdating_log_id, DROP COLUMN source;  DROP TABLE tb_fee_backdating_log;
--   ALTER TABLE tb_student_fee_items DROP COLUMN installment_plan;  ALTER TABLE tb_fee_structure_details DROP COLUMN effective_from;
--   Note that the frequency_id remapping of step 2 is not undone by that: restore the dump if you need the old item frequencies back.)
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

    FOREACH t IN ARRAY ARRAY['tb_schools', 'tb_users', 'tb_academic_years', 'tb_fee_structures', 'tb_fee_structure_details', 'tb_student_fee_structure',
                             'tb_student_fee_items', 'tb_student_fee_dues', 'lut_fee_frequency', 'tb_fee_payments', 'tb_fee_payment_allocations'] LOOP
        IF to_regclass(format('public.%I', t)) IS NULL THEN
            RAISE EXCEPTION 'Table % does not exist. This is not the database this script is meant for. Nothing was changed.', t;
        END IF;
    END LOOP;

    SELECT string_agg(c, ', ') INTO v_missing FROM unnest(ARRAY['fee_structure_detail_id', 'fee_structure_id', 'fee_type_id', 'frequency_id', 'amount', 'due_day']) c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_structure_details' AND column_name = c);
    IF v_missing IS NOT NULL THEN RAISE EXCEPTION 'tb_fee_structure_details is missing column(s): %. Not the expected schema. Nothing was changed.', v_missing; END IF;

    SELECT string_agg(c, ', ') INTO v_missing FROM unnest(ARRAY['student_fee_item_id', 'student_id', 'school_id', 'academic_year_id', 'fee_type_id', 'frequency_id']) c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_fee_items' AND column_name = c);
    IF v_missing IS NOT NULL THEN RAISE EXCEPTION 'tb_student_fee_items is missing column(s): %. Not the expected schema. Nothing was changed.', v_missing; END IF;

    SELECT string_agg(c, ', ') INTO v_missing FROM unnest(ARRAY['fee_due_id', 'student_fee_item_id', 'due_date', 'amount_due', 'amount_paid']) c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_fee_dues' AND column_name = c);
    IF v_missing IS NOT NULL THEN RAISE EXCEPTION 'tb_student_fee_dues is missing column(s): %. Not the expected schema. Nothing was changed.', v_missing; END IF;

    SELECT string_agg(c, ', ') INTO v_missing FROM unnest(ARRAY['payment_id', 'school_id', 'amount', 'receipt_number']) c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_payments' AND column_name = c);
    IF v_missing IS NOT NULL THEN RAISE EXCEPTION 'tb_fee_payments is missing column(s): %. Not the expected schema. Nothing was changed.', v_missing; END IF;

    SELECT string_agg(c, ', ') INTO v_missing FROM unnest(ARRAY['frequency_id', 'frequency_code']) c
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'lut_fee_frequency' AND column_name = c);
    IF v_missing IS NOT NULL THEN RAISE EXCEPTION 'lut_fee_frequency is missing column(s): %. Not the expected schema. Nothing was changed.', v_missing; END IF;

    -- a half-applied earlier attempt must have the right shape
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_structure_details'
               AND column_name = 'effective_from' AND data_type <> 'date') THEN
        RAISE EXCEPTION 'tb_fee_structure_details.effective_from already exists with a type other than date. Nothing was changed; look at that column first.';
    END IF;

    RAISE NOTICE 'Pre-checks passed.';
END
$pre$;

-- ---------------------------------------------------------------------------------------------------------------------
-- BEFORE SNAPSHOT. Temp tables vanish at COMMIT.
-- ---------------------------------------------------------------------------------------------------------------------
CREATE TEMP TABLE _ef_state    (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _ef_before   (school_id text PRIMARY KEY, items bigint, dues bigint, dues_due numeric, dues_paid numeric, payments bigint, payments_total numeric,
                                allocations bigint, allocations_total numeric, receipts bigint) ON COMMIT DROP;
CREATE TEMP TABLE _ef_mismatch (item_id bigint, school_id text, student_id text, fee_head text, old_frequency text, line_frequency text, new_plan text, mapped boolean) ON COMMIT DROP;

INSERT INTO _ef_state
SELECT NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_fee_structure_details' AND column_name = 'effective_from')
    OR EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = 'public.tb_fee_structure_details'::regclass AND a.attname = 'effective_from' AND NOT a.attnotnull)
    OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_fee_items' AND column_name = 'installment_plan')
    OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_fee_dues' AND column_name = 'source')
    OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_fee_dues' AND column_name = 'backdating_log_id')
    OR to_regclass('public.tb_fee_backdating_log') IS NULL
    OR (SELECT count(*) FROM pg_constraint WHERE conname IN ('chk_tb_student_fee_items_plan', 'chk_tb_student_fee_dues_source', 'chk_tb_student_fee_dues_backdated_log', 'fk_tb_student_fee_dues_backdating_log')) < 4
    OR EXISTS (   -- an item whose frequency still differs from its fee line (the first run maps them)
        SELECT 1 FROM public.tb_student_fee_items i
        JOIN public.tb_student_fee_structure a ON a.student_id = i.student_id AND a.academic_year_id = i.academic_year_id
        JOIN public.tb_fee_structure_details d ON d.fee_structure_id = a.fee_structure_id AND d.fee_type_id = i.fee_type_id
        WHERE i.frequency_id <> d.frequency_id);

INSERT INTO _ef_before
SELECT s.school_id::text,
       (SELECT count(*) FROM public.tb_student_fee_items i WHERE i.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_student_fee_dues d WHERE d.school_id = s.school_id),
       (SELECT coalesce(sum(d.amount_due), 0) FROM public.tb_student_fee_dues d WHERE d.school_id = s.school_id),
       (SELECT coalesce(sum(d.amount_paid), 0) FROM public.tb_student_fee_dues d WHERE d.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_fee_payments p WHERE p.school_id = s.school_id),
       (SELECT coalesce(sum(p.amount), 0) FROM public.tb_fee_payments p WHERE p.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_fee_payment_allocations a JOIN public.tb_fee_payments p ON p.payment_id = a.payment_id WHERE p.school_id = s.school_id),
       (SELECT coalesce(sum(a.allocated_amount), 0) FROM public.tb_fee_payment_allocations a JOIN public.tb_fee_payments p ON p.payment_id = a.payment_id WHERE p.school_id = s.school_id),
       (SELECT count(*) FROM public.tb_fee_payments p WHERE p.school_id = s.school_id AND p.receipt_number IS NOT NULL)
FROM public.tb_schools s;

-- items whose frequency differs from their fee line, and what each maps to
INSERT INTO _ef_mismatch
SELECT i.student_fee_item_id, i.school_id::text, i.student_id::text, t.fee_type_name, fi.frequency_code, fl.frequency_code,
       CASE WHEN fl.frequency_code = 'HALF_YEARLY' AND fi.frequency_code = 'MONTHLY'   THEN 'MONTHLY'
            WHEN fl.frequency_code = 'HALF_YEARLY' AND fi.frequency_code = 'QUARTERLY' THEN 'QUARTERLY'
            WHEN fl.frequency_code = 'QUARTERLY'   AND fi.frequency_code = 'MONTHLY'   THEN 'MONTHLY'
            ELSE 'NONE' END,
       (   (fl.frequency_code = 'HALF_YEARLY' AND fi.frequency_code IN ('MONTHLY', 'QUARTERLY'))
        OR (fl.frequency_code = 'QUARTERLY'   AND fi.frequency_code = 'MONTHLY'))
FROM public.tb_student_fee_items i
JOIN public.tb_student_fee_structure a ON a.student_id = i.student_id AND a.academic_year_id = i.academic_year_id
JOIN public.tb_fee_structure_details d ON d.fee_structure_id = a.fee_structure_id AND d.fee_type_id = i.fee_type_id
JOIN public.lut_fee_frequency fi ON fi.frequency_id = i.frequency_id
JOIN public.lut_fee_frequency fl ON fl.frequency_id = d.frequency_id
JOIN public.tb_fee_types t ON t.fee_type_id = i.fee_type_id
WHERE i.frequency_id <> d.frequency_id;

DO $snap$
DECLARE r record;
BEGIN
    IF (SELECT migrating FROM _ef_state) THEN RAISE NOTICE 'MIGRATING: the effective-from / installment-plan / backdating structures are not all in place yet.';
    ELSE RAISE NOTICE 'ALREADY MIGRATED: nothing to change (the checks below still run).'; END IF;
    FOR r IN SELECT * FROM _ef_before ORDER BY school_id LOOP
        RAISE NOTICE 'before: school % | % item(s) | % due(s), due % paid % | % payment(s), total % | % allocation(s) | % receipt(s)',
            r.school_id, r.items, r.dues, r.dues_due, r.dues_paid, r.payments, r.payments_total, r.allocations, r.receipts;
    END LOOP;
    RAISE NOTICE 'items whose frequency differs from their fee line: %  (of which mapped to an installment plan: %)',
        (SELECT count(*) FROM _ef_mismatch), (SELECT count(*) FROM _ef_mismatch WHERE mapped);
END
$snap$;

-- ---------------------------------------------------------------------------------------------------------------------
-- 1. effective_from on every fee line
-- ---------------------------------------------------------------------------------------------------------------------
ALTER TABLE public.tb_fee_structure_details ADD COLUMN IF NOT EXISTS effective_from DATE NULL;

UPDATE public.tb_fee_structure_details d
SET effective_from = y.start_date
FROM public.tb_fee_structures s
JOIN public.tb_academic_years y ON y.academic_year_id = s.academic_year_id
WHERE d.fee_structure_id = s.fee_structure_id AND d.effective_from IS NULL;

DO $nn$
DECLARE v_bad bigint;
BEGIN
    SELECT count(*) INTO v_bad FROM public.tb_fee_structure_details WHERE effective_from IS NULL;
    IF v_bad > 0 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: % fee line(s) have no academic year to take an effective date from. Rolled back.', v_bad;
    END IF;
END
$nn$;
ALTER TABLE public.tb_fee_structure_details ALTER COLUMN effective_from SET NOT NULL;

-- ---------------------------------------------------------------------------------------------------------------------
-- 2. installment_plan on the student's items, and the mapping of items whose frequency differed from the line
-- ---------------------------------------------------------------------------------------------------------------------
ALTER TABLE public.tb_student_fee_items ADD COLUMN IF NOT EXISTS installment_plan VARCHAR(12) NOT NULL DEFAULT 'NONE';

DO $plan$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_student_fee_items'::regclass AND conname = 'chk_tb_student_fee_items_plan') THEN
        ALTER TABLE public.tb_student_fee_items ADD CONSTRAINT chk_tb_student_fee_items_plan CHECK (installment_plan IN ('NONE', 'QUARTERLY', 'MONTHLY'));
    END IF;
END
$plan$;

-- map the items that differed: plan where there is one, and ALWAYS the frequency of the line (their dues are not touched)
UPDATE public.tb_student_fee_items i
SET installment_plan = m.new_plan,
    frequency_id = d.frequency_id
FROM _ef_mismatch m
JOIN public.tb_student_fee_structure a ON a.student_id = m.student_id::varchar AND a.school_id = m.school_id
JOIN public.tb_fee_structure_details d ON d.fee_structure_id = a.fee_structure_id
WHERE i.student_fee_item_id = m.item_id AND d.fee_type_id = i.fee_type_id AND a.academic_year_id = i.academic_year_id;

DO $warn$
DECLARE r record;
BEGIN
    FOR r IN SELECT * FROM _ef_mismatch WHERE NOT mapped ORDER BY school_id, student_id, fee_head LOOP
        RAISE WARNING 'ITEM FREQUENCY NOT MAPPED (plan set to NONE, existing dues left as they are): school % | student % | fee head % | old frequency % | line frequency %',
            r.school_id, r.student_id, r.fee_head, r.old_frequency, r.line_frequency;
    END LOOP;
END
$warn$;

-- ---------------------------------------------------------------------------------------------------------------------
-- 3. the backdating log, and the source of a due
-- ---------------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tb_fee_backdating_log (
    backdating_log_id  BIGINT        GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    school_id          VARCHAR(100)  NOT NULL REFERENCES public.tb_schools (school_id),
    kind               VARCHAR(10)   NOT NULL,                                   -- LINE (a fee line, all its students) | STUDENT (one student at admission)
    fee_structure_id   BIGINT        NULL REFERENCES public.tb_fee_structures (fee_structure_id),
    fee_type_id        BIGINT        NULL,
    student_id         VARCHAR(20)   NULL,
    academic_year_id   BIGINT        NOT NULL REFERENCES public.tb_academic_years (academic_year_id),
    from_date          DATE          NOT NULL,
    to_date            DATE          NOT NULL,
    students_count     INTEGER       NOT NULL DEFAULT 0,
    dues_count         INTEGER       NOT NULL DEFAULT 0,
    total_amount       NUMERIC(14,2) NOT NULL DEFAULT 0,
    reason             TEXT          NOT NULL,
    created_by         BIGINT        NOT NULL REFERENCES public.tb_users (user_id),
    created_at         TIMESTAMPTZ   NOT NULL DEFAULT now(),
    CONSTRAINT chk_tb_fee_backdating_log_kind   CHECK (kind IN ('LINE', 'STUDENT')),
    CONSTRAINT chk_tb_fee_backdating_log_range  CHECK (to_date >= from_date),
    CONSTRAINT chk_tb_fee_backdating_log_reason CHECK (length(btrim(reason)) > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_fee_backdating_log_school ON public.tb_fee_backdating_log (school_id, created_at DESC);

ALTER TABLE public.tb_student_fee_dues ADD COLUMN IF NOT EXISTS source            VARCHAR(12) NOT NULL DEFAULT 'AUTO';
ALTER TABLE public.tb_student_fee_dues ADD COLUMN IF NOT EXISTS backdating_log_id BIGINT      NULL;

DO $dues$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_student_fee_dues'::regclass AND conname = 'chk_tb_student_fee_dues_source') THEN
        ALTER TABLE public.tb_student_fee_dues ADD CONSTRAINT chk_tb_student_fee_dues_source CHECK (source IN ('AUTO', 'BACKDATED'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_student_fee_dues'::regclass AND conname = 'fk_tb_student_fee_dues_backdating_log') THEN
        ALTER TABLE public.tb_student_fee_dues ADD CONSTRAINT fk_tb_student_fee_dues_backdating_log
            FOREIGN KEY (backdating_log_id) REFERENCES public.tb_fee_backdating_log (backdating_log_id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.tb_student_fee_dues'::regclass AND conname = 'chk_tb_student_fee_dues_backdated_log') THEN
        ALTER TABLE public.tb_student_fee_dues ADD CONSTRAINT chk_tb_student_fee_dues_backdated_log CHECK (source = 'AUTO' OR backdating_log_id IS NOT NULL);
    END IF;
END
$dues$;
CREATE INDEX IF NOT EXISTS idx_tb_student_fee_dues_backdating ON public.tb_student_fee_dues (backdating_log_id) WHERE backdating_log_id IS NOT NULL;

-- =====================================================================================================================
-- FINAL VERIFICATION (same transaction). Any failure RAISES, which rolls back EVERYTHING above.
-- =====================================================================================================================
DO $verify$
DECLARE
    v_migrating boolean;
    v_bad       bigint;
    r           record;
BEGIN
    SELECT migrating INTO v_migrating FROM _ef_state;

    -- structure
    IF (SELECT count(*) FROM pg_constraint WHERE conname IN ('chk_tb_student_fee_items_plan', 'chk_tb_student_fee_dues_source', 'chk_tb_student_fee_dues_backdated_log',
                                                              'fk_tb_student_fee_dues_backdating_log')) <> 4 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: one of the new checks / foreign keys is missing. Rolled back.';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = 'public.tb_fee_structure_details'::regclass AND a.attname = 'effective_from' AND NOT a.attnotnull) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: effective_from is nullable. Rolled back.';
    END IF;

    -- every fee line's effective_from lies inside its academic year
    SELECT count(*) INTO v_bad
    FROM public.tb_fee_structure_details d
    JOIN public.tb_fee_structures s ON s.fee_structure_id = d.fee_structure_id
    JOIN public.tb_academic_years y ON y.academic_year_id = s.academic_year_id
    WHERE d.effective_from < y.start_date OR d.effective_from > y.end_date;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % fee line(s) have an effective_from outside their academic year. Rolled back.', v_bad; END IF;

    -- every item that has a matching fee line has that line's frequency
    SELECT count(*) INTO v_bad
    FROM public.tb_student_fee_items i
    JOIN public.tb_student_fee_structure a ON a.student_id = i.student_id AND a.academic_year_id = i.academic_year_id
    JOIN public.tb_fee_structure_details d ON d.fee_structure_id = a.fee_structure_id AND d.fee_type_id = i.fee_type_id
    WHERE i.frequency_id <> d.frequency_id;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % item(s) still have a frequency different from their fee line. Rolled back.', v_bad; END IF;

    -- every back-dated due has a log row (also enforced by the check)
    SELECT count(*) INTO v_bad FROM public.tb_student_fee_dues WHERE source = 'BACKDATED' AND backdating_log_id IS NULL;
    IF v_bad > 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: % back-dated due(s) without a log row. Rolled back.', v_bad; END IF;

    -- per school: counts and sums unchanged by this run
    FOR r IN
        SELECT b.school_id,
               (SELECT count(*) FROM public.tb_student_fee_items i WHERE i.school_id = b.school_id) AS items,
               (SELECT count(*) FROM public.tb_student_fee_dues d WHERE d.school_id = b.school_id) AS dues,
               (SELECT coalesce(sum(d.amount_due), 0) FROM public.tb_student_fee_dues d WHERE d.school_id = b.school_id) AS dues_due,
               (SELECT coalesce(sum(d.amount_paid), 0) FROM public.tb_student_fee_dues d WHERE d.school_id = b.school_id) AS dues_paid,
               (SELECT count(*) FROM public.tb_fee_payments p WHERE p.school_id = b.school_id) AS payments,
               (SELECT coalesce(sum(p.amount), 0) FROM public.tb_fee_payments p WHERE p.school_id = b.school_id) AS payments_total,
               (SELECT count(*) FROM public.tb_fee_payment_allocations a JOIN public.tb_fee_payments p ON p.payment_id = a.payment_id WHERE p.school_id = b.school_id) AS allocations,
               (SELECT coalesce(sum(a.allocated_amount), 0) FROM public.tb_fee_payment_allocations a JOIN public.tb_fee_payments p ON p.payment_id = a.payment_id WHERE p.school_id = b.school_id) AS allocations_total,
               (SELECT count(*) FROM public.tb_fee_payments p WHERE p.school_id = b.school_id AND p.receipt_number IS NOT NULL) AS receipts,
               b.items AS b_items, b.dues AS b_dues, b.dues_due AS b_due, b.dues_paid AS b_paid, b.payments AS b_pay, b.payments_total AS b_paytot,
               b.allocations AS b_alloc, b.allocations_total AS b_alloctot, b.receipts AS b_rec
        FROM _ef_before b
    LOOP
        IF (r.items, r.dues, r.dues_due, r.dues_paid, r.payments, r.payments_total, r.allocations, r.allocations_total, r.receipts)
           IS DISTINCT FROM (r.b_items, r.b_dues, r.b_due, r.b_paid, r.b_pay, r.b_paytot, r.b_alloc, r.b_alloctot, r.b_rec) THEN
            RAISE EXCEPTION 'VERIFICATION FAILED: school % differs from before (items %/%, dues %/%, due sum %/%, paid sum %/%, payments %/%, allocations %/%, receipts %/%). Rolled back.',
                r.school_id, r.items, r.b_items, r.dues, r.b_dues, r.dues_due, r.b_due, r.dues_paid, r.b_paid, r.payments, r.b_pay, r.allocations, r.b_alloc, r.receipts, r.b_rec;
        END IF;
    END LOOP;

    IF v_migrating THEN
        RAISE NOTICE 'VERIFIED: effective_from on % fee line(s), installment_plan on % item(s) (% mapped to a plan), per-school counts and sums of items, dues, payments, allocations and receipts unchanged.',
            (SELECT count(*) FROM public.tb_fee_structure_details), (SELECT count(*) FROM public.tb_student_fee_items), (SELECT count(*) FROM _ef_mismatch WHERE mapped);
    ELSE
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed. (structure and invariants verified)';
    END IF;
END
$verify$;

COMMIT;

-- =====================================================================================================================
-- RESULT: per school, the fee lines, the items and how many are on an installment plan, and the dues by source
-- =====================================================================================================================
SELECT s.school_id,
       (SELECT count(*) FROM public.tb_fee_structure_details d JOIN public.tb_fee_structures fs ON fs.fee_structure_id = d.fee_structure_id WHERE fs.school_id = s.school_id) AS fee_lines,
       (SELECT count(*) FROM public.tb_student_fee_items i WHERE i.school_id = s.school_id)                                             AS items,
       (SELECT count(*) FROM public.tb_student_fee_items i WHERE i.school_id = s.school_id AND i.installment_plan <> 'NONE')            AS items_on_a_plan,
       (SELECT count(*) FROM public.tb_student_fee_dues d WHERE d.school_id = s.school_id AND d.source = 'AUTO')                       AS dues_auto,
       (SELECT count(*) FROM public.tb_student_fee_dues d WHERE d.school_id = s.school_id AND d.source = 'BACKDATED')                  AS dues_backdated
FROM public.tb_schools s
ORDER BY s.school_id;
