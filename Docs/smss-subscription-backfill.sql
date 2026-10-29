-- Gives every school registered BEFORE the billing feature a subscription row: a fresh trial that starts today.
-- Needs smss-subscription-billing.sql. Safe to run twice (schools that already have a row are skipped).
--
-- The trial is NOT backdated to the school's registration date: trial_start_date = today (India date, like the API), trial_end_date =
-- today + trial_days from the Active pricing row, so each existing school gets a real, accurate trial starting now.
-- The platform's own organisation row (the school the Super Admin login belongs to) is not a customer and is skipped.

BEGIN;

INSERT INTO public.tb_school_subscriptions (school_id, status, trial_start_date, trial_end_date)
SELECT s.school_id,
       'TRIAL',
       (now() AT TIME ZONE 'Asia/Kolkata')::date,
       (now() AT TIME ZONE 'Asia/Kolkata')::date + p.trial_days
FROM public.tb_schools s
CROSS JOIN (SELECT trial_days FROM public.tb_subscription_pricing WHERE is_active) p
WHERE NOT EXISTS (SELECT 1 FROM public.tb_school_subscriptions x WHERE x.school_id = s.school_id)
  AND NOT EXISTS (SELECT 1 FROM public.tb_users u WHERE u.school_id = s.school_id AND u.user_type = 'Super Admin');

COMMIT;
