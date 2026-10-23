-- Leave Management: employee and student leave requests (request / approve / reject / cancel only).
--
-- Adds: lut_leave_type (global, seeded), tb_employee_leave_requests, tb_student_leave_requests.
-- Needs tb_schools, tb_employees (smss-employees.sql), tb_students, tb_users. Run BEFORE starting the API build that contains these entities.
-- Safe to run twice.
--
-- Needs the `btree_gist` extension (ships with PostgreSQL's contrib; CREATE EXTENSION below needs a role allowed to create it, e.g. the
-- database owner on most hosts). It backs the "no two Approved leaves of one person overlap" rule at the database level.
--
-- Decisions:
--  * NO quota / balance / accrual / carry-forward of any kind: a request is just approved or not.
--  * `status` is a varchar with a CHECK (Pending / Approved / Rejected / Cancelled), the same style as the fee-payment status, not a lut_status row.
--  * approved_by / approved_at record who DECIDED (approved or rejected) and when. They are both NULL while Pending and for Cancelled.
--  * A Rejected request must carry a non-blank decision_reason (database check, same convention as attendance corrections / payment voids).
--  * Two lut_leave_type rows may share a name ("Sick Leave", "Other"): they apply to different applicants and are told apart by
--    type_code and the applies_to_* flags. That is intentional.
--  * Approving a request writes NOTHING to attendance or to class-teacher assignments: those stay separate systems.
--  * Whether a person's Approved leaves overlap is checked by the application (under a per-person lock) AND enforced by an exclusion constraint.

BEGIN;

CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE IF NOT EXISTS public.lut_leave_type
(
    leave_type_id        bigint       GENERATED ALWAYS AS IDENTITY,
    type_name            varchar(50)  NOT NULL,
    type_code            varchar(30)  NOT NULL,
    applies_to_employee  boolean      NOT NULL DEFAULT false,
    applies_to_student   boolean      NOT NULL DEFAULT false,
    is_active            boolean      NOT NULL DEFAULT true,
    created_at           timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT lut_leave_type_pkey PRIMARY KEY (leave_type_id),
    CONSTRAINT uq_lut_leave_type_code UNIQUE (type_code),
    CONSTRAINT uq_lut_leave_type_name_scope UNIQUE (type_name, applies_to_employee, applies_to_student),
    CONSTRAINT chk_lut_leave_type_applies CHECK (applies_to_employee OR applies_to_student)
);

INSERT INTO public.lut_leave_type (type_name, type_code, applies_to_employee, applies_to_student) VALUES
    ('Casual Leave',   'EMP_CASUAL',   true,  false),
    ('Sick Leave',     'EMP_SICK',     true,  false),
    ('Earned Leave',   'EMP_EARNED',   true,  false),
    ('Unpaid Leave',   'EMP_UNPAID',   true,  false),
    ('Other',          'EMP_OTHER',    true,  false),
    ('Sick Leave',     'STU_SICK',     false, true),
    ('Personal Leave', 'STU_PERSONAL', false, true),
    ('Other',          'STU_OTHER',    false, true)
ON CONFLICT (type_code) DO NOTHING;

CREATE TABLE IF NOT EXISTS public.tb_employee_leave_requests
(
    leave_request_id bigint        GENERATED ALWAYS AS IDENTITY,
    school_id        varchar(100)  NOT NULL,
    employee_id      varchar(20)   NOT NULL,
    leave_type_id    bigint        NOT NULL,
    from_date        date          NOT NULL,
    to_date          date          NOT NULL,
    reason           text          NOT NULL,
    status           varchar(20)   NOT NULL DEFAULT 'Pending',
    applied_by       bigint        NOT NULL,      -- tb_users.user_id of whoever recorded the request
    applied_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    approved_by      bigint,                      -- tb_users.user_id of whoever approved OR rejected it
    approved_at      timestamptz,
    decision_reason  text,
    created_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_employee_leave_requests_pkey PRIMARY KEY (leave_request_id),
    CONSTRAINT fk_tb_elr_school      FOREIGN KEY (school_id)     REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_elr_employee    FOREIGN KEY (employee_id)   REFERENCES public.tb_employees (employee_id),
    CONSTRAINT fk_tb_elr_type        FOREIGN KEY (leave_type_id) REFERENCES public.lut_leave_type (leave_type_id),
    CONSTRAINT fk_tb_elr_applied_by  FOREIGN KEY (applied_by)    REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_elr_approved_by FOREIGN KEY (approved_by)   REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_elr_dates      CHECK (to_date >= from_date),
    CONSTRAINT chk_tb_elr_status     CHECK (status IN ('Pending', 'Approved', 'Rejected', 'Cancelled')),
    CONSTRAINT chk_tb_elr_reason     CHECK (length(btrim(reason)) > 0),
    CONSTRAINT chk_tb_elr_decision   CHECK ((status IN ('Pending', 'Cancelled') AND approved_by IS NULL AND approved_at IS NULL)
                                            OR (status IN ('Approved', 'Rejected') AND approved_by IS NOT NULL AND approved_at IS NOT NULL)),
    CONSTRAINT chk_tb_elr_reject_reason CHECK (status <> 'Rejected' OR length(btrim(coalesce(decision_reason, ''))) > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_elr_school_status ON public.tb_employee_leave_requests (school_id, status);
CREATE INDEX IF NOT EXISTS idx_tb_elr_employee      ON public.tb_employee_leave_requests (school_id, employee_id);

CREATE TABLE IF NOT EXISTS public.tb_student_leave_requests
(
    leave_request_id bigint        GENERATED ALWAYS AS IDENTITY,
    school_id        varchar(100)  NOT NULL,
    student_id       varchar(20)   NOT NULL,
    leave_type_id    bigint        NOT NULL,
    from_date        date          NOT NULL,
    to_date          date          NOT NULL,
    reason           text          NOT NULL,
    status           varchar(20)   NOT NULL DEFAULT 'Pending',
    applied_by       bigint        NOT NULL,
    applied_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    approved_by      bigint,
    approved_at      timestamptz,
    decision_reason  text,
    created_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at       timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_student_leave_requests_pkey PRIMARY KEY (leave_request_id),
    CONSTRAINT fk_tb_slr_school      FOREIGN KEY (school_id)     REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_slr_student     FOREIGN KEY (student_id)    REFERENCES public.tb_students (student_id),
    CONSTRAINT fk_tb_slr_type        FOREIGN KEY (leave_type_id) REFERENCES public.lut_leave_type (leave_type_id),
    CONSTRAINT fk_tb_slr_applied_by  FOREIGN KEY (applied_by)    REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_slr_approved_by FOREIGN KEY (approved_by)   REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_slr_dates      CHECK (to_date >= from_date),
    CONSTRAINT chk_tb_slr_status     CHECK (status IN ('Pending', 'Approved', 'Rejected', 'Cancelled')),
    CONSTRAINT chk_tb_slr_reason     CHECK (length(btrim(reason)) > 0),
    CONSTRAINT chk_tb_slr_decision   CHECK ((status IN ('Pending', 'Cancelled') AND approved_by IS NULL AND approved_at IS NULL)
                                            OR (status IN ('Approved', 'Rejected') AND approved_by IS NOT NULL AND approved_at IS NOT NULL)),
    CONSTRAINT chk_tb_slr_reject_reason CHECK (status <> 'Rejected' OR length(btrim(coalesce(decision_reason, ''))) > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_slr_school_status ON public.tb_student_leave_requests (school_id, status);
CREATE INDEX IF NOT EXISTS idx_tb_slr_student       ON public.tb_student_leave_requests (school_id, student_id);

-- Two Approved leaves of one person can never overlap (inclusive date ranges). Pending / Rejected / Cancelled requests are unrestricted.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_tb_elr_no_approved_overlap') THEN
        ALTER TABLE public.tb_employee_leave_requests
            ADD CONSTRAINT ex_tb_elr_no_approved_overlap
            EXCLUDE USING gist (school_id WITH =, employee_id WITH =, daterange(from_date, to_date, '[]') WITH &&)
            WHERE (status = 'Approved');
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_tb_slr_no_approved_overlap') THEN
        ALTER TABLE public.tb_student_leave_requests
            ADD CONSTRAINT ex_tb_slr_no_approved_overlap
            EXCLUDE USING gist (school_id WITH =, student_id WITH =, daterange(from_date, to_date, '[]') WITH &&)
            WHERE (status = 'Approved');
    END IF;
END $$;

COMMIT;
