-- =====================================================================================================================
-- RUN-ME: Student and admission changes, stage 2d = STUDENT DOCUMENTS  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: Docs/admission-docs-latefee-registration-design.md (section 3), with the owner's decisions of 24 Nov 2026.
--
-- THE RULES
--   * A school marks a document type "required" (tb_document_types.is_required, applies_to STUDENT or BOTH: the existing Document Types screen). The same list applies to ALL students.
--   * The requirement applies to students admitted ON OR AFTER the date it was switched on (tb_document_types.required_from), so existing students are never flagged all at once.
--   * Per student and required type the status is SUBMITTED, WAIVED or PENDING. PENDING is DERIVED (no row), never stored. There is no OVERDUE and no "days to submit".
--   * A waiver needs a reason and records who waived it and when. Voiding a document hides it (the file and the row stay); nothing is hard-deleted.
--   * Documents NEVER block an admission or any other flow.
--
-- WHAT IT DOES
--   1. tb_document_types.required_from  date NULL. Invariant (check): required_from is filled exactly when is_required is true. Existing required types (none today) get today's date
--      (India time), so no existing student is flagged.
--   2. tb_student_documents  (new table): one ACTIVE row per student and document type (partial unique index), school_id on every row, a SUBMITTED row carries the private-storage key of the
--      file, a WAIVED row carries the reason; voided_* columns instead of a delete; created_by / created_at / updated_at audit; source_application_document_id remembers which application
--      document a carried-over row came from (unique, so a carry-over can never run twice).
--   Pre-check (raises, nothing changed): none needed: the table is new and the new column is derived from is_required.
--   Verification (raises, everything rolls back, on any mismatch): the document-type row count is unchanged, every required type has required_from and no other type has it, the new table
--   exists and is empty, and its checks and indexes exist.
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-2d.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK
--   DROP TABLE public.tb_student_documents;  ALTER TABLE public.tb_document_types DROP CONSTRAINT chk_tb_document_types_required_from, DROP COLUMN required_from;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st2d_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st2d_before (doc_types bigint, required_types bigint) ON COMMIT DROP;

DO $st2d$
DECLARE
    v_mig boolean;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    IF to_regclass('public.tb_document_types') IS NULL OR to_regclass('public.tb_students') IS NULL OR to_regclass('public.tb_users') IS NULL
       OR to_regclass('public.tb_admission_application_documents') IS NULL THEN
        RAISE EXCEPTION 'A required table (tb_document_types, tb_students, tb_users or tb_admission_application_documents) does not exist. Nothing was changed.';
    END IF;

    v_mig := NOT (
        EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_document_types' AND column_name = 'required_from')
        AND EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_document_types_required_from' AND conrelid = 'public.tb_document_types'::regclass)
        AND to_regclass('public.tb_student_documents') IS NOT NULL
        AND to_regclass('public.uq_tb_student_documents_active') IS NOT NULL);
    INSERT INTO _st2d_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;
    RAISE NOTICE 'No pre-check needed: the table is new and required_from is derived from is_required.';

    INSERT INTO _st2d_before SELECT count(*), count(*) FILTER (WHERE is_required) FROM public.tb_document_types;
END
$st2d$;

ALTER TABLE public.tb_document_types ADD COLUMN IF NOT EXISTS required_from date NULL;

DO $st2dm$
BEGIN
    IF NOT (SELECT migrating FROM _st2d_state) THEN RETURN; END IF;

    -- a type that is already required starts being required today (India time): no existing student is flagged
    UPDATE public.tb_document_types SET required_from = (now() AT TIME ZONE 'Asia/Kolkata')::date WHERE is_required AND required_from IS NULL;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_tb_document_types_required_from' AND conrelid = 'public.tb_document_types'::regclass) THEN
        ALTER TABLE public.tb_document_types ADD CONSTRAINT chk_tb_document_types_required_from CHECK (is_required = (required_from IS NOT NULL));
    END IF;

    CREATE TABLE IF NOT EXISTS public.tb_student_documents (
        student_document_id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
        school_id                      varchar(100) NOT NULL,
        student_id                     varchar(20)  NOT NULL,
        document_type_id               bigint       NOT NULL,
        status                         varchar(10)  NOT NULL,
        document_path                  text         NULL,
        original_file_name             varchar(255) NULL,
        content_type                   varchar(100) NULL,
        file_size                      bigint       NULL,
        remarks                        text         NULL,
        waiver_reason                  text         NULL,
        waived_by                      bigint       NULL,
        waived_at                      timestamptz  NULL,
        source_application_document_id bigint       NULL,
        voided_at                      timestamptz  NULL,
        voided_by                      bigint       NULL,
        void_reason                    text         NULL,
        created_by                     bigint       NULL,
        created_at                     timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        updated_at                     timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
        CONSTRAINT fk_tb_student_documents_school   FOREIGN KEY (school_id)        REFERENCES public.tb_schools (school_id),
        CONSTRAINT fk_tb_student_documents_student  FOREIGN KEY (student_id)       REFERENCES public.tb_students (student_id),
        CONSTRAINT fk_tb_student_documents_type     FOREIGN KEY (document_type_id) REFERENCES public.tb_document_types (document_type_id),
        CONSTRAINT fk_tb_student_documents_source   FOREIGN KEY (source_application_document_id) REFERENCES public.tb_admission_application_documents (application_document_id),
        CONSTRAINT fk_tb_student_documents_created  FOREIGN KEY (created_by)       REFERENCES public.tb_users (user_id),
        CONSTRAINT fk_tb_student_documents_waived   FOREIGN KEY (waived_by)        REFERENCES public.tb_users (user_id),
        CONSTRAINT fk_tb_student_documents_voided   FOREIGN KEY (voided_by)        REFERENCES public.tb_users (user_id),
        CONSTRAINT chk_tb_student_documents_status  CHECK (status IN ('SUBMITTED', 'WAIVED')),
        -- a SUBMITTED row carries its file; a WAIVED row carries the reason and who/when and no file
        CONSTRAINT chk_tb_student_documents_submitted CHECK (status <> 'SUBMITTED' OR (document_path IS NOT NULL AND original_file_name IS NOT NULL AND content_type IS NOT NULL AND file_size > 0)),
        CONSTRAINT chk_tb_student_documents_waived CHECK (status <> 'WAIVED' OR (length(btrim(COALESCE(waiver_reason, ''))) > 0 AND waived_by IS NOT NULL AND waived_at IS NOT NULL AND document_path IS NULL)),
        CONSTRAINT chk_tb_student_documents_voided CHECK ((voided_at IS NULL) = (voided_by IS NULL))
    );

    -- ONE active (not voided) row per student and document type
    CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_student_documents_active ON public.tb_student_documents (school_id, student_id, document_type_id) WHERE voided_at IS NULL;
    -- a carried-over application document can only be carried once
    CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_student_documents_source ON public.tb_student_documents (source_application_document_id) WHERE source_application_document_id IS NOT NULL;
    CREATE INDEX IF NOT EXISTS idx_tb_student_documents_student ON public.tb_student_documents (school_id, student_id);
END
$st2dm$;

DO $st2dv$
DECLARE
    b _st2d_before; n bigint;
BEGIN
    IF NOT (SELECT migrating FROM _st2d_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st2d_before;
    SELECT count(*) INTO n FROM public.tb_document_types;
    IF n <> b.doc_types THEN RAISE EXCEPTION 'VERIFICATION FAILED: the number of document types changed. Everything is rolled back.'; END IF;
    IF EXISTS (SELECT 1 FROM public.tb_document_types WHERE is_required <> (required_from IS NOT NULL)) THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: required_from does not match is_required. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM public.tb_document_types WHERE required_from IS NOT NULL) <> b.required_types THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: the number of types with a required_from differs from the number of required types. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM public.tb_student_documents) <> 0 THEN RAISE EXCEPTION 'VERIFICATION FAILED: tb_student_documents is not empty. Everything is rolled back.'; END IF;
    IF to_regclass('public.uq_tb_student_documents_active') IS NULL OR to_regclass('public.uq_tb_student_documents_source') IS NULL THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an index is missing. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_student_documents'::regclass AND contype = 'c') <> 4 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: the four checks of tb_student_documents are not all there. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % document types before and after (% required, each with required_from); tb_student_documents created empty with its checks and indexes.', n, b.required_types;
END
$st2dv$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_document_types) AS document_types, (SELECT count(*) FROM public.tb_document_types WHERE is_required) AS required_types,
       (SELECT count(*) FROM public.tb_student_documents) AS student_documents;
