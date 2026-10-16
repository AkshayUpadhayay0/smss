-- Student Fee Billing, stage 3 (Docs/roles-workflow.md section 28): payments and how each one is split over the installments (dues).
--
-- Needs smss-student-fee-items.sql applied first. Run BEFORE starting the API build that contains these entities. Safe to run twice.
--
-- Deviation from section 28: tb_students.student_id is a varchar(20) business id (e.g. DPS001ST001), not a bigint, so
-- student_id is varchar(20) here.
--
-- NOT in this file: anything for voiding / reversing a payment. How that works is still to be decided, and the columns it needs
-- (a void flag, who / when / why) depend on the approach; they will be added by their own file.

BEGIN;

-- tb_fee_payments: one row per transaction, the ONLY thing that marks money as received
CREATE TABLE IF NOT EXISTS public.tb_fee_payments
(
    payment_id      bigint        GENERATED ALWAYS AS IDENTITY,
    student_id      varchar(20)   NOT NULL,
    school_id       varchar(100)  NOT NULL,
    payment_date    timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    amount          numeric(12,2) NOT NULL,   -- total of this transaction, may cover several installments
    payment_mode    varchar(30)   NOT NULL,   -- Cash, Cheque, UPI, Card, Bank Transfer, Online
    receipt_number  varchar(50)   NOT NULL,   -- e.g. RCP-2026-0001, unique and sequential per school
    collected_by    bigint,                   -- tb_users.user_id of whoever recorded it
    remarks         text,
    created_at      timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_fee_payments_pkey PRIMARY KEY (payment_id),
    CONSTRAINT fk_tb_fee_payments_student      FOREIGN KEY (student_id)   REFERENCES public.tb_students (student_id),
    CONSTRAINT fk_tb_fee_payments_school       FOREIGN KEY (school_id)    REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_fee_payments_collected_by FOREIGN KEY (collected_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT uq_tb_fee_payments_school_receipt UNIQUE (school_id, receipt_number),
    CONSTRAINT chk_tb_fee_payments_amount CHECK (amount > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_fee_payments_student ON public.tb_fee_payments (student_id);
CREATE INDEX IF NOT EXISTS idx_tb_fee_payments_school_date ON public.tb_fee_payments (school_id, payment_date);

-- tb_fee_payment_allocations: splits one payment across one or more installments
CREATE TABLE IF NOT EXISTS public.tb_fee_payment_allocations
(
    allocation_id     bigint        GENERATED ALWAYS AS IDENTITY,
    payment_id        bigint        NOT NULL,
    fee_due_id        bigint        NOT NULL,
    allocated_amount  numeric(12,2) NOT NULL,
    created_at        timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_fee_payment_allocations_pkey PRIMARY KEY (allocation_id),
    CONSTRAINT fk_tb_fee_payment_allocations_payment FOREIGN KEY (payment_id) REFERENCES public.tb_fee_payments (payment_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_fee_payment_allocations_due     FOREIGN KEY (fee_due_id) REFERENCES public.tb_student_fee_dues (fee_due_id),
    CONSTRAINT uq_tb_fee_payment_allocations_payment_due UNIQUE (payment_id, fee_due_id),
    CONSTRAINT chk_tb_fee_payment_allocations_amount CHECK (allocated_amount > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_fee_payment_allocations_due ON public.tb_fee_payment_allocations (fee_due_id);

COMMIT;
