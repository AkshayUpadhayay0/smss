-- =====================================================================================================================
-- READ-ONLY dry run for the guardian stage (3a to 3e): what the guardian mobile numbers look like and which guardians look like duplicates.
-- It changes NOTHING (SELECTs only). Run it before 3a and again at each gate; the numbers must match the design summary of 25 Nov 2026 (on smss-db: 355 guardians, all
-- "bare 10-digit 6-9", 0 that would not normalise, 6 duplicate groups of 2 rows each, 0 shared across schools, 0 equal to a student's or a user's number).
-- fn_phone_key exists only AFTER 3a; this script uses the same rule inline so it can run before.
-- =====================================================================================================================
WITH g AS (
    SELECT guardian_id, school_id, first_name, relationship, mobile_number,
           regexp_replace(mobile_number, '[^0-9]', '', 'g') AS d
      FROM public.tb_guardians
), k AS (
    SELECT g.*, CASE WHEN d ~ '^[6-9][0-9]{9}$' THEN d WHEN d ~ '^91[6-9][0-9]{9}$' THEN substr(d, 3) WHEN d ~ '^0[6-9][0-9]{9}$' THEN substr(d, 2) END AS phone_key
      FROM g
)
SELECT 'guardians' AS check_name, count(*)::text AS result FROM k
UNION ALL SELECT 'format: bare 10-digit 6-9', count(*)::text FROM k WHERE mobile_number ~ '^[6-9][0-9]{9}$'
UNION ALL SELECT 'format: anything else (would be rewritten or rejected)', count(*)::text FROM k WHERE mobile_number !~ '^[6-9][0-9]{9}$'
UNION ALL SELECT 'would NOT normalise (key is null)', count(*)::text FROM k WHERE phone_key IS NULL
UNION ALL SELECT 'duplicate groups (same school, same key)', count(*)::text FROM (SELECT school_id, phone_key FROM k WHERE phone_key IS NOT NULL GROUP BY 1, 2 HAVING count(*) > 1) x
UNION ALL SELECT 'rows inside duplicate groups', coalesce(sum(n), 0)::text FROM (SELECT count(*) n FROM k WHERE phone_key IS NOT NULL GROUP BY school_id, phone_key HAVING count(*) > 1) x
UNION ALL SELECT 'duplicate groups with different names', count(*)::text FROM (SELECT school_id, phone_key FROM k WHERE phone_key IS NOT NULL GROUP BY 1, 2 HAVING count(*) > 1 AND count(DISTINCT lower(btrim(first_name))) > 1) x
UNION ALL SELECT 'keys shared across schools', count(*)::text FROM (SELECT phone_key FROM k WHERE phone_key IS NOT NULL GROUP BY 1 HAVING count(DISTINCT school_id) > 1) x
UNION ALL SELECT 'guardian number equals a student number', count(*)::text FROM k JOIN public.tb_students s ON s.school_id = k.school_id AND s.mobile_number IS NOT NULL AND s.mobile_number = k.mobile_number
UNION ALL SELECT 'guardian number equals a user number / login id', count(*)::text FROM k JOIN public.tb_users u ON (u.mobile_number IS NOT NULL AND u.mobile_number = k.mobile_number) OR u.org_user_id = k.mobile_number
UNION ALL SELECT 'students with more than one primary guardian', count(*)::text FROM (SELECT student_id FROM public.tb_student_guardians WHERE is_primary GROUP BY 1 HAVING count(*) > 1) x
UNION ALL SELECT 'students with no primary guardian', count(*)::text FROM public.tb_students s WHERE NOT EXISTS (SELECT 1 FROM public.tb_student_guardians l WHERE l.student_id = s.student_id AND l.is_primary)
UNION ALL SELECT 'guardians with an email', count(*)::text FROM public.tb_guardians WHERE email IS NOT NULL AND btrim(email) <> ''
UNION ALL SELECT 'guardians with a WhatsApp number', count(*)::text FROM public.tb_guardians WHERE whatsapp_number IS NOT NULL
UNION ALL SELECT 'users holding the Guardian role', count(*)::text FROM public.tb_user_roles ur JOIN public.lut_roles r ON r.role_id = ur.role_id WHERE r.role_code = 'GUARDIAN';
