-- Notices / Communication (v1): the School Admin or a Principal publishes a notice to ALL, the STAFF or the STUDENTS_GUARDIANS; each user's reads are tracked.
--
-- Adds: tb_notices (the notice) and tb_notice_reads (who has opened it).
-- Needs tb_schools, tb_users, lut_status. Run BEFORE starting the API build that contains these entities. Safe to run twice.
--
-- Decisions:
--  * A notice has exactly ONE audience tier: 'ALL', 'STAFF' or 'STUDENTS_GUARDIANS' (a CHECK). Who falls in which tier is resolved by the application.
--  * Publishing is immediate: published_at defaults to now() and there is no draft or schedule.
--  * "Delete" is status_id -> Inactive (the project hide-row pattern); status_id is integer like every other tb_ table (lut_status.sid is integer).
--    The read rows of a hidden notice stay; they only go if the notice row itself is ever deleted (ON DELETE CASCADE).
--  * One read row per notice per user (unique); the application upserts it with ON CONFLICT DO NOTHING, so opening a notice twice, or from two tabs at once, is safe.

BEGIN;

CREATE TABLE IF NOT EXISTS public.tb_notices
(
    notice_id    bigint        GENERATED ALWAYS AS IDENTITY,
    school_id    varchar(100)  NOT NULL,
    title        varchar(200)  NOT NULL,
    body         text          NOT NULL,
    audience     varchar(20)   NOT NULL,
    created_by   bigint        NOT NULL,
    published_at timestamptz   NOT NULL DEFAULT now(),
    status_id    integer       NOT NULL,
    created_at   timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at   timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_notices_pkey PRIMARY KEY (notice_id),
    CONSTRAINT fk_tb_notices_school  FOREIGN KEY (school_id)  REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_notices_creator FOREIGN KEY (created_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_notices_status  FOREIGN KEY (status_id)  REFERENCES public.lut_status (sid),
    CONSTRAINT chk_tb_notices_audience CHECK (audience IN ('ALL', 'STAFF', 'STUDENTS_GUARDIANS')),
    CONSTRAINT chk_tb_notices_title    CHECK (length(btrim(title)) > 0),
    CONSTRAINT chk_tb_notices_body     CHECK (length(btrim(body)) > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_notices_school_published ON public.tb_notices (school_id, published_at DESC);

CREATE TABLE IF NOT EXISTS public.tb_notice_reads
(
    notice_read_id bigint       GENERATED ALWAYS AS IDENTITY,
    notice_id      bigint       NOT NULL,
    user_id        bigint       NOT NULL,
    read_at        timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT tb_notice_reads_pkey PRIMARY KEY (notice_read_id),
    CONSTRAINT fk_tb_nr_notice FOREIGN KEY (notice_id) REFERENCES public.tb_notices (notice_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_nr_user   FOREIGN KEY (user_id)   REFERENCES public.tb_users (user_id),
    CONSTRAINT uq_tb_notice_reads_notice_user UNIQUE (notice_id, user_id)
);
CREATE INDEX IF NOT EXISTS idx_tb_nr_user ON public.tb_notice_reads (user_id);

COMMIT;
