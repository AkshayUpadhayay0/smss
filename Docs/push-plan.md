# Push plan (prepared, NOT run)

Do nothing in this file until the GitHub repository is **private** and every secret is **rotated**. Nothing here has been run against the real repository. The rewrite and the verification were proven only on a scratch clone (331 commits in, 316 out, all checks passed). Background: `Docs/security-runbook.md` (sections 0 to 3), `Docs/deploy-order.md` (database scripts in order).

## 0. Before anything (you, not scripts)
1. Repository set to **Private** on GitHub (Settings, Danger zone). Pause branch protection only if it blocks the force push (step 5); switch it back on in step 8.
2. Rotate every secret that was ever in the history (runbook sections 0 to 2): the database password, `Jwt:Key`, the Gmail app password, the super admin setup key, any WhatsApp/Meta tokens. Put the NEW values in user-secrets / environment variables only. Restart the API. Revoke all refresh tokens (everyone signs in again), as in the runbook.
3. Make sure `smss-api` is **not running from the folder** you are about to replace, and close the Visual Studio solution.
4. Save the remote state first: `git ls-remote origin > E:\SecurityBackup\remote-refs-before.txt`.

## 1. Rewrite on a scratch mirror (does not touch the real repository)
```
cd E:\05_Smss\smss
git fetch origin
python scripts/rewrite-history.py E:\05_Smss\smss E:\SecurityBackup\rewrite-<date>
python scripts/verify-rewritten-history.py E:\SecurityBackup\rewrite-<date>\rewritten.git E:\SecurityBackup\rewrite-<date>\original.git
```
Needs `pip install git-filter-repo`. The first script only clones FROM the working copy. It removes `Docs/demo-credentials.txt`, `Docs/smss-db*`, `Docs/~$*`, `smss-api/smss-api/uploads`, `uploads-private`, every `.vs` folder, and replaces every secret value that any tracked appsettings file ever held (read from the history itself; never printed) with `REDACTED-SECRET` (a connection-string password only after `Password=`). Remote-only branches (`oct_21`) become branches; remote refs and the origin link are dropped. If a secret was never committed in a tracked config file but must go, put it on one line in a text file and add `--extra-secrets-file <file>`.

**Stop unless the verify script prints `RESULT: ALL CHECKS PASSED`** (banned paths, old secret values in files and commit messages, key/token patterns, same branches, tip-tree differences listed per branch, `git fsck`). Expected tip differences: `main` and `24Nov` differ only in `appsettings.json`; `oct_21` loses the 7 dump/upload files; `24_nov` is identical. Optional second opinion: `gitleaks git <rewritten.git> --no-banner`.

`original.git` still holds every secret. Keep it offline (not in a synced folder), delete it only after step 9.

## 2. Where `24_nov` and its commits go
All branches are rewritten in the same pass, so `24_nov` already exists in `rewritten.git` with the same content and the same order; **only the hashes change** (every hash below is the OLD hash). Find a boundary in the new history by subject, never by old hash:
```
git --git-dir=<rewritten.git> log --oneline --grep="<first words of the subject>" 24_nov
```
Nothing needs to be re-created by hand. Re-run `git log --oneline main..24_nov` in the rewritten mirror and check it lists the same subjects as before.

## 3. Check the rewritten mirror really builds (scratch)
```
git clone E:\SecurityBackup\rewrite-<date>\rewritten.git E:\SecurityBackup\verify-clone
cd E:\SecurityBackup\verify-clone; git checkout 24_nov
```
Run the usual scratch builds and tests from this clone (`dotnet build`, `npm ci && ng test`, `npm ci && npm test` in smss-mobile). `TrackedConfigHasNoSecrets` must pass. It will not have the ignored files (uploads, user-secrets), which is expected.

## 4. Add the remote to the rewritten mirror
```
cd E:\SecurityBackup\rewrite-<date>\rewritten.git
git remote add origin https://github.com/AkshayUpadhayay0/smss.git
```

## 5. Force push (this is the irreversible step; only with your word)
Order matters; `main` first, since the PRs are opened against it.
```
git push --force origin main
git push --force origin oct_21
git push --force origin 24Nov          # only if this branch exists on GitHub; otherwise plain push
git push origin 24_nov                # new push
```
Then the PR branches of step 7 (plain pushes, new names). Do **not** use `--mirror`; do not push `refs/pull/*`. Afterwards `git ls-remote origin` must list only the branches you meant to publish. Rollback is possible only while the repo is private and nobody has fetched: push the old refs again from `original.git` using the saved `remote-refs-before.txt`.

## 6. Verify the remote
```
git clone https://github.com/AkshayUpadhayay0/smss.git E:\SecurityBackup\remote-check
python scripts/verify-rewritten-history.py E:\SecurityBackup\remote-check\.git E:\SecurityBackup\rewrite-<date>\original.git
```
(For a normal clone the script reads `refs/remotes/origin/*` as extra refs; if it complains, run it on `git clone --mirror`.) Also search the repo on GitHub for a secret fragment and for `smss-db`: expect nothing.

## 7. GitHub Support purge request
GitHub keeps old commits reachable by hash and by `refs/pull/*`, and caches views of them. Open a request at support.github.com (Contact, "Sensitive data removal"), text:

> Repository: AkshayUpadhayay0/smss (private). I removed sensitive data (credentials, a database dump and uploaded files) from the full history with git-filter-repo and force-pushed on <date>. All credentials in it have been rotated. Please (1) run garbage collection on the repository so unreachable objects are deleted, (2) remove cached views of the old commits and any references to them, including `refs/pull/*/head` and `refs/pull/*/merge` of closed pull requests, and (3) confirm when done. Old commit SHAs that must no longer resolve: <paste the hashes from `remote-refs-before.txt` and, for any pull request that existed, its head SHA>. The repository has no forks, and I have not shared it with anyone outside the team.

Tell me if a fork exists; a fork keeps the old objects and must be deleted.

## 8. After the push
Re-enable branch protection, turn on secret scanning and push protection (Settings, Code security), confirm CI runs on `main`. Delete `E:\SecurityBackup\...\original.git` and `remote-check` when you are satisfied (the backup of the old state is useful only until then).

## 9. What collaborators must do
Do **not** `git pull` or merge: that would bring the old history back. Everyone: commit or `git format-patch` local work, then
```
git clone https://github.com/AkshayUpadhayay0/smss.git smss-new
```
Re-apply the saved work in the new clone: `git am` the patches, or `git cherry-pick` (the old and new histories are unrelated, so rebase and merge do not work across them). Delete the old clone afterwards. Nobody pushes from an old clone.

## 10. Your own folders (Visual Studio and Angular)
1. Close Visual Studio, VS Code, `ng serve`, Metro, the API.
2. Rename the old folder: `E:\05_Smss\smss` to `E:\05_Smss\smss-old`.
3. `git clone https://github.com/AkshayUpadhayay0/smss.git E:\05_Smss\smss` and `git checkout 24_nov`.
4. Copy back what git never had (from `smss-old`): `smss-api\smss-api\uploads`, `smss-api\smss-api\uploads-private`, `scripts\demo-seed\state_*.json`, your untracked `Docs\*.md` files (admission-docs-latefee-registration-design, guardian-portal-design, stage-2/4/5-6/7-8 build logs, student-year-promotion-design), `smss-mobile\.env`, `smss-mobile\android` (or run prebuild again), any local `appsettings.Development.json` overrides.
5. `git config core.hooksPath .githooks` (the secrets pre-commit hook).
6. `npm ci` in `smss-web` and `smss-mobile`. Delete `.angular`, `dist`, `bin`, `obj` and any `.vs` folder, then reopen the solution (Visual Studio rebuilds `.vs`).
7. User-secrets live in your Windows profile and are keyed by project, so they survive; set the NEW rotated values (`dotnet user-secrets set ...`). `appsettings.json` now holds no secrets.
8. Run the API and the web app once, sign in, open a school logo; then delete `smss-old` (it holds the old history) after copying anything missing.

## 11. Pull request split
Merge **in this order**; each PR is opened against `main` (stacked: PR n+1 is based on PR n and retargeted to `main` after PR n merges). Cut the branches in the rewritten mirror by subject (step 2): `git branch pr-1 <new hash of the PR's last commit>` and so on. Scripts live in `Docs/`; run each on the target database with the usual gate (pg_dump to `E:\SmssBackups` first, violators query, run twice, second run prints ALREADY MIGRATED) **before** deploying the code that needs it (code that maps a table fails at start without it).

| # | Commits (old hashes) | Scripts it needs |
|---|---|---|
| 1 | `57b4534` | none |
| 2 | `2075f71` .. `8d9ab16` (Stage 1) | smss-yc-1a, smss-yc-1b |
| 3 | `6e9272a` .. `fa93fab` (Stage 2) | smss-st-2a, 2b, 2c, 2d |
| 4 | `bbe16d5` .. `470fc0d` (Stage 3) | smss-st-3a, 3d |
| 5 | `47892fc` .. `44e8495` (Stages 4a, 4b) | smss-st-4b1, 4b4 |
| 6 | `2f81870` .. `3d5fa85` (Stage 5) | smss-st-5a1 |
| 7 | `a444df6` .. `74a5c9c` (Stages 6, 7a) | smss-st-6a |
| 8 | `0882263`, `0b06ae2` (Stage 7b mobile, deploy docs) | none |
| 9 | `d8c86c6`, `1649be8`, `9ea0176`, `6e58140`, `1ddcb9f` (Stage 8) | smss-st-8a, 8b, 8b2, 8c |
| 10 | `49bf361`, `f21ccae`, `c633624`, `6e4ff35`, this plan | none |

(The S1 to S7 academic-year scripts are already on `main`. The full ordered list of the 30 scripts is `Docs/deploy-order.md`.) The old hashes help only to find the subjects; in the rewritten history use `git log --grep`.

### Test baselines (backend passed / web / mobile)
After PR 6: backend 2551+; PR 7 (6b, 6c, 7a): 2648, web 1748; PR 8: mobile 235; PR 9: backend 2697 (21 old failures, fixed in PR 10), web 1772; PR 10 final: **0 failed / 2726 passed / 19 skipped**, web 1773, mobile 236. Backend tests need the scratch Postgres template, never the real database.

### PR 1: Secrets out of tracked configuration
**Title:** Move all secrets out of tracked config (config keys required)
**Description:** `appsettings*.json` no longer hold any secret; the API reads them from user-secrets / environment variables. New required keys: `ConnectionStrings__DefaultConnection`, `Jwt__Key`, `Email__SenderAppPassword`; `Bootstrap__SuperAdminSetupKey` is empty by default and `Bootstrap__Enabled` is false; `Cors__AllowedOrigins__0..n` (API refuses to start outside Development without it), `ForwardedHeaders__KnownProxies__0..n`, `FileStorage` paths. Test `TrackedConfigHasNoSecrets` fails the build if a secret returns; `.githooks/pre-commit` blocks one locally. **Migration:** none. **Deploy:** set the keys first, then deploy. **Rollback:** redeploy the previous build with the same keys (they are only read, never written).

### PR 2: Stage 1 (year-end, class promotion foundations)
**Title:** Stage 1: year-end foundations
**Description:** tables and APIs of Stage 1. **Migrations:** `smss-yc-1a-RUN-ME.sql`, `smss-yc-1b-RUN-ME.sql`, before deploy. **Config:** none. **Tests:** the Stage 1 test files in `smss-api-tests`; web specs for the screens. **Rollback:** the scripts only add; old code ignores the new tables and columns, so redeploying the previous build is safe; do not drop.

### PR 3: Stage 2
**Title:** Stage 2: <fill from the commit subjects of `6e9272a`..`fa93fab`>
**Description:** **Migrations:** `smss-st-2a`, `2b`, `2c`, `2d` (in that order). **Config:** none new. **Tests:** Stage 2 DB tests (scratch template). **Rollback:** additive only; previous build keeps working.

### PR 4: Stage 3
**Title:** Stage 3: <subjects of `bbe16d5`..`470fc0d`>
**Description:** **Migrations:** `smss-st-3a`, `smss-st-3d`. **Config:** none. **Tests:** Stage 3 tests. **Rollback:** additive; redeploy previous build.

### PR 5: Stages 4a and 4b (promotion)
**Title:** Stage 4: student promotion and year change
**Description:** **Migrations:** `smss-st-4b1`, `smss-st-4b4`. 4b4 changes data rules for enrolments: take the backup and read the violators output before running. **Config:** none. **Tests:** promotion tests (the source-scan test lists the code paths allowed to write enrolments). **Rollback:** restore from the pre-migration dump if a promotion was run; otherwise additive.

### PR 6: Stage 5
**Title:** Stage 5: <subjects of `2f81870`..`3d5fa85`>
**Description:** **Migration:** `smss-st-5a1`. **Config:** none. **Tests:** Stage 5 tests (report card designs, demo seed stays unpublished). **Rollback:** additive.

### PR 7: Stages 6 and 7a (fees, parent accounts, WhatsApp)
**Title:** Stages 6 and 7a: <fee reminders, parent accounts, WhatsApp>
**Description:** **Migration:** `smss-st-6a`. **Config keys:** `WhatsApp__*` (access token, app secret, webhook verify token, phone number id; secrets only in user-secrets / env), `ParentAccounts__*`, `RateLimiting__*` new policies. **External setup:** Meta app and webhook URL registered before enabling; leave `WhatsApp` disabled until then (the feature fails closed). **Tests:** backend 2648, web 1748. **Rollback:** disable the WhatsApp flag; previous build ignores the new tables.

### PR 8: Stage 7b mobile and deploy docs
**Title:** Stage 7b: mobile app screens and deploy docs
**Description:** Expo app changes and `Docs/deploy-order.md`. **Migration:** none. **Config:** `smss-mobile/.env` API URL (not committed). **Tests:** mobile 235. **Rollback:** revert the PR; no server impact. No app version, package or store changes.

### PR 9: Stage 8 (leaving, transfer certificates, section change)
**Title:** Stage 8: student leaving, transfer certificates, mid-year section change
**Description:** 8a records leaving (one shared live-enrolment rule; `tb_students.status_id` is never changed by leaving; leavers cannot sign in), 8b issues numbered transfer certificates (`<school code>/TC/<year>/<4 digits>`, immutable snapshot, PDF), 8c changes a student's section mid-year with history, capacity and roll checks, 8d web screens. **Migrations, in order:** `smss-st-8a-RUN-ME.sql`, `smss-st-8b-RUN-ME.sql`, `smss-st-8b2-RUN-ME.sql` (school affiliation / UDISE / board code), `smss-st-8c-RUN-ME.sql`; all before deploy. **Config:** none. **Tests:** backend 2697 passed (21 old failures, fixed in PR 10), web 1772, mobile 236. **Rollback:** the code needs the scripts but the scripts are additive; to roll back code redeploy the previous build; leaving records and certificates stay in their tables (do not drop; certificates are legal records).

### PR 10: Hardening
**Title:** Hardening: endpoint authorization guards, attendance for moved students, test fixes
**Description:** automated guard that every endpoint has an explicit authorization (plus a golden role matrix), the monthly attendance report and the report-card term attendance now count a moved student's days from the attendance rows' own section, Document Types shows "Required from", 21 old failing tests fixed (all test or environment problems), `Docs/push-plan.md`, history-rewrite scripts. **Migration:** none. **Config:** none. **Tests:** backend 0 failed / 2726 passed / 19 skipped, web 1773, mobile 236. **Rollback:** revert; no data change.

Each PR description ends with the usual generated-by line if the PR was opened from Claude Code.
