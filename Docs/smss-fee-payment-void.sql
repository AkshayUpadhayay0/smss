-- Soft void for fee payments (Stage 3 follow-up). A payment is never deleted: voiding marks it Voided with who / when / why, and its
-- allocations stay as the audit trail. The amounts it had put against installments are taken back off tb_student_fee_dues.amount_paid
-- by the API in the same transaction. Receipt numbers are never reused (the row stays).
--
-- Voiding reverses the fee bookkeeping only: it does not move or track cash (refunds are out of scope).
--
-- Needs smss-fee-payments.sql. Run BEFORE starting the API build that maps these columns (every payment query reads them). Safe to run twice.
-- Existing payments become 'Active' (the default).

BEGIN;

ALTER TABLE public.tb_fee_payments
    ADD COLUMN IF NOT EXISTS status      varchar(20) NOT NULL DEFAULT 'Active',
    ADD COLUMN IF NOT EXISTS voided_by   bigint,
    ADD COLUMN IF NOT EXISTS voided_at   timestamptz,
    ADD COLUMN IF NOT EXISTS void_reason text;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_fee_payments_voided_by') THEN
        ALTER TABLE public.tb_fee_payments
            ADD CONSTRAINT fk_tb_fee_payments_voided_by FOREIGN KEY (voided_by) REFERENCES public.tb_users (user_id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_fee_payments_status') THEN
        ALTER TABLE public.tb_fee_payments
            ADD CONSTRAINT chk_tb_fee_payments_status CHECK (status IN ('Active', 'Voided'));
    END IF;

    -- a voided payment always says who, when and why
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_fee_payments_void_details') THEN
        ALTER TABLE public.tb_fee_payments
            ADD CONSTRAINT chk_tb_fee_payments_void_details CHECK (
                status = 'Active'
                OR (voided_by IS NOT NULL AND voided_at IS NOT NULL AND void_reason IS NOT NULL AND length(btrim(void_reason)) > 0));
    END IF;
END $$;

COMMIT;
