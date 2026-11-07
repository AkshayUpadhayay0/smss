-- Examination / Report Card, STAGE 5b, part 1: "things to improve" text next to the per-subject remark.
--
-- Changes tb_report_subject_remarks (created in the stage 5a migration). Additive and idempotent (safe to run twice); no existing row is changed.
--  * improvement_text varchar(200) NULL: what a child should work on in this subject (descriptive, GRADES-mode schemes; only the playful-report design prints it).
--  * remark may now be NULL, because a teacher can write only an improvement note. A row must still carry some text: at least one of the two is non-blank.
--    (The remark column was NOT NULL with a non-blank check; both are relaxed to the "at least one of the two" rule. Existing rows all satisfy it.)
--  * Same rules as the remark: written by whoever may enter marks for the subject, or the class teacher, while the term-section is OPEN; locked with it.

BEGIN;

ALTER TABLE public.tb_report_subject_remarks ADD COLUMN IF NOT EXISTS improvement_text varchar(200);
ALTER TABLE public.tb_report_subject_remarks ALTER COLUMN remark DROP NOT NULL;

ALTER TABLE public.tb_report_subject_remarks DROP CONSTRAINT IF EXISTS chk_tb_rsr_remark;
ALTER TABLE public.tb_report_subject_remarks DROP CONSTRAINT IF EXISTS chk_tb_rsr_text;
ALTER TABLE public.tb_report_subject_remarks ADD CONSTRAINT chk_tb_rsr_text
    CHECK ((remark IS NULL OR length(btrim(remark)) > 0)
       AND (improvement_text IS NULL OR length(btrim(improvement_text)) > 0)
       AND (remark IS NOT NULL OR improvement_text IS NOT NULL));

COMMIT;
