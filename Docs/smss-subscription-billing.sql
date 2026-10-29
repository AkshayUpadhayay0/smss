-- Subscription & Billing, stage 1: what SMSS (the Super Admin) charges each school, per student (optionally teachers and staff too),
-- with an optional negotiated rate / discount per school. NOT the school-to-student Fee module.
--
-- Needs tb_schools and tb_users. Run BEFORE starting the API build that contains these entities AND the registration hook:
-- school registration inserts a tb_school_subscriptions row, so registering a school fails if these tables are missing.
-- Safe to run twice. New tables only; nothing existing is altered.
-- A database that already has the FIRST version of these tables (per-user pricing, payments tied to one invoice) must run
-- smss-subscription-per-student-upgrade.sql BEFORE this file (it also adds the per-school terms columns to an existing
-- tb_school_subscriptions).
--
-- Design:
--  * tb_subscription_pricing is global and versioned: changing the rate adds a row and deactivates the old one, so history stays.
--    ONE ACTIVE row is enforced here by a partial unique index (the same idea as tb_class_teacher_assignments).
--  * tb_school_subscriptions is 1:1 with tb_schools. Its status is the SUBSCRIPTION state, separate from the school's operational
--    status in tb_schools.school_status_id (a school can be operationally Active and subscription Overdue).
--  * Billing is per user, normally students only. A school may have its own rate and yearly discount (custom_* columns; NULL = use the
--    global pricing row) and may have its teachers and staff billed as well (include_staff_in_billing). These are the school's terms:
--    they only affect invoices generated afterwards. Every invoice stores the student / teacher / staff counts, which of them were billed
--    (billable_user_count, staff_billed) and the rate and discount actually used, locked at generation.
--  * An invoice's status (Pending / Paid / Overdue) is NEVER stored: it is derived from its allocations from Active payments and due_date.
--  * A payment belongs to a SCHOOL and may cover several invoices; tb_subscription_payment_allocations splits it (same idea as
--    tb_fee_payment_allocations). Voiding a payment marks it Voided and keeps every row (who / when / why); because an invoice's paid
--    amount only counts allocations of Active payments, the void needs no further bookkeeping.
--  * Nothing here is backfilled: schools registered before this file have no subscription row (see smss-subscription-backfill.sql).

BEGIN;

-- 1. Global pricing, one Active row at a time
CREATE TABLE IF NOT EXISTS public.tb_subscription_pricing
(
    pricing_id               bigint        GENERATED ALWAYS AS IDENTITY,
    rate_per_student_monthly numeric(10,2) NOT NULL,
    yearly_discount_percent  numeric(5,2)  NOT NULL DEFAULT 0,
    trial_days               integer       NOT NULL,
    effective_from           date          NOT NULL DEFAULT CURRENT_DATE,
    is_active                boolean       NOT NULL DEFAULT true,
    created_at               timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_subscription_pricing_pkey PRIMARY KEY (pricing_id),
    CONSTRAINT chk_tb_subscription_pricing_rate     CHECK (rate_per_student_monthly >= 0),
    CONSTRAINT chk_tb_subscription_pricing_discount CHECK (yearly_discount_percent >= 0 AND yearly_discount_percent <= 100),
    CONSTRAINT chk_tb_subscription_pricing_trial    CHECK (trial_days >= 0)
);
-- ONE ACTIVE pricing row
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_subscription_pricing_one_active ON public.tb_subscription_pricing (is_active) WHERE is_active;

-- Seed the first row only when the table is empty: Rs 20 per student per month, 10% off yearly, 30 trial days
INSERT INTO public.tb_subscription_pricing (rate_per_student_monthly, yearly_discount_percent, trial_days, effective_from, is_active)
SELECT 20.00, 10.00, 30, CURRENT_DATE, true
WHERE NOT EXISTS (SELECT 1 FROM public.tb_subscription_pricing);

-- 2. One subscription per school
CREATE TABLE IF NOT EXISTS public.tb_school_subscriptions
(
    subscription_id          bigint        GENERATED ALWAYS AS IDENTITY,
    school_id                varchar(100)  NOT NULL,
    status                   varchar(20)   NOT NULL DEFAULT 'TRIAL',   -- TRIAL / ACTIVE / OVERDUE / CANCELLED
    billing_frequency        varchar(10),                              -- MONTHLY / YEARLY, set when the school leaves the trial
    custom_rate_per_student_monthly numeric(10,2),                     -- this school's negotiated rate; NULL = the global rate
    custom_yearly_discount_percent  numeric(5,2),                      -- this school's negotiated yearly discount; NULL = the global discount
    include_staff_in_billing boolean       NOT NULL DEFAULT false,     -- true = teachers and staff are billed like students
    trial_start_date         date          NOT NULL,
    trial_end_date           date          NOT NULL,
    current_cycle_start_date date,
    current_cycle_end_date   date,
    next_renewal_date        date,
    created_at               timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at               timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_school_subscriptions_pkey PRIMARY KEY (subscription_id),
    CONSTRAINT uq_tb_school_subscriptions_school UNIQUE (school_id),
    CONSTRAINT fk_tb_school_subscriptions_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT chk_tb_school_subscriptions_status    CHECK (status IN ('TRIAL', 'ACTIVE', 'OVERDUE', 'CANCELLED')),
    CONSTRAINT chk_tb_school_subscriptions_frequency CHECK (billing_frequency IS NULL OR billing_frequency IN ('MONTHLY', 'YEARLY')),
    CONSTRAINT chk_tb_school_subscriptions_trial     CHECK (trial_end_date >= trial_start_date),
    CONSTRAINT chk_tb_school_subscriptions_custom    CHECK ((custom_rate_per_student_monthly IS NULL OR custom_rate_per_student_monthly >= 0)
                                                        AND (custom_yearly_discount_percent IS NULL OR (custom_yearly_discount_percent >= 0 AND custom_yearly_discount_percent <= 100)))
);

-- 3. Invoices, generated by hand by the Super Admin. All three head counts are stored for transparency; billable_user_count is the part
--    that was actually priced (students, plus teachers and staff when the school's terms say so). Counts, rate and discount are locked at
--    generation, whether the rate came from the global pricing or from the school's own terms. No status column: it is derived.
CREATE TABLE IF NOT EXISTS public.tb_subscription_invoices
(
    invoice_id             bigint        GENERATED ALWAYS AS IDENTITY,
    school_id              varchar(100)  NOT NULL,
    subscription_id        bigint        NOT NULL,
    billing_period_start   date          NOT NULL,
    billing_period_end     date          NOT NULL,
    billing_frequency      varchar(10)   NOT NULL,
    student_count          integer       NOT NULL,
    teacher_count          integer       NOT NULL,
    staff_count            integer       NOT NULL,
    staff_billed           boolean       NOT NULL,   -- were teachers and staff included in billable_user_count?
    billable_user_count    integer       NOT NULL,   -- what was priced
    rate_per_student       numeric(10,2) NOT NULL,
    discount_percent       numeric(5,2)  NOT NULL,
    gross_amount           numeric(12,2) NOT NULL,
    discount_amount        numeric(12,2) NOT NULL,
    net_amount             numeric(12,2) NOT NULL,
    due_date               date          NOT NULL,
    generated_by           bigint,
    created_at             timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at             timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_subscription_invoices_pkey PRIMARY KEY (invoice_id),
    CONSTRAINT fk_tb_subscription_invoices_school       FOREIGN KEY (school_id)       REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_subscription_invoices_subscription FOREIGN KEY (subscription_id) REFERENCES public.tb_school_subscriptions (subscription_id),
    CONSTRAINT fk_tb_subscription_invoices_generated_by FOREIGN KEY (generated_by)    REFERENCES public.tb_users (user_id),
    -- one invoice per billing period, which also stops a double click from billing the same cycle twice
    CONSTRAINT uq_tb_subscription_invoices_period UNIQUE (subscription_id, billing_period_start),
    -- lets an allocation point at (invoice_id, school_id), so a payment can only be spread over its own school's invoices
    CONSTRAINT uq_tb_subscription_invoices_id_school UNIQUE (invoice_id, school_id),
    CONSTRAINT chk_tb_subscription_invoices_frequency CHECK (billing_frequency IN ('MONTHLY', 'YEARLY')),
    CONSTRAINT chk_tb_subscription_invoices_period    CHECK (billing_period_end >= billing_period_start),
    CONSTRAINT chk_tb_subscription_invoices_counts    CHECK (student_count >= 0 AND teacher_count >= 0 AND staff_count >= 0
                                                             AND billable_user_count = student_count + CASE WHEN staff_billed THEN teacher_count + staff_count ELSE 0 END),
    CONSTRAINT chk_tb_subscription_invoices_amounts   CHECK (gross_amount >= 0 AND discount_amount >= 0 AND net_amount >= 0
                                                             AND net_amount = gross_amount - discount_amount)
);
CREATE INDEX IF NOT EXISTS idx_tb_subscription_invoices_school ON public.tb_subscription_invoices (school_id);

-- 4. Payments: one row per transaction, belongs to a SCHOOL, may cover several invoices (see the allocations). Soft-voidable.
CREATE TABLE IF NOT EXISTS public.tb_subscription_payments
(
    payment_id       bigint        GENERATED ALWAYS AS IDENTITY,
    school_id        varchar(100)  NOT NULL,
    payment_date     timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    amount           numeric(12,2) NOT NULL,   -- total of this transaction; its allocations add up to exactly this
    payment_mode     varchar(30)   NOT NULL,   -- Cash, Cheque, UPI, Card, Bank Transfer, Online
    reference_number varchar(100),
    recorded_by      bigint,
    remarks          text,
    status           varchar(20)   NOT NULL DEFAULT 'Active',   -- Active / Voided
    voided_by        bigint,
    voided_at        timestamptz,
    void_reason      text,
    created_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_subscription_payments_pkey PRIMARY KEY (payment_id),
    CONSTRAINT fk_tb_subscription_payments_school      FOREIGN KEY (school_id)   REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_subscription_payments_recorded_by FOREIGN KEY (recorded_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_subscription_payments_voided_by   FOREIGN KEY (voided_by)   REFERENCES public.tb_users (user_id),
    CONSTRAINT uq_tb_subscription_payments_id_school UNIQUE (payment_id, school_id),
    CONSTRAINT chk_tb_subscription_payments_amount  CHECK (amount > 0),
    CONSTRAINT chk_tb_subscription_payments_status  CHECK (status IN ('Active', 'Voided')),
    -- a voided payment always says who, when and why
    CONSTRAINT chk_tb_subscription_payments_void_details CHECK (
        status = 'Active'
        OR (voided_by IS NOT NULL AND voided_at IS NOT NULL AND void_reason IS NOT NULL AND length(btrim(void_reason)) > 0))
);
CREATE INDEX IF NOT EXISTS idx_tb_subscription_payments_school ON public.tb_subscription_payments (school_id);

-- 5. Allocations: how one payment is split over invoices. Kept when a payment is voided (the audit trail).
--    school_id is repeated on both sides, so the composite foreign keys force the payment and the invoice to belong to the same school.
CREATE TABLE IF NOT EXISTS public.tb_subscription_payment_allocations
(
    allocation_id    bigint        GENERATED ALWAYS AS IDENTITY,
    payment_id       bigint        NOT NULL,
    invoice_id       bigint        NOT NULL,
    school_id        varchar(100)  NOT NULL,
    allocated_amount numeric(12,2) NOT NULL,
    created_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_subscription_payment_allocations_pkey PRIMARY KEY (allocation_id),
    CONSTRAINT fk_tb_subscription_allocations_payment FOREIGN KEY (payment_id, school_id) REFERENCES public.tb_subscription_payments (payment_id, school_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_subscription_allocations_invoice FOREIGN KEY (invoice_id, school_id) REFERENCES public.tb_subscription_invoices (invoice_id, school_id),
    CONSTRAINT uq_tb_subscription_allocations_payment_invoice UNIQUE (payment_id, invoice_id),
    CONSTRAINT chk_tb_subscription_allocations_amount CHECK (allocated_amount > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_subscription_allocations_invoice ON public.tb_subscription_payment_allocations (invoice_id);

COMMIT;
