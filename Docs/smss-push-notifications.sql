-- Push notifications (Firebase Cloud Messaging), stage 1: fee-overdue reminders to the Student login.
-- Design: Docs/push-notification-design.md (sections 1 to 6).
--
-- Adds three NEW tables: tb_user_devices, tb_school_notification_settings, tb_push_notification_log.
-- ADDITIVE ONLY: no existing table, column, constraint or row is changed. Safe to run twice (every statement is IF NOT EXISTS / guarded).
-- Needs tb_schools, tb_users and tb_students. Run BEFORE starting the API build that contains these entities.
--
-- Decisions:
--  * tb_user_devices.fcm_token is UNIQUE: a token belongs to ONE user at a time. Registering an existing token re-assigns it to the caller
--    (a shared phone where another student signs in). school_id and user_id always come from the JWT, never the request body.
--  * No hard deletes. A dead token goes is_active = false.
--  * tb_school_notification_settings: one row per school (school_id is the key); NO ROW = the defaults (reminders OFF, 09:00 India time, 0 grace days).
--  * tb_push_notification_log: UNIQUE (student_id, notification_type, notify_date) is the backstop that makes the daily job idempotent
--    after a restart or a double run. notify_date is the INDIA calendar date.
--  * The message text and the data payload carry no amount and no student id (see the design, section 1); the log keeps title/body only.

BEGIN;

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 1. Devices
-- ---------------------------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tb_user_devices
(
    device_id     bigint GENERATED ALWAYS AS IDENTITY,
    user_id       bigint         NOT NULL,
    school_id     varchar(100)   NOT NULL,
    fcm_token     varchar(1024)  NOT NULL,
    platform      varchar(10)    NOT NULL,
    device_label  varchar(100),
    app_version   varchar(30),
    is_active     boolean        NOT NULL DEFAULT true,
    last_seen_at  timestamptz    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_at    timestamptz    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at    timestamptz    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_user_devices_pkey PRIMARY KEY (device_id),
    CONSTRAINT uq_tb_user_devices_token UNIQUE (fcm_token),
    CONSTRAINT fk_tb_user_devices_user   FOREIGN KEY (user_id)   REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_user_devices_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT chk_tb_user_devices_platform CHECK (platform IN ('ANDROID', 'IOS'))
);

CREATE INDEX IF NOT EXISTS ix_tb_user_devices_user_active ON public.tb_user_devices (user_id) WHERE is_active;
CREATE INDEX IF NOT EXISTS ix_tb_user_devices_school      ON public.tb_user_devices (school_id);

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 2. Per-school settings
-- ---------------------------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tb_school_notification_settings
(
    school_id                  varchar(100) NOT NULL,
    fee_reminder_enabled       boolean      NOT NULL DEFAULT false,
    fee_reminder_send_time     time         NOT NULL DEFAULT '09:00',   -- India time
    fee_reminder_grace_days    smallint     NOT NULL DEFAULT 0,
    last_fee_reminder_run_date date,                                    -- India date of the last completed run
    updated_by                 bigint,
    created_at                 timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at                 timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_school_notification_settings_pkey PRIMARY KEY (school_id),
    CONSTRAINT fk_tb_school_notif_settings_school  FOREIGN KEY (school_id)  REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_school_notif_settings_updater FOREIGN KEY (updated_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_school_notif_settings_grace CHECK (fee_reminder_grace_days BETWEEN 0 AND 30)
);

-- ---------------------------------------------------------------------------------------------------------------------------------
-- 3. Log (also the idempotency record)
-- ---------------------------------------------------------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tb_push_notification_log
(
    notification_id    bigint GENERATED ALWAYS AS IDENTITY,
    school_id          varchar(100) NOT NULL,
    user_id            bigint,
    student_id         varchar(20),
    notification_type  varchar(30)  NOT NULL,                           -- v1: FEE_OVERDUE
    notify_date        date         NOT NULL,                           -- India date
    title              varchar(100) NOT NULL,
    body               varchar(300) NOT NULL,
    status             varchar(20)  NOT NULL DEFAULT 'PENDING',
    attempt_count      smallint     NOT NULL DEFAULT 0,
    devices_targeted   smallint     NOT NULL DEFAULT 0,
    devices_succeeded  smallint     NOT NULL DEFAULT 0,
    error_summary      varchar(500),
    created_at         timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    sent_at            timestamptz,
    CONSTRAINT tb_push_notification_log_pkey PRIMARY KEY (notification_id),
    CONSTRAINT uq_tb_push_log_student_type_day UNIQUE (student_id, notification_type, notify_date),
    CONSTRAINT fk_tb_push_log_school  FOREIGN KEY (school_id)  REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_push_log_user    FOREIGN KEY (user_id)    REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_push_log_student FOREIGN KEY (student_id) REFERENCES public.tb_students (student_id),
    CONSTRAINT chk_tb_push_log_status CHECK (status IN ('PENDING', 'SENT', 'FAILED', 'NO_DEVICE', 'DRY_RUN'))
);

CREATE INDEX IF NOT EXISTS ix_tb_push_log_school_date ON public.tb_push_notification_log (school_id, notify_date);

-- DRY_RUN = FCM validated the message but delivered nothing (Firebase:DryRun = true). A real run the same day treats it as not yet sent and updates it in place.
-- Idempotent: a table created by an earlier version of this script gets its CHECK replaced; one that already allows DRY_RUN is left alone.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_push_log_status' AND conrelid = 'public.tb_push_notification_log'::regclass
                   AND pg_get_constraintdef(oid) LIKE '%DRY_RUN%') THEN
        ALTER TABLE public.tb_push_notification_log DROP CONSTRAINT IF EXISTS chk_tb_push_log_status;
        ALTER TABLE public.tb_push_notification_log ADD CONSTRAINT chk_tb_push_log_status
            CHECK (status IN ('PENDING', 'SENT', 'FAILED', 'NO_DEVICE', 'DRY_RUN'));
    END IF;
END $$;

COMMIT;
