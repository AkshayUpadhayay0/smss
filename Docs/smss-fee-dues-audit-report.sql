-- =====================================================================================================================
-- AUDIT REPORT: fee dues that look wrongly generated   (READ ONLY: this file changes nothing)
--
-- WHAT IT SHOWS, one row per school
--   dues_before_enrolment .............. dues dated BEFORE the student's enrolment date for that academic year (a student cannot owe a month before they joined)
--   past_at_creation ................... dues that were already past on the day they were created (born overdue), split into:
--     past_created_on_enrolment_day ...... created on (or the day after) the student's enrolment date: generated at admission
--     past_created_after_enrolment ....... created later than that
--     past_added_by_later_sync ........... created more than a minute after their fee item: added by a later structure change (a fee line added or edited), not at admission
--                                          (students entered with an HISTORICAL admission date show up in the first two columns: their dues were generated when the record was keyed in)
--   items_frequency_differs_from_line .. fee items whose frequency is not their fee line's frequency (the old per-student frequency override)
--   items_total_over_expected .......... items whose dues add up to MORE than a full year of their line (net amount x installments of the line's frequency)
--   items_total_under_expected_full_year  items of students enrolled within a month of the start of the year whose dues add up to LESS than a full year
--                                          (often legitimate: a student enrolled after the first due day of a monthly fee owes one installment fewer; look at the large gaps)
--   (each count has an _amount column where an amount makes sense)
--
-- HOW TO RUN IT
--   Open this file in the Query Tool of any SMSS database in pgAdmin (right-click the database > Query Tool > open file) and run it with F5. The result is in the "Data Output" tab.
--   It works on a database before or after the effective-from migration (it only reads columns that have always existed). It starts a READ ONLY transaction and ends with ROLLBACK, so PostgreSQL itself
--   refuses any change; it reads no password or secret column (school id and name only).
-- =====================================================================================================================

BEGIN TRANSACTION READ ONLY;

WITH due_facts AS (
    SELECT d.fee_due_id, d.school_id, d.due_date, d.amount_due, d.amount_paid,
           (d.created_at AT TIME ZONE 'Asia/Kolkata')::date AS created_day,
           d.created_at - i.created_at                       AS created_after_item,
           e.enrollment_date::date                           AS enrollment_date
    FROM public.tb_student_fee_dues d
    JOIN public.tb_student_fee_items i ON i.student_fee_item_id = d.student_fee_item_id
    LEFT JOIN public.tb_student_enrollments e ON e.student_id = i.student_id AND e.academic_year_id = i.academic_year_id
),
due_summary AS (
    SELECT school_id,
           count(*)                                                                                                      AS dues,
           count(*) FILTER (WHERE enrollment_date IS NOT NULL AND due_date < enrollment_date)                            AS dues_before_enrolment,
           coalesce(sum(amount_due) FILTER (WHERE enrollment_date IS NOT NULL AND due_date < enrollment_date), 0)        AS dues_before_enrolment_amount,
           count(*) FILTER (WHERE created_day > due_date)                                                                AS past_at_creation,
           coalesce(sum(amount_due) FILTER (WHERE created_day > due_date), 0)                                            AS past_at_creation_amount,
           count(*) FILTER (WHERE created_day > due_date AND enrollment_date IS NOT NULL AND created_day <= enrollment_date + 1)   AS past_created_on_enrolment_day,
           count(*) FILTER (WHERE created_day > due_date AND (enrollment_date IS NULL OR created_day > enrollment_date + 1))       AS past_created_after_enrolment,
           count(*) FILTER (WHERE created_day > due_date AND created_after_item > interval '1 minute')                   AS past_added_by_later_sync,
           coalesce(sum(amount_due) FILTER (WHERE created_day > due_date AND created_after_item > interval '1 minute'), 0) AS past_added_by_later_sync_amount
    FROM due_facts
    GROUP BY school_id
),
item_facts AS (
    SELECT i.school_id, i.student_fee_item_id,
           fi.frequency_code AS item_freq, fl.frequency_code AS line_freq,
           i.net_amount * CASE fl.frequency_code WHEN 'MONTHLY' THEN 12 WHEN 'QUARTERLY' THEN 4 WHEN 'HALF_YEARLY' THEN 2 ELSE 1 END AS expected_total,
           coalesce((SELECT sum(x.amount_due) FROM public.tb_student_fee_dues x WHERE x.student_fee_item_id = i.student_fee_item_id), 0) AS dues_total,
           (e.enrollment_date::date <= ay.start_date + 31) AS full_year_student,
           i.is_applicable, i.isactive
    FROM public.tb_student_fee_items i
    JOIN public.tb_student_fee_structure a ON a.student_id = i.student_id AND a.academic_year_id = i.academic_year_id
    JOIN public.tb_fee_structure_details d ON d.fee_structure_id = a.fee_structure_id AND d.fee_type_id = i.fee_type_id
    JOIN public.lut_fee_frequency fi ON fi.frequency_id = i.frequency_id
    JOIN public.lut_fee_frequency fl ON fl.frequency_id = d.frequency_id
    JOIN public.tb_academic_years ay ON ay.academic_year_id = i.academic_year_id
    LEFT JOIN public.tb_student_enrollments e ON e.student_id = i.student_id AND e.academic_year_id = i.academic_year_id
),
item_summary AS (
    SELECT school_id,
           count(*)                                                                                                           AS items,
           count(*) FILTER (WHERE item_freq <> line_freq)                                                                     AS items_frequency_differs_from_line,
           count(*) FILTER (WHERE is_applicable AND isactive AND dues_total > expected_total)                                 AS items_total_over_expected,
           coalesce(sum(dues_total - expected_total) FILTER (WHERE is_applicable AND isactive AND dues_total > expected_total), 0) AS items_total_over_expected_amount,
           count(*) FILTER (WHERE is_applicable AND isactive AND full_year_student AND dues_total < expected_total)           AS items_total_under_expected_full_year
    FROM item_facts
    GROUP BY school_id
)
SELECT s.school_id,
       s.school_name,
       coalesce(ds.dues, 0)                              AS dues,
       coalesce(ds.dues_before_enrolment, 0)             AS dues_before_enrolment,
       coalesce(ds.dues_before_enrolment_amount, 0)      AS dues_before_enrolment_amount,
       coalesce(ds.past_at_creation, 0)                  AS past_at_creation,
       coalesce(ds.past_at_creation_amount, 0)           AS past_at_creation_amount,
       coalesce(ds.past_created_on_enrolment_day, 0)     AS past_created_on_enrolment_day,
       coalesce(ds.past_created_after_enrolment, 0)      AS past_created_after_enrolment,
       coalesce(ds.past_added_by_later_sync, 0)          AS past_added_by_later_sync,
       coalesce(ds.past_added_by_later_sync_amount, 0)   AS past_added_by_later_sync_amount,
       coalesce(its.items, 0)                            AS items,
       coalesce(its.items_frequency_differs_from_line, 0) AS items_frequency_differs_from_line,
       coalesce(its.items_total_over_expected, 0)        AS items_total_over_expected,
       coalesce(its.items_total_over_expected_amount, 0) AS items_total_over_expected_amount,
       coalesce(its.items_total_under_expected_full_year, 0) AS items_total_under_expected_full_year
FROM public.tb_schools s
LEFT JOIN due_summary ds  ON ds.school_id  = s.school_id
LEFT JOIN item_summary its ON its.school_id = s.school_id
ORDER BY s.school_id;

ROLLBACK;
