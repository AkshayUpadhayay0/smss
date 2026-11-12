-- Admin-initiated password reset: one audit row per reset (who reset whom, when, why). The new password is NEVER stored here or anywhere in plain text.
--
-- Run BEFORE starting the API build that maps TbPasswordResetAudit (POST /api/PasswordReset inserts into it). Safe to run twice. New table only; nothing existing is altered.
--
-- Rollback: DROP TABLE public.tb_password_reset_audit;  (nothing else references it)

BEGIN;

CREATE TABLE IF NOT EXISTS public.tb_password_reset_audit
(
    audit_id          bigint        GENERATED ALWAYS AS IDENTITY,
    school_id         varchar(100),                                  -- the TARGET's school (NULL for a login that has none)
    target_user_id    bigint        NOT NULL,                        -- whose password was reset
    target_login_id   varchar(50)   NOT NULL,                        -- their org_user_id at that moment (kept even if the login id is changed later)
    reset_by_user_id  bigint        NOT NULL,                        -- the Super Admin / School Admin who did it (from the token)
    reason            varchar(500)  NOT NULL,
    reset_at          timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_password_reset_audit_pkey PRIMARY KEY (audit_id),
    CONSTRAINT fk_tb_password_reset_audit_target FOREIGN KEY (target_user_id)   REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_password_reset_audit_by     FOREIGN KEY (reset_by_user_id) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_password_reset_audit_reason CHECK (btrim(reason) <> '' AND char_length(reason) <= 500)
);

CREATE INDEX IF NOT EXISTS ix_tb_password_reset_audit_target ON public.tb_password_reset_audit (target_user_id, reset_at DESC);
CREATE INDEX IF NOT EXISTS ix_tb_password_reset_audit_school ON public.tb_password_reset_audit (school_id, reset_at DESC);

COMMIT;
