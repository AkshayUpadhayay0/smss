-- Fee Setup module (Docs/roles-workflow.md section 27): fee types, fee frequencies, fee structures (per class per
-- academic year) with their line items, and fee concessions.
--
-- This project keeps its schema as plain SQL files in Docs/ (there is no EF Core migrations folder), so this file IS
-- the migration. Run it BEFORE starting the API build that contains the Fee Setup entities. Safe to run twice.
-- The deferred student tables (tb_student_fee_structure, tb_student_fee_concessions) are NOT part of this file.

BEGIN;

-- 1. tb_fee_types (school-owned master)
CREATE TABLE IF NOT EXISTS public.tb_fee_types
(
    fee_type_id    bigint       GENERATED ALWAYS AS IDENTITY,
    school_id      varchar(100) NOT NULL,
    fee_type_name  varchar(100) NOT NULL,
    fee_type_code  varchar(30)  NOT NULL,
    description    text,
    isactive       boolean      NOT NULL DEFAULT true,
    created_at     timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at     timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_fee_types_pkey PRIMARY KEY (fee_type_id),
    CONSTRAINT fk_tb_fee_types_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT uq_tb_fee_types_school_code UNIQUE (school_id, fee_type_code)
);
CREATE INDEX IF NOT EXISTS idx_tb_fee_types_school_id ON public.tb_fee_types (school_id);

-- 2. lut_fee_frequency (global lookup, small fixed list)
CREATE TABLE IF NOT EXISTS public.lut_fee_frequency
(
    frequency_id    bigint      GENERATED ALWAYS AS IDENTITY,
    frequency_name  varchar(50) NOT NULL,
    frequency_code  varchar(30) NOT NULL,
    isactive        boolean     NOT NULL DEFAULT true,
    CONSTRAINT lut_fee_frequency_pkey PRIMARY KEY (frequency_id),
    CONSTRAINT uq_lut_fee_frequency_code UNIQUE (frequency_code)
);

-- One row at a time, in display order, so the ids (and therefore the list order) are always One Time ... Annual
INSERT INTO public.lut_fee_frequency (frequency_name, frequency_code, isactive)
SELECT v.name, v.code, true
FROM (VALUES (1, 'One Time', 'ONE_TIME'),
             (2, 'Monthly', 'MONTHLY'),
             (3, 'Quarterly', 'QUARTERLY'),
             (4, 'Half-Yearly', 'HALF_YEARLY'),
             (5, 'Annual', 'ANNUAL')) AS v(ord, name, code)
WHERE NOT EXISTS (SELECT 1 FROM public.lut_fee_frequency f WHERE f.frequency_code = v.code)
ORDER BY v.ord;

-- 3. tb_fee_structures (header: one per class per academic year per school)
CREATE TABLE IF NOT EXISTS public.tb_fee_structures
(
    fee_structure_id  bigint       GENERATED ALWAYS AS IDENTITY,
    school_id         varchar(100) NOT NULL,
    academic_year_id  bigint       NOT NULL,
    class_id          bigint       NOT NULL,
    structure_name    varchar(150) NOT NULL,
    isactive          boolean      NOT NULL DEFAULT true,
    created_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        timestamptz  NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_fee_structures_pkey PRIMARY KEY (fee_structure_id),
    CONSTRAINT fk_tb_fee_structures_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_fee_structures_year   FOREIGN KEY (academic_year_id) REFERENCES public.tb_academic_years (academic_year_id),
    CONSTRAINT fk_tb_fee_structures_class  FOREIGN KEY (class_id) REFERENCES public.tb_classes (class_id),
    CONSTRAINT uq_tb_fee_structures_school_year_class UNIQUE (school_id, academic_year_id, class_id)
);
CREATE INDEX IF NOT EXISTS idx_tb_fee_structures_school_id ON public.tb_fee_structures (school_id);

-- 4. tb_fee_structure_details (line items; deleted together with their structure)
CREATE TABLE IF NOT EXISTS public.tb_fee_structure_details
(
    fee_structure_detail_id  bigint        GENERATED ALWAYS AS IDENTITY,
    fee_structure_id         bigint        NOT NULL,
    fee_type_id              bigint        NOT NULL,
    frequency_id             bigint        NOT NULL,
    amount                   numeric(12,2) NOT NULL,
    created_at               timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at               timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_fee_structure_details_pkey PRIMARY KEY (fee_structure_detail_id),
    CONSTRAINT fk_tb_fee_structure_details_structure FOREIGN KEY (fee_structure_id) REFERENCES public.tb_fee_structures (fee_structure_id) ON DELETE CASCADE,
    CONSTRAINT fk_tb_fee_structure_details_fee_type  FOREIGN KEY (fee_type_id) REFERENCES public.tb_fee_types (fee_type_id),
    CONSTRAINT fk_tb_fee_structure_details_frequency FOREIGN KEY (frequency_id) REFERENCES public.lut_fee_frequency (frequency_id),
    CONSTRAINT uq_tb_fee_structure_details_structure_type UNIQUE (fee_structure_id, fee_type_id),
    CONSTRAINT chk_tb_fee_structure_details_amount CHECK (amount >= 0)
);

-- 5. tb_fee_concessions (school-owned discount / concession master)
CREATE TABLE IF NOT EXISTS public.tb_fee_concessions
(
    concession_id            bigint        GENERATED ALWAYS AS IDENTITY,
    school_id                varchar(100)  NOT NULL,
    concession_name          varchar(100)  NOT NULL,
    concession_code          varchar(30)   NOT NULL,
    concession_type          varchar(20)   NOT NULL,
    value                    numeric(10,2) NOT NULL,
    applicable_fee_type_id   bigint,
    isactive                 boolean       NOT NULL DEFAULT true,
    created_at               timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at               timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_fee_concessions_pkey PRIMARY KEY (concession_id),
    CONSTRAINT fk_tb_fee_concessions_school   FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_fee_concessions_fee_type FOREIGN KEY (applicable_fee_type_id) REFERENCES public.tb_fee_types (fee_type_id),
    CONSTRAINT chk_tb_fee_concessions_type  CHECK (concession_type IN ('PERCENTAGE', 'FIXED')),
    CONSTRAINT chk_tb_fee_concessions_value CHECK (value >= 0 AND (concession_type <> 'PERCENTAGE' OR value <= 100))
);
CREATE INDEX IF NOT EXISTS idx_tb_fee_concessions_school_id ON public.tb_fee_concessions (school_id);

-- A school cannot have two concessions with the same code. Added separately so that a database that already has the
-- table (created by an earlier version of this file) gets it too. Codes are stored upper-case by the API.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_tb_fee_concessions_school_code') THEN
        ALTER TABLE public.tb_fee_concessions
            ADD CONSTRAINT uq_tb_fee_concessions_school_code UNIQUE (school_id, concession_code);
    END IF;
END $$;

COMMIT;
