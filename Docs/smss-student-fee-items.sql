-- Student Fee Billing, stage 2 (Docs/roles-workflow.md section 28): the due day on a fee structure line, per-student fee
-- items (the frequency / applicability the student actually has per fee head) and the generated installments (dues).
-- Payments (tb_fee_payments, tb_fee_payment_allocations) are stage 3 and are NOT in this file.
--
-- Needs, in this order: smss-fee-setup.sql, smss-student-id-varchar.sql, smss-student-fee-assignment.sql.
-- Run BEFORE starting the API build that contains these entities (the fee structure queries read due_day). Safe to run twice.
--
-- Deviation from section 28: tb_students.student_id is a varchar(20) business id (e.g. DPS001ST001), not a bigint, so
-- student_id on both tables is varchar(20).

BEGIN;

-- 1. Due day of the month a recurring fee head falls due (NULL for One Time / Annual, where it is not needed)
ALTER TABLE public.tb_fee_structure_details
    ADD COLUMN IF NOT EXISTS due_day smallint;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_fee_structure_details_due_day') THEN
        ALTER TABLE public.tb_fee_structure_details
            ADD CONSTRAINT chk_tb_fee_structure_details_due_day CHECK (due_day IS NULL OR due_day BETWEEN 1 AND 31);
    END IF;
END $$;

-- 2. tb_student_fee_items: one row per student per academic year per fee head
CREATE TABLE IF NOT EXISTS public.tb_student_fee_items
(
    student_fee_item_id  bigint        GENERATED ALWAYS AS IDENTITY,
    student_id           varchar(20)   NOT NULL,
    school_id            varchar(100)  NOT NULL,
    academic_year_id     bigint        NOT NULL,
    fee_type_id          bigint        NOT NULL,
    frequency_id         bigint        NOT NULL,
    base_amount          numeric(12,2) NOT NULL,
    concession_amount    numeric(12,2) NOT NULL DEFAULT 0,
    net_amount           numeric(12,2) NOT NULL,
    is_applicable        boolean       NOT NULL DEFAULT true,
    isactive             boolean       NOT NULL DEFAULT true,
    created_at           timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_student_fee_items_pkey PRIMARY KEY (student_fee_item_id),
    CONSTRAINT fk_tb_student_fee_items_student   FOREIGN KEY (student_id)       REFERENCES public.tb_students (student_id),
    CONSTRAINT fk_tb_student_fee_items_school    FOREIGN KEY (school_id)        REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_student_fee_items_year      FOREIGN KEY (academic_year_id) REFERENCES public.tb_academic_years (academic_year_id),
    CONSTRAINT fk_tb_student_fee_items_fee_type  FOREIGN KEY (fee_type_id)      REFERENCES public.tb_fee_types (fee_type_id),
    CONSTRAINT fk_tb_student_fee_items_frequency FOREIGN KEY (frequency_id)     REFERENCES public.lut_fee_frequency (frequency_id),
    CONSTRAINT uq_tb_student_fee_items_student_year_type UNIQUE (student_id, academic_year_id, fee_type_id),
    CONSTRAINT chk_tb_student_fee_items_amounts CHECK (base_amount >= 0 AND concession_amount >= 0 AND net_amount >= 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_student_fee_items_school_id ON public.tb_student_fee_items (school_id);

-- 3. tb_student_fee_dues: the generated installments. Status is NOT a column: it is computed from amount_paid and due_date.
CREATE TABLE IF NOT EXISTS public.tb_student_fee_dues
(
    fee_due_id           bigint        GENERATED ALWAYS AS IDENTITY,
    student_fee_item_id  bigint        NOT NULL,
    student_id           varchar(20)   NOT NULL,   -- denormalised for fast list / dashboard queries
    school_id            varchar(100)  NOT NULL,
    due_date             date          NOT NULL,
    amount_due           numeric(12,2) NOT NULL,
    amount_paid          numeric(12,2) NOT NULL DEFAULT 0,   -- cached running total, only ever written by payment allocation (stage 3)
    created_at           timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_student_fee_dues_pkey PRIMARY KEY (fee_due_id),
    CONSTRAINT fk_tb_student_fee_dues_item    FOREIGN KEY (student_fee_item_id) REFERENCES public.tb_student_fee_items (student_fee_item_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_student_fee_dues_student FOREIGN KEY (student_id)          REFERENCES public.tb_students (student_id),
    CONSTRAINT fk_tb_student_fee_dues_school  FOREIGN KEY (school_id)           REFERENCES public.tb_schools (school_id),
    CONSTRAINT uq_tb_student_fee_dues_item_date UNIQUE (student_fee_item_id, due_date),
    CONSTRAINT chk_tb_student_fee_dues_amounts CHECK (amount_due >= 0 AND amount_paid >= 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_student_fee_dues_student   ON public.tb_student_fee_dues (student_id);
CREATE INDEX IF NOT EXISTS idx_tb_student_fee_dues_school_due ON public.tb_student_fee_dues (school_id, due_date);

COMMIT;
