# SMSS deploy checklist (production)

Do the steps in order. Nothing here contains a secret: put real values in the server's environment variables or secret store, never in git.
Baseline assumed: the production database was restored from `Docs/smss-db(14Nov).sql`.

## 0. Before anything
- Back up the production database: `pg_dump -Fc -f smss-before-deploy.dump <db>` (keep the file outside the repository).
- **Secrets that are in git history:** the committed `smss-api/smss-api/appsettings.json` currently holds a database password, a JWT signing key, a setup key and an SMTP (Gmail app) password (`docs/configuration.md` says it should be empty: the file was reverted). The API does NOT refuse these values, so on the server you MUST override all four with environment variables (section 2), and you should **rotate** the JWT key and the Gmail app password because they have been committed. Do not run production on the committed values.

## 1. SQL scripts to run on the production database (in this order)
These are the only scripts newer than the 14Nov dump. Each was run twice on a scratch database restored from that dump: the first run made the change, the second made none, and none of the 7 reported an error, so **all seven are idempotent**. Run each with
`psql -h <host> -U <user> -d <db> -v ON_ERROR_STOP=1 -f Docs/<file>` (the inventory script as in step 5). Run them BEFORE starting the new API build, because the new entities map these tables and columns.

| # | Script | Purpose |
|---|---|---|
| 1 | `smss-fee-tax-types-RUN-ME.sql` | Tax types table (`tb_tax_types`) and tax columns on fee payments. |
| 2 | `smss-fee-effective-from-RUN-ME.sql` | "Effective from" on fee lines, installment plan, due source, and `tb_fee_backdating_log`. |
| 3 | `smss-fee-line-removal-log-RUN-ME.sql` | Audit table `tb_fee_line_removal_log` for removed fee lines. |
| 4 | `smss-fee-concession-at-payment-RUN-ME.sql` | Concession snapshot columns on fee payments. |
| 5 | `smss-inventory-stage1.sql` | Inventory and stock tables, lookups and the Store Keeper role. Follow `Docs/inventory-stage1-apply.md` (pre-check with `smss-inventory-stage1-verify.sql -v mode=pre`, run with `--single-transaction`, post-check with `-v mode=post`). |
| 6 | `smss-push-notifications.sql` | Push-notification tables (`tb_user_devices`, `tb_school_notification_settings`, `tb_push_notification_log`). |
| 7 | `smss-partner-schools.sql` | `tb_schools.show_on_website` (default FALSE) and its index, for the Partner Schools section. |

Already in the 14Nov dump, no need to run (re-running changes nothing): enquiries, password-reset audit, transport (all four scripts), timetable, leave, homework, assessments, notices, report cards, subscriptions, employees, attendance and the rest of `Docs/smss-*.sql`.

**Never run on production:**
- `smss-student-id-varchar.sql`: **NOT idempotent** (it drops and recreates student tables; it fails on a database that already has them). Already applied in the dump.
- `smss-admission-type-global-drop-old.sql` and `smss-drop-legacy-school-subscription.sql`: destructive clean-ups, only on request.
- `smss-admission-type-global.sql`: the older copy of the `-RUN-ME` script, not needed.
- `smss-db*.sql` (dumps) and the inventory fingerprint / signature helpers.

After the scripts, check: `select column_name from information_schema.columns where table_name='tb_schools' and column_name='show_on_website';` returns one row, and `select to_regclass('public.tb_user_devices'), to_regclass('public.tb_inv_items'), to_regclass('public.tb_tax_types');` returns three non-null values.

## 2. API settings (environment variables, `__` stands for `:`)
Set these on the server. Never put real values in git.

| Setting | Value / note |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` (turns on the strict start-up checks). |
| `ASPNETCORE_URLS` | the address the API listens on (behind your reverse proxy). |
| `ConnectionStrings__DefaultConnection` | production PostgreSQL connection string (**secret**). |
| `Jwt__Key` | a new random string of 32+ characters (**secret**); `Jwt__Issuer` `smss-api`, `Jwt__Audience` `smss-web`; `Jwt__ExpiryMinutes` 15, `Jwt__RefreshTokenDays` 1, `Jwt__RememberMeRefreshTokenDays` 14 (the mobile app signs in with remember-me, so its refresh token lasts 14 days). |
| `Email__SenderAppPassword`, `Email__SenderEmail`, `Email__SenderName`, `Email__SmtpHost`, `Email__SmtpPort` | SMTP account that sends school login emails (**password is a secret**). |
| `Email__LoginUrl` | the web app's sign-in page, e.g. `https://<site>/login`. |
| `Cors__AllowedOrigins__0` (`__1`, ...) | the website origin(s): `https://smss-erp.digital` and the `www` form if used. Scheme + host only. The API refuses to start without one outside Development, and `*` is rejected. The partner-schools list and the enquiry form fail in the browser without it. |
| `ForwardedHeaders__KnownProxies__0` (or `KnownNetworks__0`) | the address of the reverse proxy / load balancer in front of the API. Without it every visitor shares one rate-limit bucket and the stored IP is the proxy's. `ForwardedHeaders__ForwardLimit` stays 1. |
| `Bootstrap__Enabled` | `false` (a Super Admin already exists). Leave `Bootstrap__SuperAdminSetupKey` empty. |
| `RateLimiting__PublicEnquiries__PermitLimit` / `__WindowMinutes` | default 5 per 10 minutes per address (enquiry form). |
| `RateLimiting__PublicPartnerSchools__PermitLimit` / `__WindowMinutes` | default 60 per minute per address (partner list). |
| `RateLimiting__DriverLocation__PermitLimit` / `__WindowSeconds` | default 12 per 60 seconds (driver location pings). |
| `PartnerSchools__MaxItems` / `__CacheSeconds` | defaults 60 schools, 300 seconds. |
| `LoginThrottle__*` | defaults in the file (5 failures per login id, 30 per address, 15 minutes). |
| `TransportLive__RejectMockLocations` | `true` in production (the committed default; only the Development file turns it off). |
| `FileStorage__RootPath`, `FileStorage__PrivateRootPath` | folders on **persistent storage** that survive deploys: public logos (`uploads`) and private files (`uploads-private`: student photos, documents, attachments). Back up both. |
| `Firebase__CredentialsPath` | path to the Firebase service-account file on the server (**secret file**; never in git). `Firebase__DryRun` is `true` by default, which means push notifications are validated but NOT delivered: set it to `false` only when you are ready for real pushes. |
| `AllowedHosts` | your API host name(s) (default `*`). |

Restart the API after changing settings.

## 3. Web: set values, build, deploy
1. `smss-web/src/environments/environment.ts` (the production build): `siteUrl` = the public website origin (default `https://smss-erp.digital`), `apiUrl` = the API origin (default `https://api.smss-erp.digital`). Both without a trailing slash.
2. `smss-web/src/app/core/config/site.config.ts`: set `DEVELOPER_NAME` to the exact developer name from the Play Console. **`npm run build` stops with a message until you do.**
3. From `smss-web`: `npm ci`, then **`npm run build`** (never bare `ng build`: only `npm run build` generates `robots.txt` and `sitemap.xml`).
4. Upload `dist/smss-web/browser/` to the web host. The host must:
   - serve with gzip or brotli;
   - serve the prerendered pages (`/`, `/contact`, `/request-demo`, `/privacy-policy`, `/terms`) from their own `index.html`;
   - answer every other unknown URL (including `/login` and every signed-in route) with **`index.csr.html`**, not `index.html`;
   - use HTTPS.
5. Check: `https://<site>/privacy-policy` opens without signing in and is the new policy; `https://<site>/robots.txt` and `/sitemap.xml` exist and use your domain.

## 4. API: build and deploy
`dotnet publish smss-api/smss-api/smss-api.csproj -c Release -o <folder>`, copy the folder to the server, set the environment variables (section 2), run the SQL (section 1) first, then start the API. If you run it from Visual Studio locally, stop and restart debugging.

## 5. Mobile app (EAS)
- `eas.json` already sets `EXPO_PUBLIC_API_BASE_URL` and `EXPO_PUBLIC_PRIVACY_POLICY_URL` (`https://smss-erp.digital/privacy-policy`) for the `preview` and `production` profiles. Change them there if your domains differ; the app has a Privacy Policy link on the sign-in screen, the driver disclosure and the profile sheet.
- The application id `com.smsserp.mobile` is permanent after the first store build: confirm it before building for the Play Store.
- Play Console: set the Privacy Policy URL to `https://<site>/privacy-policy`; the Data safety form and the background-location and foreground-service declarations must match the policy (location: drivers only, latest position only, no history, not shared for advertising).

## 6. After deploying
- **Production schools start hidden:** `show_on_website` is FALSE for every school. Switch a school on only after it has agreed, through Registered Schools, Edit school, "Website listing" (do not edit the column with SQL: the toggle also refreshes the API's 5-minute cache). The dev database has one school (Little Angels School) switched on for testing; the production database does not.
- `GET https://<api>/api/public/partner-schools` returns `{"status":true,...,"data":[]}` with CORS for your site origin. The home page shows the Partner Schools section only after a Super Admin switches a school on (Registered Schools, Edit school, "Website listing") and that school is Active.
- Submit a test enquiry; sign in as the Super Admin and open Enquiries.
- Open the driver app, accept the disclosure, start and end a duty with a throwaway driver (never a real driver).
- Confirm `Bootstrap__Enabled` is `false` and the committed secrets are NOT the ones in use.
