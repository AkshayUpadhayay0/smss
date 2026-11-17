# Applying Inventory Stage 1 to a local database

For a teammate whose local DB does **not** have Inventory yet. All scripts are in `Docs/`. Replace `<host>`, `<port>`, `<db_user>`, `<db_name>`; psql asks for the password (never put it in a command or file).

## Prerequisites (the PRE check verifies these)
- `lut_status` has one row `Active` / `general status`, and `lut_status.sid` is `integer`.
- `tb_students` has `UNIQUE (student_id, school_id)`.
- `tb_employees`, `tb_employee_departments`, `tb_users`, `tb_schools` exist (and `lut_roles`).
- No role with code `STORE_KEEPER` under a different name; no `tb_inv_*` / `lut_inv_*` tables yet.

## Steps
1. **Stop the API** (and any process using the DB).
2. **Back up:**
   `pg_dump -h <host> -p <port> -U <db_user> -Fc -f smss-before-inventory.dump <db_name>`
3. **Fingerprint (before):**
   `psql -q -h <host> -p <port> -U <db_user> -d <db_name> -At -f smss-inventory-stage1-fingerprint.sql > fp-before.txt`
4. **Verify, PRE mode:**
   `psql -h <host> -p <port> -U <db_user> -d <db_name> -v mode=pre -f smss-inventory-stage1-verify.sql`
   Pass = every line `PASS` and the last line `FAILED CHECKS: 0`. Anything else: stop.
5. **Migrate (one transaction, stop on first error):**
   `psql -h <host> -p <port> -U <db_user> -d <db_name> -v ON_ERROR_STOP=1 --single-transaction -f smss-inventory-stage1.sql`
6. **Verify, POST mode:**
   `psql -h <host> -p <port> -U <db_user> -d <db_name> -v mode=post -f smss-inventory-stage1-verify.sql`
   Must again end with `FAILED CHECKS: 0`. (`mode` defaults to `post` if omitted, so always pass it explicitly.)
7. **Schema signature:**
   `psql -h <host> -p <port> -U <db_user> -d <db_name> -At -f smss-inventory-stage1-schema-signature.sql > sig-local.txt`
   The last line is `MD5 <hash>`, a hash of every other line. Ask me for the reference `sig` output (or its MD5); your file must be identical (`diff` prints nothing).
8. **Fingerprint (after) and compare:**
   `psql -q -h <host> -p <port> -U <db_user> -d <db_name> -At -f smss-inventory-stage1-fingerprint.sql > fp-after.txt`
   `diff fp-before.txt fp-after.txt` must print **nothing**. Use `-q` both times. Do it while nobody else is writing to the DB.
9. **Restart the API.**

## What to expect
- The only change to existing data: `lut_roles` gains one row, `STORE_KEEPER` ("Store Keeper"). Every other pre-existing table is identical (the fingerprint ignores the new `tb_inv_*` / `lut_inv_*` tables, `tb_refresh_tokens`, and that one role row).
- Two harmless psql warnings, because the script has its own `BEGIN`/`COMMIT`:
  `WARNING: there is already a transaction in progress` (at the start) and `WARNING: there is no transaction in progress` (at the end).
- Any other `ERROR` is a failure.

## If something fails
The transaction rolls back, so nothing is half-applied. **Do not re-run blindly.** Read the error, restore from your own backup if the DB looks wrong (`pg_restore` of `smss-before-inventory.dump` into a fresh DB), and ask me. The rollback and cleanup scripts are **not** shared (they delete data and are kept out of git).

## Warnings
- **Never re-run the migration on a database that already has Inventory data.** Stock ledger rows cannot be deleted (triggers block it), so a bad state cannot simply be wiped.
- **Never run it on a shared or production database** without a fresh backup and my approval.

## Logging in as a Store Keeper
Create an employee for the person in the app (Employees). Then use the employee's existing **Create login** action (`POST /api/Employee/{employeeId}/create-login`) and choose the **Store Keeper** role. The login id and temporary password are shown once; hand them over securely. The person changes the password at first sign-in.
