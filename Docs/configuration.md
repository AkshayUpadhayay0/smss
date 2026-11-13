# SMSS API: configuration and secrets

The committed `smss-api/smss-api/appsettings.json` holds **no secrets**. Every secret is an empty placeholder there. The real values live

- in **development**: in ASP.NET **user-secrets** (stored in your Windows profile, outside the repository), and
- in **production**: in **environment variables** (or your host's secret store, which ends up as environment variables).

If a required setting is missing, empty, or still a placeholder (`CHANGE_ME…`, `<…>`), the API **refuses to start** and the error names the setting (never its value). Secrets are never logged.

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

## Rotating a secret

- **Database password**: change it in PostgreSQL, then update `ConnectionStrings:DefaultConnection`, restart the API.
- **SMTP app password**: create a new app password, update `Email:SenderAppPassword`, restart.
- **Setup key**: update `Bootstrap:SuperAdminSetupKey` (only matters while Bootstrap is enabled).
- **JWT key**: update `Jwt:Key` and restart. Access tokens signed with the old key stop working immediately; the app then silently trades the user's refresh token (stored in the database, not signed with this key) for a new access token, so users normally notice nothing. Nothing in the database changes. Only a user whose refresh token has also expired has to log in again.

A secret that was ever committed to git must be treated as exposed even after it is removed from the file: rotate it (see `git log` for the history findings).

## Secret scan (so a secret cannot be committed again)

The scanner is [gitleaks](https://github.com/gitleaks/gitleaks) (MIT licence). `.gitleaks.toml` extends its built-in rules with SMSS rules (a connection string with a password, a non-empty `Key` / `SuperAdminSetupKey` / `SenderAppPassword` in a JSON file, and any PostgreSQL dump).

**Install** (once): download `gitleaks_<version>_windows_x64.zip` from the releases page, check it against `gitleaks_<version>_checksums.txt` (`sha256sum`), unzip it somewhere outside the repository and put that folder on `PATH` (or set `GITLEAKS_BIN` to the exe).

**Switch the pre-commit hook on** (once per clone):

```
git config core.hooksPath .githooks
```

The hook (`.githooks/pre-commit`) refuses a commit that stages build output (`bin/`, `obj/`), IDE state (`.vs/`), a database dump or backup (by name, or by content: a `PGDMP` archive or a "PostgreSQL database dump" header under any name), an uploaded file or a credentials file, or any change gitleaks flags. It refuses to run without gitleaks installed, so the check cannot be skipped by accident.

The same scan runs on GitHub for every push and pull request once you enable it: `.github/workflows/secret-scan.yml` (it only reads the repository; nothing needs to be configured beyond leaving Actions on).

**Scan the working tree** (one line, from the repository root; prints nothing secret; it also reports ignored build folders such as `bin/` that exist on your disk, which is expected and harmless):

```
gitleaks dir . --config .gitleaks.toml --redact
```

Other scans: `gitleaks git --staged --config .gitleaks.toml --redact` (what is about to be committed) and `gitleaks git . --config .gitleaks.toml --redact` (all history; it will keep reporting the old, already-leaked values until the history is rewritten).

A false positive can be silenced for one finding with its fingerprint in a `.gitleaksignore` file; never silence a real secret, rotate it instead.
