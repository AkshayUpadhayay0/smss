-- Company Finance (Super Admin only), Stage 2 of Subscription & Billing: SMSS's OWN business data (the company that runs the SaaS).
-- Completely separate from any school: no tb_schools row, no tb_employees row, no school_id anywhere in this file.
--
-- Adds: tb_company_employees, lut_expense_category (seeded), tb_company_expenses, tb_company_salary_payments.
-- Needs tb_users, lut_status and the Subscription & Billing tables (the Financial Overview reads tb_subscription_payments).
-- Run BEFORE starting the API build that contains these entities. Safe to run twice. New tables only; nothing existing is altered.
--
-- Decisions:
--  * "Delete" never removes a row. An employee is hidden with status_id -> Inactive (the project's hide-row pattern); an expense or a salary
--    payment is VOIDED (status Voided, with who / when / why), the same soft-void shape as tb_fee_payments / tb_subscription_payments.
--  * A salary payment's amount is its own value, locked when recorded: later changes to the employee's monthly_salary never rewrite a past payment.
--  * One ACTIVE salary payment per employee per month, enforced here by a partial unique index; once a payment is voided the month can be paid again.
--  * No deductions, tax slabs or approval workflow: a flat amount per payment, and the Super Admin logs everything directly.

BEGIN;

-- 1. SMSS's own team
CREATE TABLE IF NOT EXISTS public.tb_company_employees
(
    company_employee_id bigint        GENERATED ALWAYS AS IDENTITY,
    first_name          varchar(100)  NOT NULL,
    last_name           varchar(100)  NOT NULL,
    designation         varchar(100)  NOT NULL,           -- free text: a small internal team needs no designation master
    email               varchar(150),
    mobile              varchar(20),
    date_of_joining     date          NOT NULL,
    date_of_leaving     date,
    monthly_salary      numeric(12,2) NOT NULL,           -- the CURRENT salary: the default of a new salary payment, never copied back into past ones
    status_id           integer       NOT NULL,           -- lut_status Active / Inactive (hide-row pattern); lut_status.sid is an integer
    created_at          timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_company_employees_pkey PRIMARY KEY (company_employee_id),
    CONSTRAINT fk_tb_company_employees_status FOREIGN KEY (status_id) REFERENCES public.lut_status (sid),
    CONSTRAINT chk_tb_company_employees_salary CHECK (monthly_salary >= 0),
    CONSTRAINT chk_tb_company_employees_dates  CHECK (date_of_leaving IS NULL OR date_of_leaving >= date_of_joining)
);
CREATE INDEX IF NOT EXISTS idx_tb_company_employees_status ON public.tb_company_employees (status_id);

-- 2. Expense categories: global, small and fixed (the lut_event_category pattern)
CREATE TABLE IF NOT EXISTS public.lut_expense_category
(
    category_id   bigint       GENERATED ALWAYS AS IDENTITY,
    category_name varchar(60)  NOT NULL,
    category_code varchar(40)  NOT NULL,
    is_active     boolean      NOT NULL DEFAULT true,
    created_at    timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at    timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT lut_expense_category_pkey PRIMARY KEY (category_id),
    CONSTRAINT uq_lut_expense_category_code UNIQUE (category_code),
    CONSTRAINT uq_lut_expense_category_name UNIQUE (category_name)
);

INSERT INTO public.lut_expense_category (category_name, category_code) VALUES
    ('Rent',                  'RENT'),
    ('Utilities',             'UTILITIES'),
    ('Marketing',             'MARKETING'),
    ('Software/Tools',        'SOFTWARE_TOOLS'),
    ('Office Supplies',       'OFFICE_SUPPLIES'),
    ('Travel',                'TRAVEL'),
    ('Professional Services', 'PROFESSIONAL_SERVICES'),
    ('Other',                 'OTHER')
ON CONFLICT (category_code) DO NOTHING;

-- 3. Expenses: logged directly by the Super Admin, soft-voidable
CREATE TABLE IF NOT EXISTS public.tb_company_expenses
(
    expense_id   bigint        GENERATED ALWAYS AS IDENTITY,
    expense_date date          NOT NULL,
    category_id  bigint        NOT NULL,
    description  varchar(500)  NOT NULL,
    amount       numeric(12,2) NOT NULL,
    payment_mode varchar(30)   NOT NULL,   -- Cash, Cheque, UPI, Card, Bank Transfer, Online
    recorded_by  bigint,                   -- tb_users.user_id, from the token
    status       varchar(20)   NOT NULL DEFAULT 'Active',   -- Active / Voided
    voided_by    bigint,
    voided_at    timestamptz,
    void_reason  text,
    created_at   timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at   timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_company_expenses_pkey PRIMARY KEY (expense_id),
    CONSTRAINT fk_tb_company_expenses_category    FOREIGN KEY (category_id) REFERENCES public.lut_expense_category (category_id),
    CONSTRAINT fk_tb_company_expenses_recorded_by FOREIGN KEY (recorded_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_company_expenses_voided_by   FOREIGN KEY (voided_by)   REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_company_expenses_amount CHECK (amount > 0),
    CONSTRAINT chk_tb_company_expenses_status CHECK (status IN ('Active', 'Voided')),
    -- a voided expense always says who, when and why
    CONSTRAINT chk_tb_company_expenses_void_details CHECK (
        status = 'Active'
        OR (voided_by IS NOT NULL AND voided_at IS NOT NULL AND void_reason IS NOT NULL AND length(btrim(void_reason)) > 0))
);
CREATE INDEX IF NOT EXISTS idx_tb_company_expenses_date     ON public.tb_company_expenses (expense_date);
CREATE INDEX IF NOT EXISTS idx_tb_company_expenses_category ON public.tb_company_expenses (category_id);

-- 4. Salary payments: a flat amount per employee per month, locked once recorded, soft-voidable
CREATE TABLE IF NOT EXISTS public.tb_company_salary_payments
(
    salary_payment_id   bigint        GENERATED ALWAYS AS IDENTITY,
    company_employee_id bigint        NOT NULL,
    period_month        smallint      NOT NULL,   -- the month being paid for, 1-12
    period_year         smallint      NOT NULL,
    amount              numeric(12,2) NOT NULL,   -- defaults to the employee's monthly_salary when recorded, but is its own value (bonus / partial month)
    payment_date        date          NOT NULL,   -- when the money went out; the Financial Overview counts a payout in this month
    payment_mode        varchar(30)   NOT NULL,
    remarks             text,
    recorded_by         bigint,                   -- tb_users.user_id, from the token
    status              varchar(20)   NOT NULL DEFAULT 'Active',   -- Active / Voided
    voided_by           bigint,
    voided_at           timestamptz,
    void_reason         text,
    created_at          timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_company_salary_payments_pkey PRIMARY KEY (salary_payment_id),
    CONSTRAINT fk_tb_company_salary_employee    FOREIGN KEY (company_employee_id) REFERENCES public.tb_company_employees (company_employee_id),
    CONSTRAINT fk_tb_company_salary_recorded_by FOREIGN KEY (recorded_by)         REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_company_salary_voided_by   FOREIGN KEY (voided_by)           REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_company_salary_month  CHECK (period_month BETWEEN 1 AND 12),
    CONSTRAINT chk_tb_company_salary_year   CHECK (period_year BETWEEN 2000 AND 2100),
    CONSTRAINT chk_tb_company_salary_amount CHECK (amount > 0),
    CONSTRAINT chk_tb_company_salary_status CHECK (status IN ('Active', 'Voided')),
    CONSTRAINT chk_tb_company_salary_void_details CHECK (
        status = 'Active'
        OR (voided_by IS NOT NULL AND voided_at IS NOT NULL AND void_reason IS NOT NULL AND length(btrim(void_reason)) > 0))
);
CREATE INDEX IF NOT EXISTS idx_tb_company_salary_employee ON public.tb_company_salary_payments (company_employee_id);
CREATE INDEX IF NOT EXISTS idx_tb_company_salary_date     ON public.tb_company_salary_payments (payment_date);
-- ONE ACTIVE payment per employee per month: no double pay, but a voided month can be paid again
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_company_salary_one_active_per_month
    ON public.tb_company_salary_payments (company_employee_id, period_month, period_year) WHERE status = 'Active';

COMMIT;
