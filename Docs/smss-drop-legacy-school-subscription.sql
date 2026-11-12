-- Drops the legacy subscription columns from tb_schools: subscription_plan_id, subscription_start_date, subscription_end_date,
-- subscription_status_id (and the foreign key on the last one). They were a leftover of the old "Select Subscription Plan" design.
-- Subscriptions live in tb_school_subscriptions (see smss-subscription-billing.sql); nothing reads these columns any more.
--
-- ORDER: run this AFTER the API build without TbSchools.SubscriptionPlanId / StartDate / EndDate / StatusId is the one running.
-- The previous build selects those columns on every tb_schools query and would fail once they are gone. The new build does not
-- reference them, so it also runs fine while they still exist. Safe to run twice (IF EXISTS everywhere).
--
-- DATA LOST (recorded on 2026-11-12, real smss-db, 4 schools): plan id, start date and end date were NULL on every school.
-- subscription_status_id was set on exactly ONE school: SUAD0000001 (the Super Admin's own school), value = lut_status
-- 'Active' (stype 'general status'). That school has no tb_school_subscriptions row; the backfill script is NOT part of this change.
--
-- ROLLBACK (the columns were empty except for the one value above; the quickest full rollback is restoring the pre-migration dump):
--   BEGIN;
--   ALTER TABLE public.tb_schools
--       ADD COLUMN IF NOT EXISTS subscription_plan_id     bigint,
--       ADD COLUMN IF NOT EXISTS subscription_start_date  date,
--       ADD COLUMN IF NOT EXISTS subscription_end_date    date,
--       ADD COLUMN IF NOT EXISTS subscription_status_id   integer;
--   ALTER TABLE public.tb_schools
--       ADD CONSTRAINT fk_tb_schools_subscription_status FOREIGN KEY (subscription_status_id) REFERENCES public.lut_status (sid);
--   UPDATE public.tb_schools
--      SET subscription_status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')
--    WHERE school_id = 'SUAD0000001';
--   COMMIT;
--   Then redeploy the API build that still has the four TbSchools properties (only needed if that build must run again).

BEGIN;

ALTER TABLE public.tb_schools DROP CONSTRAINT IF EXISTS fk_tb_schools_subscription_status;

ALTER TABLE public.tb_schools
    DROP COLUMN IF EXISTS subscription_plan_id,
    DROP COLUMN IF EXISTS subscription_start_date,
    DROP COLUMN IF EXISTS subscription_end_date,
    DROP COLUMN IF EXISTS subscription_status_id;

COMMIT;
