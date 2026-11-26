-- =====================================================================================================================
-- RUN-ME: Student status and leaving, stage 8b = TRANSFER CERTIFICATES  (one self-contained file for pgAdmin's Query Tool; single transaction)
-- Design: the Stage 8 decisions of 26 Nov 2026 (number <school code>/TC/<academic year>/<4-digit number>, never reused; an immutable snapshot of what was printed; duplicates keep the number).
--
-- WHAT IT DOES (three NEW tables; no existing table or row is touched)
--   1. tb_transfer_certificate_sequences  one counter per school and academic-year label. A number is taken with an atomic "+1" under a per-school advisory lock; the counter never goes down,
--      so a voided certificate's number is never handed out again.
--   2. tb_transfer_certificates  one row per certificate: the number, the enrolment it is for, ISSUED or VOID, the snapshot (jsonb) of EVERYTHING printed at issue time, the conduct and remarks entered at
--      issue, whether fees were cleared, the dues override (reason of at least 10 characters) and the amount, who and when issued, who and when and why voided. A row is never deleted.
--      UNIQUE (school_id, certificate_number); at most ONE ISSUED certificate per enrolment (a voided one does not count: a reissue is a new number).
--   3. tb_transfer_certificate_copies  one audit row per print: the original or each duplicate ("DUPLICATE COPY No. N", numbered per certificate), who and when.
--
-- PRE-CHECK (violators): none possible. The three tables are new and empty. The script checks that the 8a columns exist and prints how many enrolments exist; it verifies that every existing
--   enrolment is identical afterwards (fingerprint).
--
-- BEFORE YOU RUN IT
--   1. TAKE A pg_dump BACKUP FIRST, e.g.:  pg_dump -h localhost -U postgres -Fc -f smss-db-before-st-8b.dump smss-db
--   2. Make sure you are connected to the RIGHT database: the script prints current_database() first.
--   DEPLOY ORDER: after the 8a script, BEFORE the API build that contains Stage 8b (the 8a correction check also reads tb_transfer_certificates from that build on).
--
-- RE-RUNNING IT
--   Safe: a second run changes nothing and prints "ALREADY MIGRATED: nothing changed".
--
-- HOW TO ROLL BACK (only while no certificate has been issued)
--   DROP TABLE tb_transfer_certificate_copies; DROP TABLE tb_transfer_certificates; DROP TABLE tb_transfer_certificate_sequences;
-- =====================================================================================================================

BEGIN;

CREATE TEMP TABLE _st8b_state  (migrating boolean NOT NULL) ON COMMIT DROP;
CREATE TEMP TABLE _st8b_before (enrolments bigint, fingerprint text) ON COMMIT DROP;

DO $st8b$
DECLARE
    v_mig boolean;
BEGIN
    RAISE NOTICE 'Connected to database: %   (server port %, user %)', current_database(), inet_server_port(), current_user;
    RAISE NOTICE 'If this is not the database you meant to change, run ROLLBACK now.';

    IF to_regclass('public.tb_student_enrollments') IS NULL OR to_regclass('public.tb_students') IS NULL OR to_regclass('public.tb_users') IS NULL OR to_regclass('public.tb_schools') IS NULL THEN
        RAISE EXCEPTION 'A table the certificates hang on is missing (enrolments, students, users or schools). Nothing was changed.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'tb_student_enrollments' AND column_name = 'leaving_date') THEN
        RAISE EXCEPTION 'tb_student_enrollments.leaving_date (stage 8a) is missing. Run the 8a script first. Nothing was changed.';
    END IF;

    v_mig := NOT (to_regclass('public.tb_transfer_certificate_sequences') IS NOT NULL AND to_regclass('public.tb_transfer_certificates') IS NOT NULL AND to_regclass('public.tb_transfer_certificate_copies') IS NOT NULL);
    INSERT INTO _st8b_state VALUES (v_mig);
    IF NOT v_mig THEN
        RAISE NOTICE 'ALREADY MIGRATED: nothing changed';
        RETURN;
    END IF;

    INSERT INTO _st8b_before
    SELECT count(*), coalesce(md5(string_agg(concat_ws('|', enrollment_id, school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id, outcome, outcome_at,
                                                       leaving_date, leaving_reason, outcome_by, created_at, updated_at), E'\n' ORDER BY enrollment_id)), '')
      FROM public.tb_student_enrollments;
    RAISE NOTICE 'PRE-CHECK: % enrolments exist; no violators are possible (the three tables are new).', (SELECT enrolments FROM _st8b_before);
END
$st8b$;

-- ------------------------------------------------------------------------------------------------ 1. the counter
CREATE TABLE IF NOT EXISTS public.tb_transfer_certificate_sequences (
    school_id    varchar(100) NOT NULL,
    year_label   varchar(50)  NOT NULL,                       -- the academic year of the enrolment, e.g. 2026-27
    last_number  integer      NOT NULL DEFAULT 0,
    updated_at   timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_tb_transfer_certificate_sequences PRIMARY KEY (school_id, year_label),
    CONSTRAINT fk_tb_tc_sequences_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT chk_tb_tc_sequences_number CHECK (last_number >= 0)
);

-- ------------------------------------------------------------------------------------------------ 2. the certificates
CREATE TABLE IF NOT EXISTS public.tb_transfer_certificates (
    certificate_id      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    school_id           varchar(100)  NOT NULL,
    student_id          varchar(20)   NOT NULL,
    enrollment_id       bigint        NOT NULL,
    certificate_number  varchar(80)   NOT NULL,                -- <school code>/TC/<academic year>/<4-digit number>
    year_label          varchar(50)   NOT NULL,
    sequence_no         integer       NOT NULL,
    status              varchar(10)   NOT NULL DEFAULT 'ISSUED',
    snapshot            jsonb         NOT NULL,                -- everything printed, as at issue time; every later copy is drawn from THIS
    conduct             varchar(100)  NOT NULL,
    remarks             varchar(500)  NULL,
    fees_cleared        boolean       NOT NULL,
    override_applied    boolean       NOT NULL DEFAULT false,
    override_reason     varchar(500)  NULL,
    outstanding_amount  numeric(12,2) NOT NULL DEFAULT 0,
    issued_by           bigint        NOT NULL,
    issued_at           timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    voided_by           bigint        NULL,
    voided_at           timestamptz   NULL,
    void_reason         varchar(500)  NULL,
    CONSTRAINT fk_tb_transfer_certificates_school     FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_transfer_certificates_student    FOREIGN KEY (student_id, school_id) REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_transfer_certificates_enrolment  FOREIGN KEY (school_id, enrollment_id) REFERENCES public.tb_student_enrollments (school_id, enrollment_id),
    CONSTRAINT fk_tb_transfer_certificates_issued_by  FOREIGN KEY (issued_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_transfer_certificates_voided_by  FOREIGN KEY (voided_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT uq_tb_transfer_certificates_number     UNIQUE (school_id, certificate_number),
    CONSTRAINT uq_tb_transfer_certificates_school_cert UNIQUE (school_id, certificate_id),
    CONSTRAINT chk_tb_transfer_certificates_status    CHECK (status IN ('ISSUED', 'VOID')),
    CONSTRAINT chk_tb_transfer_certificates_void      CHECK ((status = 'VOID') = (voided_at IS NOT NULL AND voided_by IS NOT NULL AND void_reason IS NOT NULL AND length(btrim(void_reason)) >= 10)),
    CONSTRAINT chk_tb_transfer_certificates_override  CHECK ((NOT override_applied AND override_reason IS NULL) OR (override_applied AND override_reason IS NOT NULL AND length(btrim(override_reason)) >= 10)),
    CONSTRAINT chk_tb_transfer_certificates_amount    CHECK (outstanding_amount >= 0),
    CONSTRAINT chk_tb_transfer_certificates_seq       CHECK (sequence_no > 0)
);
-- one certificate per leaving: a VOID one does not count (a reissue is a new number)
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_transfer_certificates_one_issued ON public.tb_transfer_certificates (school_id, enrollment_id) WHERE status = 'ISSUED';
CREATE INDEX IF NOT EXISTS idx_tb_transfer_certificates_school_student ON public.tb_transfer_certificates (school_id, student_id);
CREATE INDEX IF NOT EXISTS idx_tb_transfer_certificates_school_issued ON public.tb_transfer_certificates (school_id, issued_at DESC);

-- ------------------------------------------------------------------------------------------------ 3. every print
CREATE TABLE IF NOT EXISTS public.tb_transfer_certificate_copies (
    copy_id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    school_id       varchar(100) NOT NULL,
    certificate_id  bigint       NOT NULL,
    kind            varchar(10)  NOT NULL,                    -- ORIGINAL (0 = the issue itself, or a re-print of the original) | DUPLICATE (1, 2, 3 ...)
    copy_no         integer      NOT NULL,
    printed_by      bigint       NOT NULL,
    printed_at      timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_tb_tc_copies_certificate FOREIGN KEY (school_id, certificate_id) REFERENCES public.tb_transfer_certificates (school_id, certificate_id),
    CONSTRAINT fk_tb_tc_copies_user        FOREIGN KEY (printed_by) REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_tc_copies_kind       CHECK ((kind = 'ORIGINAL' AND copy_no = 0) OR (kind = 'DUPLICATE' AND copy_no > 0))
);
CREATE INDEX IF NOT EXISTS idx_tb_tc_copies_certificate ON public.tb_transfer_certificate_copies (school_id, certificate_id);
-- a duplicate number is used once per certificate (the service takes the next one under a lock; this is the net under it)
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_tc_copies_duplicate_no ON public.tb_transfer_certificate_copies (school_id, certificate_id, copy_no) WHERE kind = 'DUPLICATE';

-- ------------------------------------------------------------------------------------------------ verification
DO $st8bv$
DECLARE
    b _st8b_before;
    f text;
BEGIN
    IF NOT (SELECT migrating FROM _st8b_state) THEN RETURN; END IF;
    SELECT * INTO b FROM _st8b_before;
    SELECT coalesce(md5(string_agg(concat_ws('|', enrollment_id, school_id, student_id, academic_year_id, class_section_id, roll_number, enrollment_date, admission_type_id, status_id, outcome, outcome_at,
                                             leaving_date, leaving_reason, outcome_by, created_at, updated_at), E'\n' ORDER BY enrollment_id)), '')
      INTO f FROM public.tb_student_enrollments;
    IF (SELECT count(*) FROM public.tb_student_enrollments) <> b.enrolments OR f <> b.fingerprint THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: an enrolment changed. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM public.tb_transfer_certificates) <> 0 OR (SELECT count(*) FROM public.tb_transfer_certificate_copies) <> 0 OR (SELECT count(*) FROM public.tb_transfer_certificate_sequences) <> 0 THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a certificate row exists after migrating. Everything is rolled back.';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_transfer_certificates'::regclass AND contype = 'f') <> 5
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_transfer_certificates'::regclass AND contype = 'c') <> 5
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_transfer_certificate_copies'::regclass AND contype = 'f') <> 2
       OR (SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.tb_transfer_certificate_copies'::regclass AND contype = 'c') <> 1
       OR NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'uq_tb_transfer_certificates_one_issued')
       OR NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'uq_tb_tc_copies_duplicate_no') THEN
        RAISE EXCEPTION 'VERIFICATION FAILED: a constraint or index is missing. Everything is rolled back.';
    END IF;
    RAISE NOTICE 'VERIFIED: % enrolments unchanged (fingerprint identical); the three certificate tables exist, empty, with their foreign keys, checks and unique indexes.', b.enrolments;
END
$st8bv$;

COMMIT;

SELECT (SELECT count(*) FROM public.tb_transfer_certificates) AS certificates;
