-- Schema signature of the Inventory objects: columns, constraints, indexes, triggers and the two functions, one normalised line each.
-- READ-ONLY. Run it on the scratch database and on the real one after the migration; the two outputs must be IDENTICAL.
--
--   psql -h localhost -U <db_user> -d smss_scratch_inv -At -f smss-inventory-stage1-schema-signature.sql > sig-scratch.txt
--   psql -h localhost -U <db_user> -d smss-db          -At -f smss-inventory-stage1-schema-signature.sql > sig-real.txt
--   diff sig-scratch.txt sig-real.txt        <- must print NOTHING
--   (the line starting MD5 is a hash of every other line: if it matches, the schemas match)
--
-- Identity values, row data and the role id given to STORE_KEEPER are NOT part of it (they legitimately differ between databases).
\set ON_ERROR_STOP on
\pset format unaligned
\pset tuples_only on

WITH objs AS (
    SELECT c.oid, c.relname
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = 'public' AND c.relkind = 'r' AND (c.relname LIKE 'tb\_inv\_%' OR c.relname LIKE 'lut\_inv\_%')
),
lines AS (
    SELECT 'column|' || o.relname || '|' || a.attnum || '|' || a.attname || '|' || format_type(a.atttypid, a.atttypmod) || '|notnull=' || a.attnotnull
           || '|default=' || coalesce(pg_get_expr(d.adbin, d.adrelid), '') || '|identity=' || a.attidentity::text AS line
    FROM objs o
    JOIN pg_attribute a ON a.attrelid = o.oid AND a.attnum > 0 AND NOT a.attisdropped
    LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
    UNION ALL
    SELECT 'constraint|' || o.relname || '|' || con.conname || '|' || pg_get_constraintdef(con.oid)
    FROM objs o JOIN pg_constraint con ON con.conrelid = o.oid
    UNION ALL
    SELECT 'index|' || o.relname || '|' || pg_get_indexdef(i.indexrelid)
    FROM objs o JOIN pg_index i ON i.indrelid = o.oid
    UNION ALL
    SELECT 'trigger|' || o.relname || '|' || t.tgname || '|enabled=' || t.tgenabled::text || '|' || pg_get_triggerdef(t.oid)
    FROM objs o JOIN pg_trigger t ON t.tgrelid = o.oid AND NOT t.tgisinternal
    UNION ALL
    SELECT 'function|' || p.proname || '|' || md5(pg_get_functiondef(p.oid))
    FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
    WHERE n.nspname = 'public' AND p.proname IN ('fn_tb_inv_stock_movements_immutable', 'fn_tb_inv_issues_recipient_school')
    UNION ALL
    SELECT 'seed|lut_inv_unit|' || unit_code || '|' || unit_name || '|' || is_active FROM public.lut_inv_unit
    UNION ALL
    SELECT 'seed|lut_roles|' || role_code || '|' || role_name || '|' || coalesce(description, '') FROM public.lut_roles WHERE role_code = 'STORE_KEEPER'
)
SELECT line FROM lines
UNION ALL
SELECT 'MD5 ' || md5(string_agg(line, E'\n' ORDER BY line)) FROM lines
ORDER BY 1;
