-- =====================================================================================================================
-- READ-ONLY dry run for Stage 4a-1 (fee structure copy): what the FEE_STRUCTURES part of Year Copy WOULD do. Changes NOTHING (SELECTs only).
-- For every school with two or more academic years it takes the latest year that starts before the newest year as the source, the newest as the target,
-- and reports per source fee structure: copied, already there, or skipped with the reason. It mirrors the rules in YearCopyRepository.FeeStructCand.
-- On smss-db at design time only LAS261103001 has two years (2026-27 -> 2027-2028, where 2027-2028 offers one class), so every other class is "not offered".
-- =====================================================================================================================
WITH pairs AS (
    SELECT t.school_id, t.academic_year_id AS tgt, t.year_name AS tgt_name,
           (SELECT s.academic_year_id FROM public.tb_academic_years s WHERE s.school_id = t.school_id AND s.start_date < t.start_date ORDER BY s.start_date DESC LIMIT 1) AS src
      FROM public.tb_academic_years t
     WHERE t.start_date = (SELECT max(start_date) FROM public.tb_academic_years x WHERE x.school_id = t.school_id)
), cand AS (
    SELECT p.school_id, p.tgt_name, s.fee_structure_id, cl.class_name,
           CASE WHEN NOT s.isactive THEN 'The fee structure is inactive'
                WHEN cl.status_id IS DISTINCT FROM (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status') THEN 'The class is inactive'
                WHEN NOT EXISTS (SELECT 1 FROM public.tb_academic_year_classes t WHERE t.academic_year_id = p.tgt AND t.class_id = s.class_id
                                    AND t.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')) THEN 'The class is not offered in the target year'
                WHEN EXISTS (SELECT 1 FROM public.tb_fee_structures t WHERE t.school_id = p.school_id AND t.academic_year_id = p.tgt AND t.class_id = s.class_id) THEN 'Already there'
                WHEN NOT EXISTS (SELECT 1 FROM public.tb_fee_structure_details d JOIN public.tb_fee_types ft ON ft.fee_type_id = d.fee_type_id AND ft.school_id = p.school_id WHERE d.fee_structure_id = s.fee_structure_id AND ft.isactive) THEN 'No line with an active fee type'
                ELSE 'WOULD COPY' END AS outcome
      FROM pairs p
      JOIN public.tb_fee_structures s ON s.school_id = p.school_id AND s.academic_year_id = p.src
      JOIN public.tb_classes cl ON cl.class_id = s.class_id AND cl.school_id = p.school_id
     WHERE p.src IS NOT NULL
)
SELECT school_id, tgt_name AS target_year, outcome, count(*) AS structures,
       coalesce(sum((SELECT count(*) FROM public.tb_fee_structure_details d JOIN public.tb_fee_types ft ON ft.fee_type_id = d.fee_type_id AND ft.isactive WHERE d.fee_structure_id = cand.fee_structure_id)) FILTER (WHERE outcome = 'WOULD COPY'), 0) AS lines_that_would_be_copied,
       string_agg(class_name, ', ' ORDER BY class_name) AS classes
  FROM cand GROUP BY school_id, tgt_name, outcome ORDER BY school_id, outcome;
