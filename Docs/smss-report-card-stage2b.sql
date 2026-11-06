-- Examination / Report Card, STAGE 2b: late students and the lock. Additive, idempotent. Needs the Stage 2 migration.
--
-- Decision (architect review, 6 Nov 2026): a student added after a section's marks were accepted must not block locking. Lock proceeds after a confirmation that names them,
-- and the audit row stores their student ids so it is always known who was left out of a term's results.
BEGIN;
ALTER TABLE public.tb_report_audit ADD COLUMN IF NOT EXISTS excluded_students text;   -- comma-separated student ids; only set on a LOCK that left late students out
COMMIT;
