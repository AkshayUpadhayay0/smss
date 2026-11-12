# Security runbook: rotating secrets, ending sessions, rewriting git history

Prepared 2026-11-06 after the repository was found to be public with secrets, personal data and database dumps in its history. **Nothing in this file has been run.** Run the steps in order. Every value in the examples is a placeholder.

## 0. Order

1. Rotate the secrets (database password, SMTP app password, setup key, JWT key): see `Docs/configuration.md` ("Rotating a secret").
2. Restart the API (so it has the new JWT key). All access tokens signed with the old key now die.
3. Revoke every refresh token (section 1).
4. Reset or force-change the logins you care about (section 2).
5. Make the repository private, then rewrite and force-push the history (section 3), then re-clone.
6. Ask GitHub to purge cached views (section 3, last part).

Treat every old secret and everything inside the old database dumps as already public, whatever you do to the history.

## 1. Revoke all refresh tokens (real database)

Refresh tokens are stored hashed (SHA-256 of a random token; the table keeps only `token_hash`), so a leaked dump cannot be turned back into a working token. None of the 14 dumps in the history holds a token that is still valid (the latest expired on 2026-10-30). Revoking is still the right step after rotating the JWT key, because it ends the sessions that exist today (8 active tokens when checked).

Run in `psql` against `smss-db`. It first saves the list of what it revoked, so it can be undone:

```sql
BEGIN;
CREATE TABLE tb_refresh_tokens_revoked_20261106 AS
    SELECT refresh_token_id, now() AS revoked_at_run FROM tb_refresh_tokens WHERE revoked_at IS NULL;
UPDATE tb_refresh_tokens SET revoked_at = now() WHERE revoked_at IS NULL;
SELECT count(*) AS revoked_now FROM tb_refresh_tokens_revoked_20261106;   -- check, then COMMIT (or ROLLBACK)
COMMIT;
```

**What users experience:** an open session keeps working only until its 15-minute access token ends (immediately, if the JWT key was already rotated and the API restarted). After that the app tries to refresh, gets 401, and sends the user to the login page. Their passwords still work: they log in again and carry on. Nothing is lost.

**Rollback** (only if you ran it by mistake; it brings the old sessions back, so do not use it after a leak):

```sql
UPDATE tb_refresh_tokens t SET revoked_at = NULL
  FROM tb_refresh_tokens_revoked_20261106 r WHERE t.refresh_token_id = r.refresh_token_id;
DROP TABLE tb_refresh_tokens_revoked_20261106;   -- when you no longer need the list
```

(A refresh token that is presented after it was revoked also makes the API revoke all of that user's tokens and log "Refresh token reuse detected": expected, harmless.)

## 2. Passwords

Passwords are BCrypt hashes. `tb_users.is_first_login = true` makes the API answer 403 "Password change required" to everything except changing the password, until the user sets a new one (checked on every request, so it also stops sessions already open). The change needs the *current* password, so it does **not** help against someone who already knows the old password: they could change it first.

Since 2026-11 an admin can reset a password in the app (no SQL needed): `POST /api/PasswordReset { loginId, reason }`, from the Reset Password action on the employee profile, the student profile and the Super Admin school list. A Super Admin resets a School Admin; a School Admin resets any other login of their own school (never a Super Admin, never their own). It sets a random temporary password (shown once), forces a password change at the next sign-in, ends all of that person's sessions, clears their sign-in throttle (section 2a) and writes one row to `tb_password_reset_audit` (who, whom, when, why; never the password). There is still **no** self-service "forgot password" (almost no login has an email on file), and nobody can reset a Super Admin's password in the app, so for those the SQL below is the way.

Make a new temporary password hash (tested against the app's login; `pip install bcrypt` once):

```
python -c "import bcrypt; print(bcrypt.hashpw(b'PUT-A-NEW-TEMP-PASSWORD-HERE', bcrypt.gensalt(11)).decode())"
```

Then, for ONE login at a time, in `psql` (it forces a change at the next login):

```sql
UPDATE tb_users SET password_hash = '<the $2b$11$... hash>', is_first_login = true
 WHERE org_user_id = '<login id, e.g. SUAD01 or the school id>' AND school_id IS NOT DISTINCT FROM '<school id, or NULL for the Super Admin>';
-- then revoke that user's sessions:
UPDATE tb_refresh_tokens SET revoked_at = now() WHERE revoked_at IS NULL AND user_id = (SELECT user_id FROM tb_users WHERE org_user_id = '<login id>');
```

To only *force a change* without resetting (accounts whose password you trust nobody knows):

```sql
UPDATE tb_users SET is_first_login = true WHERE school_id = '<school id>';
```

Which logins exist (real database, counts only): Super Admin SUAD01 (1); Akshay Public School: School Admin 1, Employee 1, Student 1; Little Angels School: School Admin 1, Employee 24, Student 39; Green Hills School: School Admin 1, Employee 24, Student 39. The Little Angels and Green Hills logins are the invented demo schools: their School Admin password (`Admin@Demo26`) is in `scripts/demo-seed/seed_setup.py` and the temporary passwords of their staff and students were in `Docs/demo-credentials.txt`, both public, so treat all of those logins as known to anyone. If the demo schools are not meant to be reachable from outside, nothing more is needed; if they are, reset the School Admin logins or deactivate the schools.

Schools that appear in the dumps in the history (names only): Akshay Public School, ABC Public School, First Test School, GSSS Bhojnagar, Test School, ZZ Role Test School, ZZ Test School (renamed), plus the platform's own "SMSS Admin" record. The dumps hold their users' password hashes, and (where the dump has them) students, guardians and employees. Decide who must be told; only Akshay Public School exists in the current database.

## 2a. Sign-in throttling and the client address

`POST /api/Auth/login` counts FAILED sign-ins in a sliding window (appsettings `LoginThrottle`, defaults: 5 per login id and 30 per client address in 15 minutes). Over the limit the API answers 429 with a `Retry-After` header, the same for an id that exists and one that does not; nothing is locked for good. A right password resets that id's count, and an admin password reset clears it.

**Where the client address comes from** (verified against the code, `Program.cs` and `AuthController`): the throttle uses `HttpContext.Connection.RemoteIpAddress`, read AFTER `app.UseForwardedHeaders()` (which runs first in the pipeline, before authentication and the controllers). So:

- **Kestrel exposed directly** (no proxy): the address is the TCP peer. `X-Forwarded-For` is ignored for any caller that is not loopback, so a client cannot choose its own address.
- **Behind a reverse proxy / load balancer**: `X-Forwarded-For` and `-Proto` are trusted ONLY when the request comes from an address in `ForwardedHeaders:KnownProxies` (IPs) or `ForwardedHeaders:KnownNetworks` (CIDR ranges); `ForwardLimit` (default 1) is how many hops are read. **Both lists are empty by default.** If you deploy behind a proxy and do not list it, every visitor looks like the proxy's address, they all share ONE per-address bucket, and 30 failed sign-ins from anyone throttle sign-in for everybody for 15 minutes. List the proxy before going live, then check with two different clients that they are counted separately.
- IPv6 clients are grouped by their /64 (one subscriber normally gets a whole /64); an IPv4-mapped IPv6 address counts as its IPv4 address.
- The per-login-id limit does not depend on the address at all, so it works even when the address cannot be told apart.

**State is in memory**: it is lost when the API restarts (a restart clears every throttle) and each API instance counts on its own, so with N instances behind a load balancer the effective limits are up to N times higher. If you ever run more than one instance, move the counters to a shared store.

To check it on a test system: sign in with a wrong password six times; the sixth answer is 429 with `Retry-After: 900`. The same public enquiry form limit (`RateLimiting:PublicEnquiries`) uses the same address rule.

## 3. Rewriting the git history

A rewritten copy of the history has been prepared and verified in `E:\SecurityBackup`:

- `smss-original-mirror.git`: an untouched backup of everything (all branches, the remote-tracking refs). **Keep it until you are completely sure.** It still contains the secrets and dumps, so store it offline and delete it afterwards.
- `smss-rewritten.git`: the rewritten history: no database dump, no uploaded file, no `Docs/demo-credentials.txt`, and the four secret values replaced by `REDACTED...` in every commit. Branches: `main`, `oct_21`, `03_Nov`, `05_Nov`. Commit hashes ALL change (a rewrite changes every commit that follows the first edited one); messages, authors and dates are kept. Commits that only added or deleted dumps disappeared (the "DB Backup" ones).

If you commit anything new before pushing, it is not in the prepared copy: either do not commit until the push is done, or redo the preparation (run `python scripts/prepare-history-rewrite.py` and then `python scripts/verify-history-rewrite.py` (both leave the original untouched and push nothing); run them while the old secrets are still in user-secrets, because it needs the old values to find them).

### 3a. Before you start

1. Rotate the secrets (section 0). 2. Make the repository **private** in GitHub (Settings, Danger Zone). 3. If `main` is a protected branch, allow force pushes for the moment. 4. Close editors and the Visual Studio solution that use the working copy.

### 3b. Force-push the rewritten history (from the prepared mirror)

```
cd E:\SecurityBackup\smss-rewritten.git
git remote add origin https://github.com/AkshayUpadhayay0/smss.git
git push --force origin refs/heads/main refs/heads/oct_21 refs/heads/03_Nov refs/heads/05_Nov
```

`main` and `oct_21` replace the remote's branches. `03_Nov` and `05_Nov` do not exist on the remote today: this creates them, which is also what keeps your **30 local-only commits** (they sit on `05_Nov`, plus the few made since) safe. They are pushed for the first time, already rewritten. If you push only `main`, those commits would exist only in the mirror and in your old working folder.

### 3c. Replace your working copy (do not "pull"; the histories are unrelated now)

```
# keep the old folder as a safety net and keep everything that is not in git
cd E:\05_Smss
ren smss smss-old
git clone https://github.com/AkshayUpadhayay0/smss.git smss
cd smss
git switch 05_Nov
git config core.hooksPath .githooks          # switch the secret-scan hook on again
```

Copy these from `smss-old` into the new `smss` (they are ignored or uncommitted, so a clone does not bring them):

- `smss-api\smss-api\uploads` and `smss-api\smss-api\uploads-private` (the real photos and documents)
- `scripts\demo-seed\state_*.json` (if you want to re-run the demo seeds)
- `Docs\exam-report-card-design.md` and `Docs\smss-db_nck.sql` (untracked), and the uncommitted edits to `Docs\roles-workflow.md`
- `smss-web\node_modules` is not needed (run `npm ci`).

User-secrets live in your Windows profile, so they stay. Check: `dotnet user-secrets list --project smss-api/smss-api`.

Then verify the clone: `git log --oneline | wc -l`, and `gitleaks git . --config .gitleaks.toml --log-opts="--all" --redact` (expect no leaks).

### 3d. What the rewrite cannot do

- Anyone who cloned or forked while the repository was public still has the old history, and GitHub keeps the old commits reachable by hash (and in pull-request refs) for some time. Contact GitHub Support ("remove cached views and references to sensitive data") with the repository name and the old commit hashes. Deleting the repository and re-creating it from the rewritten mirror removes the old objects completely and is the cleanest option if you do not need the existing stars, issues or pull requests.
- The old secrets and the data in the dumps stay compromised. The rewrite only stops new exposure.
