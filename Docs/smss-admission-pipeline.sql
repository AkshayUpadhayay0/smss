-- Admission pipeline: Admission Staff role + roles tiers on the EXISTING tb_admissions application flow + private application documents.
--
-- Extends tb_admissions (it already is the application pipeline: application number, review, approve / reject, cancel, confirm-creates-student). Nothing is
-- rebuilt and no existing row is changed. Adds:
--   * the ADMISSION_STAFF role in lut_roles
--   * tb_admissions: previous_school, review_notes, rejected_by, rejected_at (+ user foreign keys, the reject-reason rule and the converted-student lock)
--   * tb_admission_application_documents (private storage; the existing, unused tb_admission_documents is left exactly as it is)
-- Needs tb_admissions, tb_students, tb_users, tb_document_types, lut_status (with the ADMISSION_STATUS rows), lut_roles. Safe to run twice.
--
-- Decisions:
--  * Status stays status_id -> lut_status (ADMISSION_STATUS): APPLICATION_SUBMITTED = Submitted, UNDER_REVIEW = Under Review, APPROVED, REJECTED,
--    ADMITTED = converted to a student, CANCELLED = Withdrawn. No status text column is added.
--  * The Rejected rule can't be a plain CHECK on a status NAME, so (like the partial indexes elsewhere) the REJECTED / ADMITTED status ids are looked up
--    from lut_status when this file runs and put into the CHECK constraints.
--  * reviewed_by / approved_by already exist (and the 2 real rows point at a user id that no longer exists), so their foreign keys are added NOT VALID:
--    enforced for every new write, old rows are not re-checked. rejected_by is new.
--  * The converted-student lock: student_id set <=> status ADMITTED (an ADMITTED application can't change status; the app refuses every transition from it),
--    and one student can belong to at most one application.

BEGIN;

-- ---------------------------------------------------------------- role
INSERT INTO public.lut_roles (role_name, role_code, description, status_id)
SELECT 'Admission Staff', 'ADMISSION_STAFF', 'Staff who take admission applications: record applicants, move them to review, upload their documents and hand approved ones on for student creation. Cannot approve or reject.',
       (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')
WHERE NOT EXISTS (SELECT 1 FROM public.lut_roles WHERE role_code = 'ADMISSION_STAFF')
  AND EXISTS (SELECT 1 FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status');

-- ---------------------------------------------------------------- tb_admissions
ALTER TABLE public.tb_admissions ADD COLUMN IF NOT EXISTS previous_school varchar(200);
ALTER TABLE public.tb_admissions ADD COLUMN IF NOT EXISTS review_notes    text;
ALTER TABLE public.tb_admissions ADD COLUMN IF NOT EXISTS rejected_by     bigint;
ALTER TABLE public.tb_admissions ADD COLUMN IF NOT EXISTS rejected_at     timestamptz;

DO $$
DECLARE
    rejected_sid integer;
    admitted_sid integer;
BEGIN
    SELECT sid INTO rejected_sid FROM public.lut_status WHERE sname = 'REJECTED' AND stype = 'ADMISSION_STATUS';
    SELECT sid INTO admitted_sid FROM public.lut_status WHERE sname = 'ADMITTED' AND stype = 'ADMISSION_STATUS';
    IF rejected_sid IS NULL OR admitted_sid IS NULL THEN
        RAISE EXCEPTION 'lut_status has no REJECTED / ADMITTED row of type ADMISSION_STATUS';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_admissions_reviewed_by') THEN
        ALTER TABLE public.tb_admissions ADD CONSTRAINT fk_tb_admissions_reviewed_by FOREIGN KEY (reviewed_by) REFERENCES public.tb_users (user_id) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_admissions_approved_by') THEN
        ALTER TABLE public.tb_admissions ADD CONSTRAINT fk_tb_admissions_approved_by FOREIGN KEY (approved_by) REFERENCES public.tb_users (user_id) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_admissions_rejected_by') THEN
        ALTER TABLE public.tb_admissions ADD CONSTRAINT fk_tb_admissions_rejected_by FOREIGN KEY (rejected_by) REFERENCES public.tb_users (user_id);
    END IF;

    -- a REJECTED application must carry a non-blank reason (the same convention as leave's decision_reason)
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_admissions_reject_reason') THEN
        EXECUTE format('ALTER TABLE public.tb_admissions ADD CONSTRAINT chk_tb_admissions_reject_reason CHECK (status_id <> %s OR length(btrim(coalesce(rejection_reason, ''''))) > 0)', rejected_sid);
    END IF;
    -- the converted-student lock: a student is linked exactly when the application is ADMITTED
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_admissions_converted') THEN
        EXECUTE format('ALTER TABLE public.tb_admissions ADD CONSTRAINT chk_tb_admissions_converted CHECK ((status_id = %s) = (student_id IS NOT NULL))', admitted_sid);
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_admissions_student ON public.tb_admissions (student_id) WHERE student_id IS NOT NULL;

-- ---------------------------------------------------------------- application documents (private files; mirrors tb_employee_documents)
CREATE TABLE IF NOT EXISTS public.tb_admission_application_documents
(
    application_document_id bigint        GENERATED ALWAYS AS IDENTITY,
    school_id               varchar(100)  NOT NULL,
    admission_id            bigint        NOT NULL,
    document_type_id        bigint        NOT NULL,
    document_path           text          NOT NULL,        -- a key in the PRIVATE store, never a URL
    original_file_name      varchar(255)  NOT NULL,
    content_type            varchar(100)  NOT NULL,
    file_size               bigint        NOT NULL,
    remarks                 text,
    uploaded_by             bigint,
    status_id               integer       NOT NULL,        -- Active / Inactive (a removed document is hidden, never deleted)
    created_at              timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at              timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_admission_application_documents_pkey PRIMARY KEY (application_document_id),
    CONSTRAINT fk_tb_aad_school        FOREIGN KEY (school_id)        REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_aad_admission     FOREIGN KEY (admission_id)     REFERENCES public.tb_admissions (admission_id),
    CONSTRAINT fk_tb_aad_document_type FOREIGN KEY (document_type_id) REFERENCES public.tb_document_types (document_type_id),
    CONSTRAINT fk_tb_aad_uploaded_by   FOREIGN KEY (uploaded_by)      REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_aad_status        FOREIGN KEY (status_id)        REFERENCES public.lut_status (sid),
    CONSTRAINT chk_tb_aad_size         CHECK (file_size > 0)
);
CREATE INDEX IF NOT EXISTS idx_tb_aad_admission ON public.tb_admission_application_documents (school_id, admission_id);

COMMIT;
