-- Refresh tokens for api/Auth/refresh. Only the SHA-256 hash of the token is stored.
CREATE TABLE IF NOT EXISTS public.tb_refresh_tokens (
    refresh_token_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id bigint NOT NULL REFERENCES public.tb_users(user_id) ON DELETE CASCADE,
    token_hash varchar(64) NOT NULL,
    remember_me boolean NOT NULL DEFAULT false,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at timestamp with time zone NULL,
    replaced_by_token_hash varchar(64) NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_tb_refresh_tokens_token_hash ON public.tb_refresh_tokens (token_hash);
CREATE INDEX IF NOT EXISTS ix_tb_refresh_tokens_user_id ON public.tb_refresh_tokens (user_id);
