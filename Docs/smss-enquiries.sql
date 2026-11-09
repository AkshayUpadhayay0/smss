-- Public enquiries (v1): "Contact Us" messages and "Request a Demo" leads from the public marketing site, handled by the Super Admin.
--
-- Adds: tb_enquiries. PLATFORM-level table: it deliberately has NO school_id (a lead is not a tenant's data).
-- Needs tb_users. Run BEFORE starting the API build that contains this entity. Safe to run twice.
--
-- Decisions:
--  * One table for both flows; enquiry_type is 'CONTACT' or 'DEMO' (a CHECK).
--  * status is a string code (NEW, CONTACTED, DEMO_SCHEDULED, CLOSED, SPAM), not lut_status: lut_status only holds Active / Inactive.
--    The codes are constants in the API (EnquiryStatuses); no ids are hard-coded anywhere.
--  * handled_by_user_id is set by the API from the signed-in Super Admin's token, never from a request.
--  * Public submissions are rate limited in the API; ip_address / user_agent are kept for abuse review only.

BEGIN;

CREATE TABLE IF NOT EXISTS public.tb_enquiries
(
    enquiry_id           bigint        GENERATED ALWAYS AS IDENTITY,
    enquiry_type         varchar(20)   NOT NULL,
    full_name            varchar(150)  NOT NULL,
    designation          varchar(100),
    school_name          varchar(200),
    email                varchar(200)  NOT NULL,
    phone                varchar(30),
    city                 varchar(100),
    student_count_range  varchar(30),
    subject              varchar(200),
    message              text,
    preferred_demo_date  date,
    preferred_time_slot  varchar(50),
    consent_given        boolean       NOT NULL DEFAULT FALSE,
    status               varchar(20)   NOT NULL DEFAULT 'NEW',
    admin_notes          text,
    handled_by_user_id   bigint,
    ip_address           varchar(45),
    user_agent           varchar(500),
    created_at           timestamptz   NOT NULL DEFAULT now(),
    updated_at           timestamptz,
    CONSTRAINT tb_enquiries_pkey PRIMARY KEY (enquiry_id),
    CONSTRAINT fk_tb_enquiries_handler FOREIGN KEY (handled_by_user_id) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_enquiries_type   CHECK (enquiry_type IN ('CONTACT', 'DEMO')),
    CONSTRAINT chk_tb_enquiries_status CHECK (status IN ('NEW', 'CONTACTED', 'DEMO_SCHEDULED', 'CLOSED', 'SPAM'))
);

CREATE INDEX IF NOT EXISTS idx_tb_enquiries_status_created ON public.tb_enquiries (status, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_tb_enquiries_type_created   ON public.tb_enquiries (enquiry_type, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_tb_enquiries_email          ON public.tb_enquiries (email);

COMMIT;
