-- Academic Calendar: school events (holidays, exams, PTMs, staff meetings, ...) shown on a month calendar.
--
-- Adds: lut_event_category (global, seeded, each with a display colour) and tb_school_events.
-- Needs tb_schools, tb_academic_years, tb_users, lut_status. Run BEFORE starting the API build that contains these entities. Safe to run twice.
--
-- Decisions:
--  * An event belongs to one academic year (its dates must fall inside that year; the app checks it). It may span several days (start_date .. end_date).
--  * All-day events carry no times; a timed event carries BOTH start_time and end_time. On a single day the end must be after the start;
--    on a multi-day event the times are just the first day's start and the last day's end.
--  * "Delete" is status_id -> Inactive (the project's hide-row pattern). Nothing is ever removed.
--  * Every role of the school sees every event: there is no audience targeting yet.

BEGIN;

CREATE TABLE IF NOT EXISTS public.lut_event_category
(
    event_category_id bigint       GENERATED ALWAYS AS IDENTITY,
    category_name     varchar(50)  NOT NULL,
    category_code     varchar(30)  NOT NULL,
    color_hex         varchar(7)   NOT NULL,
    is_active         boolean      NOT NULL DEFAULT true,
    created_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT lut_event_category_pkey PRIMARY KEY (event_category_id),
    CONSTRAINT uq_lut_event_category_code UNIQUE (category_code),
    CONSTRAINT uq_lut_event_category_name UNIQUE (category_name),
    CONSTRAINT chk_lut_event_category_color CHECK (color_hex ~ '^#[0-9A-Fa-f]{6}$')
);

INSERT INTO public.lut_event_category (category_name, category_code, color_hex) VALUES
    ('Holiday',       'HOLIDAY',       '#22c55e'),
    ('Exam',          'EXAM',          '#ef4444'),
    ('PTM',           'PTM',           '#f59e0b'),
    ('Staff Meeting', 'STAFF_MEETING', '#3b82f6'),
    ('School Event',  'SCHOOL_EVENT',  '#8b5cf6'),
    ('Other',         'OTHER',         '#6b7280')
ON CONFLICT (category_code) DO NOTHING;

CREATE TABLE IF NOT EXISTS public.tb_school_events
(
    event_id          bigint        GENERATED ALWAYS AS IDENTITY,
    school_id         varchar(100)  NOT NULL,
    academic_year_id  bigint        NOT NULL,
    event_title       varchar(200)  NOT NULL,
    start_date        date          NOT NULL,
    end_date          date          NOT NULL,      -- the app fills it with start_date when the client leaves it out
    is_all_day        boolean       NOT NULL DEFAULT true,
    start_time        time,
    end_time          time,
    event_category_id bigint        NOT NULL,
    description       text,
    created_by        bigint        NOT NULL,      -- tb_users.user_id, from the token
    status_id         integer       NOT NULL,      -- Active / Inactive (a deleted event is hidden, never removed)
    created_at        timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_school_events_pkey PRIMARY KEY (event_id),
    CONSTRAINT fk_tb_events_school   FOREIGN KEY (school_id)         REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_events_year     FOREIGN KEY (academic_year_id)  REFERENCES public.tb_academic_years (academic_year_id),
    CONSTRAINT fk_tb_events_category FOREIGN KEY (event_category_id) REFERENCES public.lut_event_category (event_category_id),
    CONSTRAINT fk_tb_events_creator  FOREIGN KEY (created_by)        REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_events_status   FOREIGN KEY (status_id)         REFERENCES public.lut_status (sid),
    CONSTRAINT chk_tb_events_dates   CHECK (end_date >= start_date),
    CONSTRAINT chk_tb_events_times   CHECK ((is_all_day AND start_time IS NULL AND end_time IS NULL)
                                            OR (NOT is_all_day AND start_time IS NOT NULL AND end_time IS NOT NULL)),
    CONSTRAINT chk_tb_events_same_day_times CHECK (is_all_day OR end_date > start_date OR end_time > start_time),
    CONSTRAINT chk_tb_events_title   CHECK (length(btrim(event_title)) > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_events_school_range ON public.tb_school_events (school_id, start_date, end_date);
CREATE INDEX IF NOT EXISTS idx_tb_events_year         ON public.tb_school_events (school_id, academic_year_id);

COMMIT;
