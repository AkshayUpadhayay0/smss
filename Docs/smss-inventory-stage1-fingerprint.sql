-- Fingerprint of EVERY pre-existing table (row count + md5 of all rows), for the before / after comparison around the Inventory Stage 1 deploy.
-- READ-ONLY. Safe to run on any database.
--
--   psql -q -h localhost -U <db_user> -d smss-db -At -f smss-inventory-stage1-fingerprint.sql > fp-before.txt
--   ... deploy, test, clean up ...
--   psql -q -h localhost -U <db_user> -d smss-db -At -f smss-inventory-stage1-fingerprint.sql > fp-after.txt
--   diff fp-before.txt fp-after.txt        <- must print NOTHING (always use -q, so both files carry the same lines)
--
-- What is left out on purpose:
--   * every tb_inv_* / lut_inv_* table (they are new: the module's own tables)
--   * tb_refresh_tokens (a sign-in writes a token, so it changes by design; the throwaway accounts' tokens are removed by the cleanup anyway)
--   * the single lut_roles row STORE_KEEPER (the one allowed addition: the row count of lut_roles changes by +1, nothing else)
-- Run it from a quiet moment (nobody else signing in or editing), or an unrelated write shows up as a difference.
\set ON_ERROR_STOP on
\pset format unaligned
\pset tuples_only on

SELECT format(
    'SELECT %L AS tbl, count(*) AS row_count, md5(coalesce(string_agg(t::text, %L ORDER BY t::text), %L)) AS md5 FROM public.%I t %s',
    c.relname, '|', '', c.relname,
    CASE WHEN c.relname = 'lut_roles' THEN $$WHERE t.role_code <> 'STORE_KEEPER'$$ ELSE '' END)
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public'
  AND c.relkind = 'r'
  AND c.relname NOT LIKE 'tb\_inv\_%'
  AND c.relname NOT LIKE 'lut\_inv\_%'
  AND c.relname <> 'tb_refresh_tokens'
ORDER BY c.relname
\gexec
