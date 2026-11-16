-- Inventory Stage 1: read-only checks. PRE mode before the migration, POST mode after it. Pure SELECTs: it also runs in a session forced to read-only
-- (PGOPTIONS='-c default_transaction_read_only=on'), and it creates nothing, not even a temporary table.
--
--   psql -h localhost -U <db_user> -d smss-db -v mode=pre  -f smss-inventory-stage1-verify.sql     (before the migration)
--   psql -h localhost -U <db_user> -d smss-db -v mode=post -f smss-inventory-stage1-verify.sql     (after it, and again after the cleanup)
--
-- Every check prints PASS or FAIL. The last line is "FAILED CHECKS: n"; anything but 0 means stop.
\set ON_ERROR_STOP on
\pset format unaligned
\pset tuples_only on

\if :{?mode}
\else
    \set mode post
\endif
SELECT :'mode' = 'pre' AS is_pre \gset

\if :is_pre
WITH chk(name, ok, detail) AS (VALUES
    ('prereq: lut_status has Active / general status', (SELECT count(*) = 1 FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status'), (SELECT 'rows=' || count(*) FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')),
    ('prereq: lut_status.sid is integer', (SELECT data_type = 'integer' FROM information_schema.columns WHERE table_name = 'lut_status' AND column_name = 'sid'), (SELECT data_type FROM information_schema.columns WHERE table_name = 'lut_status' AND column_name = 'sid')),
    ('prereq: tb_students has UNIQUE (student_id, school_id)', (SELECT count(*) = 1 FROM pg_constraint WHERE conrelid = 'public.tb_students'::regclass AND contype = 'u' AND pg_get_constraintdef(oid) = 'UNIQUE (student_id, school_id)'), (SELECT 'found=' || count(*) FROM pg_constraint WHERE conrelid = 'public.tb_students'::regclass AND contype = 'u' AND pg_get_constraintdef(oid) = 'UNIQUE (student_id, school_id)')),
    ('prereq: tb_employees / tb_employee_departments / tb_users / tb_schools exist', (SELECT count(*) = 4 FROM pg_class WHERE relname IN ('tb_employees', 'tb_employee_departments', 'tb_users', 'tb_schools') AND relkind = 'r'), (SELECT 'found=' || count(*) FROM pg_class WHERE relname IN ('tb_employees', 'tb_employee_departments', 'tb_users', 'tb_schools') AND relkind = 'r')),
    ('prereq: no role code STORE_KEEPER is taken by a different name', (SELECT NOT EXISTS (SELECT 1 FROM public.lut_roles WHERE role_code = 'STORE_KEEPER' AND role_name <> 'Store Keeper')), ''),
    ('pre: no inventory table exists yet', (SELECT count(*) = 0 FROM pg_class WHERE relname LIKE 'tb\_inv\_%' OR relname LIKE 'lut\_inv\_%'), (SELECT 'found=' || count(*) FROM pg_class WHERE relname LIKE 'tb\_inv\_%' OR relname LIKE 'lut\_inv\_%')),
    ('pre: lut_roles has no STORE_KEEPER yet', (SELECT count(*) = 0 FROM public.lut_roles WHERE role_code = 'STORE_KEEPER'), (SELECT 'found=' || count(*) FROM public.lut_roles WHERE role_code = 'STORE_KEEPER')),
    ('pre: the migration prerequisites are not blocked by an existing object named like a new one', (SELECT count(*) = 0 FROM pg_proc WHERE proname IN ('fn_tb_inv_stock_movements_immutable', 'fn_tb_inv_issues_recipient_school')), '')
)
SELECT line FROM (
    SELECT 0 AS r, ok AS k, CASE WHEN ok THEN 'PASS  ' ELSE 'FAIL  ' END || name || CASE WHEN detail <> '' THEN '   (' || detail || ')' ELSE '' END AS line FROM chk
    UNION ALL SELECT 1, true, 'FAILED CHECKS: ' || count(*) FROM chk WHERE NOT ok
) s ORDER BY r, k, line;
\else
WITH chk(name, ok, detail) AS (VALUES
    ('prereq: lut_status has Active / general status', (SELECT count(*) = 1 FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status'), (SELECT 'rows=' || count(*) FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')),
    ('prereq: lut_status.sid is integer', (SELECT data_type = 'integer' FROM information_schema.columns WHERE table_name = 'lut_status' AND column_name = 'sid'), (SELECT data_type FROM information_schema.columns WHERE table_name = 'lut_status' AND column_name = 'sid')),
    ('prereq: tb_students has UNIQUE (student_id, school_id)', (SELECT count(*) = 1 FROM pg_constraint WHERE conrelid = 'public.tb_students'::regclass AND contype = 'u' AND pg_get_constraintdef(oid) = 'UNIQUE (student_id, school_id)'), (SELECT 'found=' || count(*) FROM pg_constraint WHERE conrelid = 'public.tb_students'::regclass AND contype = 'u' AND pg_get_constraintdef(oid) = 'UNIQUE (student_id, school_id)')),
    ('prereq: tb_employees / tb_employee_departments / tb_users / tb_schools exist', (SELECT count(*) = 4 FROM pg_class WHERE relname IN ('tb_employees', 'tb_employee_departments', 'tb_users', 'tb_schools') AND relkind = 'r'), (SELECT 'found=' || count(*) FROM pg_class WHERE relname IN ('tb_employees', 'tb_employee_departments', 'tb_users', 'tb_schools') AND relkind = 'r')),
    ('prereq: no role code STORE_KEEPER is taken by a different name', (SELECT NOT EXISTS (SELECT 1 FROM public.lut_roles WHERE role_code = 'STORE_KEEPER' AND role_name <> 'Store Keeper')), ''),
    ('post: all 9 inventory tables exist', (SELECT count(*) = 9 FROM pg_class WHERE relkind = 'r' AND relname IN ('lut_inv_unit', 'tb_inv_categories', 'tb_inv_suppliers', 'tb_inv_items', 'tb_inv_stock_movements', 'tb_inv_stock_ins', 'tb_inv_issues', 'tb_inv_issue_lines', 'tb_inv_issue_returns')), (SELECT 'found=' || count(*) FROM pg_class WHERE relkind = 'r' AND relname IN ('lut_inv_unit', 'tb_inv_categories', 'tb_inv_suppliers', 'tb_inv_items', 'tb_inv_stock_movements', 'tb_inv_stock_ins', 'tb_inv_issues', 'tb_inv_issue_lines', 'tb_inv_issue_returns'))),
    ('post: 13 units seeded', (SELECT count(*) = 13 FROM public.lut_inv_unit), (SELECT 'found=' || count(*) FROM public.lut_inv_unit)),
    ('post: exactly one Store Keeper role, Active', (SELECT count(*) = 1 FROM public.lut_roles r WHERE r.role_code = 'STORE_KEEPER' AND r.role_name = 'Store Keeper' AND r.status_id = (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')), (SELECT 'found=' || count(*) FROM public.lut_roles WHERE role_code = 'STORE_KEEPER')),
    ('post: ledger triggers exist and are ENABLED (UPDATE/DELETE and TRUNCATE)', (SELECT count(*) = 2 FROM pg_trigger WHERE tgrelid = 'public.tb_inv_stock_movements'::regclass AND NOT tgisinternal AND tgenabled = 'O' AND tgname IN ('trg_tb_inv_stock_movements_no_change', 'trg_tb_inv_stock_movements_no_truncate')), (SELECT 'enabled=' || count(*) FROM pg_trigger WHERE tgrelid = 'public.tb_inv_stock_movements'::regclass AND NOT tgisinternal AND tgenabled = 'O' AND tgname IN ('trg_tb_inv_stock_movements_no_change', 'trg_tb_inv_stock_movements_no_truncate'))),
    ('post: recipient-school trigger exists and is ENABLED', (SELECT count(*) = 1 FROM pg_trigger WHERE tgrelid = 'public.tb_inv_issues'::regclass AND NOT tgisinternal AND tgenabled = 'O' AND tgname = 'trg_tb_inv_issues_recipient_school'), (SELECT 'enabled=' || count(*) FROM pg_trigger WHERE tgrelid = 'public.tb_inv_issues'::regclass AND NOT tgisinternal AND tgenabled = 'O' AND tgname = 'trg_tb_inv_issues_recipient_school')),
    ('post: the oversell guard CHECK exists', (SELECT count(*) = 1 FROM pg_constraint WHERE conrelid = 'public.tb_inv_items'::regclass AND conname = 'chk_tb_inv_items_qty' AND pg_get_constraintdef(oid) LIKE 'CHECK ((current_quantity >= %'), (SELECT 'found=' || count(*) FROM pg_constraint WHERE conrelid = 'public.tb_inv_items'::regclass AND conname = 'chk_tb_inv_items_qty')),
    ('post: every composite tenant FK is present (item, movement, stock-in x2, issue-line x2, issue student, return)', (SELECT count(*) = 8 FROM pg_constraint WHERE conname IN ('fk_tb_inv_items_category', 'fk_tb_inv_movements_item', 'fk_tb_inv_stock_ins_item', 'fk_tb_inv_stock_ins_supplier', 'fk_tb_inv_issue_lines_issue', 'fk_tb_inv_issue_lines_item', 'fk_tb_inv_issues_student', 'fk_tb_inv_issue_returns_line')), (SELECT 'found=' || count(*) FROM pg_constraint WHERE conname IN ('fk_tb_inv_items_category', 'fk_tb_inv_movements_item', 'fk_tb_inv_stock_ins_item', 'fk_tb_inv_stock_ins_supplier', 'fk_tb_inv_issue_lines_issue', 'fk_tb_inv_issue_lines_item', 'fk_tb_inv_issues_student', 'fk_tb_inv_issue_returns_line'))),
    ('post: ledger reconciliation (SUM(quantity_change) = current_quantity for every item)',
        (SELECT count(*) = 0 FROM public.tb_inv_items i LEFT JOIN (SELECT item_id, sum(quantity_change) AS total FROM public.tb_inv_stock_movements GROUP BY item_id) m ON m.item_id = i.item_id WHERE i.current_quantity <> coalesce(m.total, 0)),
        (SELECT 'mismatching items=' || count(*) FROM public.tb_inv_items i LEFT JOIN (SELECT item_id, sum(quantity_change) AS total FROM public.tb_inv_stock_movements GROUP BY item_id) m ON m.item_id = i.item_id WHERE i.current_quantity <> coalesce(m.total, 0))),
    ('post: no negative quantity anywhere', (SELECT count(*) = 0 FROM public.tb_inv_items WHERE current_quantity < 0), (SELECT 'items=' || count(*) FROM public.tb_inv_items WHERE current_quantity < 0)),
    ('post: no row of any school points at another school (item -> category, movement -> item)',
        (SELECT count(*) = 0 FROM (SELECT 1 FROM public.tb_inv_items i JOIN public.tb_inv_categories c ON c.category_id = i.category_id WHERE c.school_id <> i.school_id UNION ALL SELECT 1 FROM public.tb_inv_stock_movements m JOIN public.tb_inv_items i ON i.item_id = m.item_id WHERE i.school_id <> m.school_id) x),
        (SELECT 'bad=' || count(*) FROM (SELECT 1 FROM public.tb_inv_items i JOIN public.tb_inv_categories c ON c.category_id = i.category_id WHERE c.school_id <> i.school_id UNION ALL SELECT 1 FROM public.tb_inv_stock_movements m JOIN public.tb_inv_items i ON i.item_id = m.item_id WHERE i.school_id <> m.school_id) x))
)
SELECT line FROM (
    SELECT 0 AS r, ok AS k, CASE WHEN ok THEN 'PASS  ' ELSE 'FAIL  ' END || name || CASE WHEN detail <> '' THEN '   (' || detail || ')' ELSE '' END AS line FROM chk
    UNION ALL SELECT 1, true, 'FAILED CHECKS: ' || count(*) FROM chk WHERE NOT ok
) s ORDER BY r, k, line;
\endif
