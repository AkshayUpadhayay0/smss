-- Optional fee types: a fee type can be marked optional (e.g. Transport). At admission an optional fee head is shown with an
-- include / exclude checkbox; a non-optional one (Tuition, Admission Fee ...) is always applied and has no toggle.
--
-- Run BEFORE starting the API build that maps TbFeeTypes.IsOptional (every fee type query reads the column). Safe to run twice.
-- Existing fee types become non-optional (default false), i.e. exactly how they behaved before.

ALTER TABLE public.tb_fee_types
    ADD COLUMN IF NOT EXISTS is_optional boolean NOT NULL DEFAULT false;
