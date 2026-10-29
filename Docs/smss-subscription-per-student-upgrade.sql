-- Upgrades a database that has the FIRST version of the subscription tables (per-user pricing, payments tied to one invoice; the shape of
-- the first Stage 1 deployment) to the current design: per-student rate, payment allocations, per-school terms.
-- Run it BEFORE the current smss-subscription-billing.sql. Safe to run twice (a database already upgraded is left alone).
--
--  * tb_subscription_pricing keeps its row (only a column is renamed).
--  * tb_school_subscriptions keeps every row untouched; it gains the per-school terms columns (all NULL / false = the global terms).
--  * The old invoice and payment tables change shape completely, so they are NOT converted. Any rows in them are COPIED, unchanged, into
--    tb_subscription_invoices_legacy / tb_subscription_payments_legacy (plain archive tables, no keys, with archived_at) and the old tables
--    are dropped and recreated by smss-subscription-billing.sql. Archived invoices were generated under the old per-total-user rule and are
--    not repriced: their numbers mean nothing under the new rules. Nothing reads the legacy tables.

BEGIN;

DO $$
BEGIN
    -- 1. archive and drop the old-shape invoices / payments
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_subscription_invoices' AND column_name = 'total_user_count') THEN
        IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'tb_subscription_invoices_legacy') THEN
            CREATE TABLE public.tb_subscription_invoices_legacy AS SELECT *, now() AS archived_at FROM public.tb_subscription_invoices;
            COMMENT ON TABLE public.tb_subscription_invoices_legacy IS 'Invoices generated under the first (per total user) billing model; archived by smss-subscription-per-student-upgrade.sql, not repriced.';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'tb_subscription_payments_legacy') THEN
            CREATE TABLE public.tb_subscription_payments_legacy AS SELECT *, now() AS archived_at FROM public.tb_subscription_payments;
            COMMENT ON TABLE public.tb_subscription_payments_legacy IS 'Payments recorded against invoices of the first billing model; archived by smss-subscription-per-student-upgrade.sql.';
        END IF;
        DROP TABLE public.tb_subscription_payments;
        DROP TABLE public.tb_subscription_invoices;
    END IF;

    -- 2. per student, not per user
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_subscription_pricing' AND column_name = 'rate_per_user_monthly') THEN
        ALTER TABLE public.tb_subscription_pricing RENAME COLUMN rate_per_user_monthly TO rate_per_student_monthly;
    END IF;

    -- 3. per-school terms
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'tb_school_subscriptions') THEN
        ALTER TABLE public.tb_school_subscriptions
            ADD COLUMN IF NOT EXISTS custom_rate_per_student_monthly numeric(10,2),
            ADD COLUMN IF NOT EXISTS custom_yearly_discount_percent  numeric(5,2),
            ADD COLUMN IF NOT EXISTS include_staff_in_billing        boolean NOT NULL DEFAULT false;
        IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_school_subscriptions_custom') THEN
            ALTER TABLE public.tb_school_subscriptions ADD CONSTRAINT chk_tb_school_subscriptions_custom
                CHECK ((custom_rate_per_student_monthly IS NULL OR custom_rate_per_student_monthly >= 0)
                   AND (custom_yearly_discount_percent IS NULL OR (custom_yearly_discount_percent >= 0 AND custom_yearly_discount_percent <= 100)));
        END IF;
    END IF;
END $$;

COMMIT;
