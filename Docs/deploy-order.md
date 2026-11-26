# SMSS deploy order: migrations, settings, builds, and the pre-push checklist

Written 26 Nov 2026 for branch `24_nov` (head `0882263`, Stage 7b). It extends `Docs/DEPLOY-CHECKLIST.md` (the production steps) and `Docs/configuration.md` (every setting). Nothing here contains a secret. **Nothing in this file was run on the real database.**

## 1. What was proven, and how

- A scratch database was restored from `Docs/smss-db(14Nov).sql` (the baseline a server is assumed to start from), then every script in section 2 was run in the order shown, twice, with `ON_ERROR_STOP=1`. Run 1 and run 2 finished with exit code 0 and no error for all 26 scripts (the inventory script with `--single-transaction`).
- The result was compared with the local development database `smss-db` (read-only queries): **identical column list (1659 columns) and the same number of constraints (2176)**. So the scripts below are the complete set between the 14Nov dump and today's schema; nothing else is missing.
- The same read-only probe shows **every script in section 2 is already applied on `smss-db`** (the local development database). A server database restored from the 14Nov dump has none of them.

## 2. Migration order on a server database

Run each with `psql -h <host> -U <user> -d <db> -v ON_ERROR_STOP=1 -f Docs/<file>` (pgAdmin's Query Tool also works for the RUN-ME files: each is one transaction with no psql commands). Back up first (`pg_dump -Fc`). Every script is idempotent (a second run changes nothing) and has pre-checks that stop BEFORE changing anything.

| # | Script | Needs first | Applied on smss-db | First API stage that needs it |
|---|---|---|---|---|
| 1 | `smss-fee-tax-types-RUN-ME.sql` | | yes | tax on payments |
| 2 | `smss-fee-concession-at-payment-RUN-ME.sql` | 1 (its own pre-check stops without it) | yes | concession at payment |
| 3 | `smss-fee-effective-from-RUN-ME.sql` | its header asks for 1 and 2 | yes | fee effective-from, back-dating |
| 4 | `smss-fee-line-removal-log-RUN-ME.sql` | | yes | fee line removal log |
| 5 | `smss-inventory-stage1.sql` (follow `Docs/inventory-stage1-apply.md`: pre-check, `--single-transaction`, post-check) | | yes | inventory |
| 6 | `smss-push-notifications.sql` | | yes | push notifications |
| 7 | `smss-partner-schools.sql` | | yes | partner schools |
| 8 | `smss-acad-s1-years-RUN-ME.sql` | | yes | S1 |
| 9 | `smss-acad-s2-offerings-RUN-ME.sql` | S1 | yes | S2 |
| 10 | `smss-acad-s3-subjects-RUN-ME.sql` | S1, S2 | yes | S3 |
| 11 | `smss-acad-s4-schedules-RUN-ME.sql` | S1, S2 (S3 for the web build) | yes | S4 |
| 12 | `smss-acad-s5-class-teachers-RUN-ME.sql` | S1, S2 | yes | S5 |
| 13 | `smss-acad-s6-timetables-RUN-ME.sql` | S1 to S5 | yes | S6 |
| 14 | `smss-acad-s7-year-copy-RUN-ME.sql` | S1 to S6 | yes | S7 |
| 15 | `smss-yc-1a-RUN-ME.sql` | S1, S2 | yes | Stage 1a (enrolment keys, roll uniqueness, next class) |
| 16 | `smss-yc-1b-RUN-ME.sql` | none (same release as 1a) | yes | Stage 1b (per-year concessions, receipt name snapshot) |
| 17 | `smss-st-2a-RUN-ME.sql` | | yes | 2a (last name optional) |
| 18 | `smss-st-2b-RUN-ME.sql` | | yes | 2b (WhatsApp numbers, school location) |
| 19 | `smss-st-2c-RUN-ME.sql` | | yes | 2c (two-step admission) |
| 20 | `smss-st-2d-RUN-ME.sql` | | yes | 2d (student documents, `required_from`) |
| 21 | `smss-st-3a-RUN-ME.sql` | | yes | 3a to 3c (guardians) |
| 22 | `smss-st-3d-RUN-ME.sql` | 3a (stops with a message if missing) | yes | 3d (merge audit) |
| 23 | `smss-st-4b1-RUN-ME.sql` | enrolments (yc-1a) | yes | 4b (promotion) |
| 24 | `smss-st-4b4-RUN-ME.sql` | 4b-1 (stops with a message if missing) | yes | 4b-4 (correction) |
| 25 | `smss-st-5a1-RUN-ME.sql` | tax types (1) | yes | 5a to 5c (late fee) |
| 26 | `smss-st-6a-RUN-ME.sql` | guardians (3a), users | yes | 6a to 7b (parent accounts, messaging) |
| 27 | `smss-st-8a-RUN-ME.sql` | 4b-1 (outcome columns) | yes (26 Nov, backup taken, two runs, fingerprint clean) | 8a (record leaving) |
| 28 | `smss-st-8b-RUN-ME.sql` | 8a | yes (26 Nov, backup taken, two runs, fingerprint clean) | 8b (transfer certificates; the 8a correction also reads its table) |
| 29 | `smss-st-8b2-RUN-ME.sql` | none | yes (26 Nov, backup taken, two runs, fingerprint identical) | 8b follow-up (three optional school fields: affiliation number, UDISE code, board school code) |

Stage 8 scripts 27 to 30 were added after the scratch proof in section 1; it was run twice on a scratch copy of the 6a template (second run prints ALREADY MIGRATED).

Stages 3b, 3c, 3e, 4a-1, 4a-2, 4b-2, 4b-3, 5a-2, 5b, 5c, 6b, 6c, 7a and 7b have **no migration**.

**Two differences from `Docs/DEPLOY-CHECKLIST.md` section 1** (that table numbers the first seven scripts as tax, effective-from, removal-log, concession): the order above puts the concession script before effective-from, because the effective-from header says to run tax types and concession first. This order was run twice on the scratch copy, as shown in section 1.

**Rules**
- Run ALL scripts before the first start of any API build from `24_nov`: the entities map the new tables and columns. Deploy the API only after the last script finished clean.
- Do not skip a number. Do not run the older copies (`smss-admission-type-global.sql`) or the destructive ones (`...-drop-old.sql`, `smss-drop-legacy-school-subscription.sql`, `smss-student-id-varchar.sql`). `smss-st-3-dryrun.sql` and `smss-st-4a-dryrun.sql` are read-only helpers.
- Already inside the 14Nov dump (no need to run): enquiries, password-reset audit, transport, timetable, leave, homework, assessments, notices, report cards, subscriptions, employees, attendance, the global admission type.
- If a script raises, nothing was changed (single transaction). Read the message; do not re-run blindly.

Quick check after the last script (all must be non-null / one row each):
`select to_regclass('public.tb_message_deliveries'), to_regclass('public.tb_promotion_corrections'), to_regclass('public.tb_year_copy_log'), to_regclass('public.tb_tax_types'), to_regclass('public.tb_inv_items'), to_regclass('public.tb_user_devices');`

## 3. Settings and secrets (user-secrets in Development, environment variables on a server; never in git)

`__` in an environment variable name stands for `:`. The committed `appsettings.json` holds no secret values; with `Production` and a blank secret the API refuses to start and names the missing setting. Full table: `Docs/DEPLOY-CHECKLIST.md` section 2 and `Docs/configuration.md`.

**Secrets**
| Setting | Used for |
|---|---|
| `ConnectionStrings__DefaultConnection` | database |
| `Jwt__Key` (32+ random characters) | token signing |
| `Email__SenderAppPassword` | SMTP password (Gmail app password) |
| `Bootstrap__SuperAdminSetupKey` | only while Bootstrap is on; leave empty |
| `WhatsApp__AccessToken` | Meta permanent System User token |
| `WhatsApp__AppSecret` | verifies Meta's webhook signature (without it the webhook answers 404) |
| `WhatsApp__WebhookVerifyToken` | the string typed in Meta's dashboard when the webhook is registered |
| `Firebase__CredentialsPath` | path to the service-account file (secret file) |

**New since the 14Nov baseline (not secret)**
| Setting | Value |
|---|---|
| `ParentAccounts__WebBaseUrl` | the website origin, e.g. `https://smss-erp.digital` (empty = parent links cannot be built) |
| `ParentAccounts__SetPasswordLinkHours` / `ResetLinkMinutes` / `ResendLimitPer24Hours` | 72 / 60 / 3 |
| `ParentAccounts__Channels__<PURPOSE>` | optional per-purpose channel (`WHATSAPP`, `EMAIL`, `WHATSAPP,EMAIL`); purposes `SET_PASSWORD`, `RESET_PASSWORD`, `CHILD_ADDED`, `STUDENT_LOGIN`. Use `EMAIL` for a purpose whose Meta template is not approved yet |
| `WhatsApp__Enabled` | `false` until Meta is ready (nothing is sent; a `LOGGED` row is written) |
| `WhatsApp__Sender` | `Log` or `Cloud` (`Cloud` also needs Enabled, AccessToken and PhoneNumberId) |
| `WhatsApp__PhoneNumberId`, `WhatsApp__BusinessAccountId` | the platform's default sending number and the business account |
| `WhatsApp__ApiVersion`, `BaseUrl`, `Language`, `TimeoutSeconds` | defaults `v21.0`, Meta Graph URL, `en`, 15 |
| `WhatsApp__TemplateParentAccountReady`, `...ParentChildAdded`, `...ParentLoginHelp`, `...ParentStudentLoginReady` | template names as approved |
| `RateLimiting__ParentAuth__PermitLimit` / `WindowMinutes` | 10 per 10 minutes per address |
| `Cors__AllowedOrigins__0` ... | website origin(s); the API will not start outside Development without one |
| `ForwardedHeaders__KnownProxies__0` | the reverse proxy / load balancer address |
| `FileStorage__RootPath`, `FileStorage__PrivateRootPath` | persistent folders (logos; private files: photos, documents, attachments) |
| `Bootstrap__Enabled` | `false` |

**Meta (WhatsApp Business Cloud)**: owner's tasks, in this order: verified business; register the sending number; create the permanent System User token; submit the four templates (texts and variables in `Docs/whatsapp-templates.md`) with the real website address in place of `https://YOUR-SITE`; pay attention to template category (a re-categorised template means that purpose goes by email via `Channels` until approved); register the webhook `https://<api host>/api/webhooks/whatsapp` with the verify token; subscribe it to message status events; add a payment method. Then set `WhatsApp__Enabled=true` and `WhatsApp__Sender=Cloud`.

**SMTP**: `Email__SmtpHost`, `Email__SmtpPort`, `Email__SenderEmail`, `Email__SenderName`, `Email__SenderAppPassword`, `Email__LoginUrl` (the sign-in page). Parent set-password and reset links also go by email, so SMTP must work before parent accounts are created.

**Public reachability**
- The API host must be reachable from the internet over HTTPS for Meta's webhook (`GET/POST /api/webhooks/whatsapp`, anonymous but signature-checked on every POST; the proxy must pass the raw body and the `X-Hub-Signature-256` header unchanged and allow bodies of at least 256 KB).
- The website must serve `/parent/set-password`, `/parent/forgot` and the other signed-out parent pages as client-rendered pages (unknown URLs fall back to `index.csr.html`). The proxy must not log query strings for `/parent/*` (the one-time token is in the address).
- `ParentAccounts__WebBaseUrl` and the website origin in `Cors__AllowedOrigins__0` must be the same site.

## 4. Builds

**API**: `dotnet publish smss-api/smss-api/smss-api.csproj -c Release -o <folder>`. Tests (baseline): 21 failed / 2648 passed / 19 skipped / 2688 total; the 21 are old and unrelated (12 push notification tests, 2 each in academic setup S2 and class teachers S5, 1 each in tax types, concession at payment, timetables S6, subjects S3, schedules S4).

**Angular** (`smss-web`): set `siteUrl` and `apiUrl` in `src/environments/environment.ts`; set `DEVELOPER_NAME` in `core/config/site.config.ts` (the build stops until you do); `npm ci`, then `npm run build` (never bare `ng build`). Upload `dist/smss-web/browser/`; serve with compression; unknown URLs fall back to `index.csr.html`. 1748 unit tests at the last run. The privacy and terms text is still marked "to be reviewed", and must mention WhatsApp and email contact and guardian data before real parent messages go out.

**Mobile** (`smss-mobile`): `eas.json` `preview` and `production` profiles carry `EXPO_PUBLIC_API_BASE_URL=https://api.smss-erp.digital` and `EXPO_PUBLIC_PRIVACY_POLICY_URL=https://smss-erp.digital/privacy-policy`; change them if the domains differ. The parent app's "Set up your account" and "Forgot ID or password" links are built from the website origin of the privacy-policy address, so that variable must be set or the links are hidden. `EXPO_PUBLIC_DUTY_ACCURACY=high` in those profiles is ignored in release builds (the code checks `__DEV__`). The application id `com.smsserp.mobile` and `versionCode` 1 are permanent after the first store build: the owner confirms them first. `google-services.json` is tracked: confirm it is the client configuration intended for the repository. Play Console: Privacy Policy URL, the Data safety form (now including guardian data) and the location declarations must match the policy. 235 unit tests; the router guards, the PDF share and the web links need a device run.

## 5. Pre-push checklist (status as found on 26 Nov 2026)

Branch state: `24_nov` is 38 commits ahead of `origin/main` (`0dfe4f8`); `origin/oct_21` also exists. **Nothing from `24_nov` has been pushed.**

| Check | Status found | What to do |
|---|---|---|
| Repository visibility | `https://github.com/AkshayUpadhayay0/smss` answers **200 to an anonymous request, so it is public today** (`gh` is not installed here, so this is the only check made). `Docs/security-runbook.md` records that it was found public with secrets and dumps in its history on 6 Nov, and that the history rewrite was prepared but "nothing has been run". | Make it private BEFORE anything else (GitHub: Settings, Danger Zone). Confirm by opening the URL in a signed-out window. |
| Secrets in history | The four values currently in the development user-secrets (JWT key, database connection string, setup key, Gmail app password) were each searched for in all history (`git log --all -S`, values never printed): the JWT key, setup key and app password are still in commits `d085d8a`, `519ce96`, `755896f`; the connection string appears in 8 commits including `57b4534` (the Stage S commit on `24_nov`, as the removed line). So **they have not been rotated**, or the same values are still in use. | Rotate all four (Docs/configuration.md "Rotating a secret"), restart the API, revoke refresh tokens (`Docs/security-runbook.md` section 1), then the history rewrite (section 3). Treat the old values as public whatever the rewrite does. |
| Database dumps in history | Dumps `Docs/smss-db(oct01).sql`, `(oct05)`, `(oct09)`, `(oct14)`, `(oct24)`, `(31oct)`, `(oct01_plain_script)` and `smss-db_bk-1.sql` were added in earlier commits and are reachable in history (they hold personal data and password hashes). The current `.gitignore` blocks `Docs/smss-db*.sql`, `*.dump`, `*.bak`, so new ones cannot be added. | They go away only with the history rewrite; until then, private repository plus rotation is the mitigation. |
| Other files to look at | `Docs/DB_Book.xlsx` is tracked (open it and confirm it holds no real data). `smss-mobile/google-services.json` is tracked (client Firebase configuration; confirm it is intended). | Decide before the rewrite. |
| Tracked config is clean now | `appsettings.json` on `24_nov` has blank secret values; `TrackedConfigHasNoSecretsTests` fails the build if a secret-looking key gets a value. | Keep that test. |
| Untracked Docs files (do NOT commit without your say) | `Docs/admission-docs-latefee-registration-design.md`, `guardian-portal-design.md`, `stage-2-build-log.md`, `stage-4-build-log.md`, `stage-5-6-build-log.md`, `student-year-promotion-design.md`. | Read them for personal data or internal notes, then commit, ignore or delete. |
| Authors and email in history | commit author is "Akshay Upadhayay"; the rewrite keeps authors. | Decide if that is acceptable for a public project. |

**Suggested split of `24_nov` into pull requests** (each builds and passes tests on its own, in this order; the migration scripts travel with the first PR that needs them):
1. `57b4534` Stage S: secrets out of tracked config. Merge FIRST and alone (after rotation and the rewrite).
2. `2075f71` to `8d9ab16` Stage 1 (year continuity, year-scoped dashboards) with yc-1a / yc-1b scripts.
3. `6e9272a` to `fa93fab` Stage 2 (last name, WhatsApp numbers, two-step admission, documents) with st-2a to st-2d.
4. `bbe16d5` to `470fc0d` Stage 3 (guardians) with st-3a / st-3d.
5. `47892fc` to `44e8495` Stages 4a and 4b (fee copy, assign fees, promotion) with st-4b1 / st-4b4.
6. `2f81870` to `3d5fa85` Stage 5 (late fee) with st-5a1.
7. `a444df6` to `74a5c9c` Stages 6 and 7a (parent accounts, messaging, family screens) with st-6a.
8. `0882263` Stage 7b (mobile).
A single PR is also possible; the split keeps each review small and puts the schema changes next to the code that needs them.

**Order of work before pushing**: make the repository private; rotate the four secrets; revoke refresh tokens; rewrite history from the prepared mirror (`Docs/security-runbook.md` section 3); re-clone; check the untracked Docs files; push `24_nov`; open the PRs. Then deploy: backup, scripts 1 to 26, settings, API, web, Meta, mobile build.
