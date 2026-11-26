# SMSS API: configuration and secrets

The committed `smss-api/smss-api/appsettings.json` holds **no secrets**. Every secret is an empty placeholder there. The real values live

- in **development**: in ASP.NET **user-secrets** (stored in your Windows profile, outside the repository), and
- in **production**: in **environment variables** (or your host's secret store, which ends up as environment variables).

If a required setting is missing, empty, or still a placeholder (`CHANGE_ME…`, `<…>`), the API **refuses to start** and the error names the setting (never its value). Secrets are never logged.

> **Stage S (24 Nov 2026):** the file had been re-filled with real values on 15 Nov (commits `2908b0d`, `0b1bd12`) and pushed. `ConnectionStrings:DefaultConnection`, `Jwt:Key` and `Email:SenderAppPassword` are empty again, `.gitignore` covers more secret file patterns, and `TrackedConfigHasNoSecretsTests` fails the test run if a committed `appsettings*.json` ever holds a value under a secret-looking key. **Every value that was ever in history is exposed, including the ones now in your user-secrets: rotate the database password, the Gmail app password, the JWT key AND `Bootstrap:SuperAdminSetupKey`, then store the NEW values in user-secrets.** History is not rewritten (decision: option A, rotate only).

> **Upgrading from an older checkout:** earlier versions of `appsettings.json` carried a real database password and JWT key. Both are now empty in the file, so the API will not start until you provide them (user-secrets in Development, environment variables in Production, see below). Anything that was ever committed must be treated as exposed: rotate it (see *Rotating a secret*).

## Settings

| Setting | Secret? | Required | What it does |
|---|---|---|---|
| `ConnectionStrings:DefaultConnection` | **yes** | always | PostgreSQL connection string (host, port, database, user, password). |
| `Jwt:Key` | **yes** | always | Signs the access tokens. At least 32 characters. |
| `Jwt:Issuer`, `Jwt:Audience` | no | always | Token issuer / audience (committed values `smss-api` / `smss-web`). |
| `Jwt:ExpiryMinutes`, `Jwt:RefreshTokenDays`, `Jwt:RememberMeRefreshTokenDays` | no | no (defaults in the file) | Access token life, and refresh token life without / with "remember me". |
| `Email:SenderAppPassword` | **yes** | outside Development (in Development a missing one only means e-mails fail, which is logged and never blocks the main action) | SMTP (Gmail app) password used to e-mail login credentials. |
| `Email:SenderEmail`, `Email:SenderName`, `Email:SmtpHost`, `Email:SmtpPort`, `Email:LoginUrl` | no | `SenderEmail` outside Development | Who the e-mails come from, the SMTP server, and the login link put in the e-mails (set `LoginUrl` to the real site in production). |
| `Bootstrap:Enabled` | no | no (committed: `false`) | Turns the one-time Super Admin creation endpoint on. Keep it `false`; it already refuses once a Super Admin exists. |
| `Bootstrap:SuperAdminSetupKey` | **yes** | only while `Bootstrap:Enabled` is `true` | The `X-Setup-Key` header value that endpoint demands. |
| `Cors:AllowedOrigins` | no | outside Development | Array of web-app origins allowed to call the API, e.g. `https://app.example.com` (scheme + host [+ port], no path). Env form `Cors__AllowedOrigins__0`, `__1`, ... Development defaults to `http://localhost:4200`; elsewhere the API refuses to start without at least one. `*` is not accepted. |
| `FileStorage:RootPath` | no | no (`uploads`) | Folder for public files (school logos). Relative paths are under the API's content root. |
| `FileStorage:PrivateRootPath` | no | no (`uploads-private`) | Folder for private files: student photos, employee documents and photos, homework and admission documents, signatures. Never served by URL. |
| `Firebase:CredentialsPath` | **yes** (a path to a secret file) | no: without it the API still starts; the daily fee-reminder job logs one clear error per run and sends nothing | Location of the Firebase service-account JSON. Read ONLY from configuration (env var `Firebase__CredentialsPath`); only the FirebaseAdmin SDK opens the file. Never commit the file or its path; keep it outside the web root (`chmod 600` on the VPS). `.gitignore` blocks `*firebase*adminsdk*.json` and `firebase-service-account*.json`. |
| `Firebase:DryRun` | no | no (committed: `true` in every appsettings file; `true` in code when absent) | While `true`, every push is sent with FCM `dryRun: true`: FCM validates, nothing is delivered, and the log row says so. Set `false` only on purpose, in the deployed environment. |
| `AllowedHosts` | no | no | Standard ASP.NET host filter. |
| `ASPNETCORE_ENVIRONMENT` | no | set it | `Development` loads user-secrets; anything else (`Production`) does not and enforces the strict checks. |
| `ASPNETCORE_URLS` | no | optional | Listen address. |

Logos are deliberately **public** (`/uploads/<school code>/logo/…`): they are shown in the app header and carry no personal data. Anything that identifies a person (student and employee photos, documents) is private.

## Development (this machine)

The project already has a `UserSecretsId`. Run these from the repository root (use your own values):

```
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=smss-db;Username=postgres;Password=YOUR_DB_PASSWORD" --project smss-api/smss-api
dotnet user-secrets set "Jwt:Key" "A_LONG_RANDOM_STRING_OF_AT_LEAST_32_CHARACTERS" --project smss-api/smss-api
dotnet user-secrets set "Email:SenderAppPassword" "YOUR_GMAIL_APP_PASSWORD" --project smss-api/smss-api
dotnet user-secrets set "Bootstrap:SuperAdminSetupKey" "A_RANDOM_SETUP_KEY" --project smss-api/smss-api
```

Check what is set (this prints values, so do it privately): `dotnet user-secrets list --project smss-api/smss-api`. Remove one: `dotnet user-secrets remove "Jwt:Key" --project smss-api/smss-api`.

A random key: `[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))` in PowerShell.

Visual Studio and `dotnet run` pick the secrets up automatically when `ASPNETCORE_ENVIRONMENT` is `Development` (the committed launch profiles set it). The helper scripts under `scripts/` read the database password from `PGPASSWORD` if set, otherwise from the same user-secrets connection string.

## Production

Set environment variables, using `__` (double underscore) for `:`:

```
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=Host=...;Port=5432;Database=...;Username=...;Password=...
Jwt__Key=<long random string, at least 32 characters>
Email__SenderAppPassword=<smtp app password>
Email__SenderEmail=<sender address>
Email__LoginUrl=https://your-site/login
Cors__AllowedOrigins__0=https://your-site
```

Do **not** set `Bootstrap__Enabled=true` except for the moment you create the very first Super Admin, with `Bootstrap__SuperAdminSetupKey` set; switch it off again afterwards.

## Server checklist (Production)

**Environment variables the API needs** (`__` stands for `:`; values are never in git):

| Variable | Required | Secret |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` = `Production` | yes | no |
| `ConnectionStrings__DefaultConnection` | yes | **yes** |
| `Jwt__Key` (32+ characters) | yes | **yes** |
| `Jwt__Issuer` = `smss-api`, `Jwt__Audience` = `smss-web` | yes (committed defaults are fine) | no |
| `Email__SenderEmail`, `Email__SenderAppPassword` | yes outside Development | password: **yes** |
| `Email__LoginUrl` (and `Email__SenderName`, `Email__SmtpHost`, `Email__SmtpPort` if they differ) | yes | no |
| `Cors__AllowedOrigins__0` (`__1`, ...) | yes (the API refuses to start without one) | no |
| `ForwardedHeaders__KnownProxies__0` (or `KnownNetworks__0`) | yes behind a proxy / load balancer | no |
| `FileStorage__RootPath`, `FileStorage__PrivateRootPath` | recommended (persistent folders) | no |
| `Firebase__CredentialsPath` (path to a secret file), `Firebase__DryRun` (`true` until real pushes are wanted) | for push notifications | the file: **yes** |
| `Bootstrap__Enabled` = `false` (leave `Bootstrap__SuperAdminSetupKey` unset) | yes | the key: **yes** |
| `AllowedHosts` | recommended | no |
| `WhatsApp__*` and `ParentAccounts__*` (every key is in "WhatsApp and parent accounts" below) | only to turn real WhatsApp sending on; **not needed for the API to start**: the default is the log-only sender | AccessToken, AppSecret, WebhookVerifyToken: **yes** |

## WhatsApp and parent accounts (stage 6a, 26 Nov 2026)

Everything is **off or empty in the committed file**; with the defaults nothing is ever sent to Meta (the log-only sender writes a delivery-log row with status `LOGGED` and sends nothing) and the webhook answers 404. Secrets go in user-secrets (Development) or environment variables (server) only; `TrackedConfigHasNoSecretsTests` fails the build if a committed file holds a value under a secret-looking key. No secret is ever logged, put in the delivery log, or put in a message.

| Setting | Secret? | Default | What it does |
|---|---|---|---|
| `WhatsApp:Enabled` | no | `false` | Master switch. `false` = log-only sender, webhook off. |
| `WhatsApp:Sender` | no | `Log` | `Log` or `Cloud`. `Cloud` also needs `Enabled`, `AccessToken` and `PhoneNumberId`; if any is missing the log-only sender is used (the delivery log's sender kind then says `LOG`). |
| `WhatsApp:AccessToken` | **yes** | empty | Permanent System User token of the Meta app. Sent only in the Authorization header of the call to Meta. |
| `WhatsApp:PhoneNumberId` | no | empty | The platform's default sending number (Meta phone number id). Used until a school's own sender status is ACTIVE (set by the Super Admin on the school's WhatsApp sender screen). |
| `WhatsApp:BusinessAccountId` | no | empty | The WhatsApp Business Account id (recorded for operations; not needed to send). |
| `WhatsApp:ApiVersion` | no | `v21.0` | Graph API version, `vNN.N`. |
| `WhatsApp:BaseUrl` | no | `https://graph.facebook.com` | Meta's address (a setting so tests and a proxy can replace it). |
| `WhatsApp:AppSecret` | **yes** | empty | The Meta app secret that signs webhook calls (`X-Hub-Signature-256`). **Without it the webhook is disabled (404).** |
| `WhatsApp:WebhookVerifyToken` | **yes** | empty | The string Meta sends once when the webhook is registered; must equal the value typed in Meta's dashboard. |
| `WhatsApp:Language` | no | `en` | Template language code sent with every message. |
| `WhatsApp:TimeoutSeconds` | no | `15` | How long one call to Meta may take (1 to 60). |
| `WhatsApp:TemplateParentAccountReady`, `…ParentChildAdded`, `…ParentLoginHelp`, `…ParentStudentLoginReady` | no | `parent_account_ready`, `parent_child_added`, `parent_login_help`, `parent_student_login_ready` | The template names as approved by Meta. Change only if Meta made you use another name. Texts and variables: `Docs/whatsapp-templates.md`. |
| `ParentAccounts:SetPasswordLinkHours` | no | `72` | How long a set-password link works. Single use. |
| `ParentAccounts:ResetLinkMinutes` | no | `60` | How long a reset link works. Single use. |
| `ParentAccounts:ResendLimitPer24Hours` | no | `3` | Resends per guardian and purpose in 24 hours (enforced in stage 6b). |
| `ParentAccounts:WebBaseUrl` | no | empty | The website address the links point at (for example `https://smss-erp.digital`). Empty = the parent-account features refuse to build a link. |
| `ParentAccounts:Channels:<PURPOSE>` | no | not set (both channels) | Which channels carry which kind of message: `WHATSAPP`, `EMAIL` or `WHATSAPP,EMAIL`. Purposes: `SET_PASSWORD`, `RESET_PASSWORD`, `CHILD_ADDED`, `STUDENT_LOGIN`. Env form `ParentAccounts__Channels__RESET_PASSWORD=EMAIL`. Use it when Meta rejects or re-categorises one template: that purpose goes by email only until it is approved, with no code change. A switched-off channel is logged as `SKIPPED / CHANNEL_OFF`. |
| `RateLimiting:ParentAuth:PermitLimit`, `RateLimiting:ParentAuth:WindowMinutes` | no | `10` per `10` minutes | Per client address, on the public `api/parent/auth/*` endpoints (set-password, check-link, forgot). On top of it: a contact can ask for recovery 5 times an hour, and a guardian can be sent at most the first link plus `ResendLimitPer24Hours` (3) resends per kind in 24 hours. |

Environment-variable form: `WhatsApp__Enabled=true`, `WhatsApp__Sender=Cloud`, `WhatsApp__AccessToken=…`, `WhatsApp__PhoneNumberId=…`, `WhatsApp__AppSecret=…`, `WhatsApp__WebhookVerifyToken=…`, `ParentAccounts__WebBaseUrl=https://…`. Development: `dotnet user-secrets set "WhatsApp:AccessToken" "…" --project smss-api/smss-api` (the same for the other secrets).

**The webhook** is `GET/POST /api/webhooks/whatsapp` (anonymous because Meta calls it). It is guarded by the signature on every POST, answers Meta's raw contract (not the API envelope), reads at most 256 KB, logs only counts, and changes nothing but the status of the delivery-log row with the same provider message id (forward only: SENT, DELIVERED, READ; FAILED only after SENT).

**Tokens in URLs:** the one-time links carry their token in the query of a WEBSITE page (`/parent/set-password?t=…`); that page posts the token in a request BODY to the API and removes it from the address bar. The API itself never receives a token in a URL, and the committed log levels (`Microsoft.AspNetCore` at Warning) do not log request paths or queries. If a reverse proxy in front of the site logs full URLs, configure it to drop the query string for `/parent/*`.

The tuning settings (rate limits, `LoginThrottle__*`, `PartnerSchools__*`, `TransportLive__*`) have safe defaults in the committed file; see `Docs/DEPLOY-CHECKLIST.md` section 2.

**Deploy order:**
1. Set / rotate the environment variables on the server FIRST (at least the secret ones above). Do not deploy the blanked config before this: with `Production` and a blank secret the API refuses to start.
2. Deploy the new build with the blanked `appsettings.json`.
3. Restart the API. It must start, and `/api/...` must answer; if it stops with "missing settings", the message names the setting (never a value).
4. Rotating the JWT key ends open access tokens; users are silently re-issued from their refresh token (see *Rotating a secret*).
5. Confirm `Bootstrap__Enabled` is `false` and that the old values no longer work.

## Rotating a secret

- **Database password**: change it in PostgreSQL, then update `ConnectionStrings:DefaultConnection`, restart the API.
- **SMTP app password**: create a new app password, update `Email:SenderAppPassword`, restart.
- **Setup key**: update `Bootstrap:SuperAdminSetupKey` (only matters while Bootstrap is enabled).
- **JWT key**: update `Jwt:Key` and restart. Access tokens signed with the old key stop working immediately; the app then silently trades the user's refresh token (stored in the database, not signed with this key) for a new access token, so users normally notice nothing. Nothing in the database changes. Only a user whose refresh token has also expired has to log in again.

A secret that was ever committed to git must be treated as exposed even after it is removed from the file: rotate it (see `git log` for the history findings).

## Commit guard (so dumps and build output cannot be committed)

**Switch the pre-commit hook on** (once per clone):

```
git config core.hooksPath .githooks
```

The hook (`.githooks/pre-commit`) refuses a commit that stages build output (`bin/`, `obj/`), IDE state (`.vs/`), a database dump or backup (by name, or by content: a `PGDMP` archive or a "PostgreSQL database dump" header under any name), an uploaded file or a credentials file. It does not scan for secrets: keep them in user-secrets / environment variables.
