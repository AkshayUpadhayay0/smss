-- Existing Student Onboarding: direct-entry students have no tb_admissions row to hold the Aadhaar number,
-- so tb_students gets its own (optional) column. The regular admission flow still keeps Aadhaar on the
-- application only and does not write this column.
--
-- Run this BEFORE starting the API build that contains the TbStudent.AadharNumber mapping: once that mapping is
-- deployed, every query and insert on tb_students references aadhar_number.

ALTER TABLE public.tb_students
    ADD COLUMN IF NOT EXISTS aadhar_number varchar(20);

CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_students_school_aadhar
    ON public.tb_students (school_id, aadhar_number)
    WHERE aadhar_number IS NOT NULL;
