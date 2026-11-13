# SMSS Mobile — Project Guide for Claude Code

React Native (Expo, TypeScript strict) client for the EXISTING SMSS API (`../smss-api`). Sibling of `../smss-web` (Angular).
Students, teachers and (Android first) drivers. Read this fully before changing anything; the code is the source of truth if this goes stale.

## Rules that override defaults
- **No new backend without asking.** This app consumes `smss-api`; it does not change it. If a screen has no suitable endpoint, list the gap and
  propose a minimal endpoint, then WAIT for approval. Exception: a real server-side authorization gap found while wiring a screen is closed
  immediately in `smss-api` as its own separate commit, and reported.
- Online-only in v1: no offline sync. Do not add libraries unless they solve a real requirement (no Redux, no NativeWind, no axios).
- Build in phases and stop after each for review (Phase 1 skeleton + auth, Phase 2 student screens, Phase 3 teacher screens).
- No real-database smoke tests as a real user. A test that needs a login uses a throwaway account that is deleted afterwards; never touch a
  real account's password hash. The tunnel is for development with throwaway accounts only.
- Never claim something works unless it was run.

## Stack
Expo SDK 58, Expo Router (file-based, `app/`), TanStack Query (server state), a small React Context for the session (`src/auth/session.tsx`),
`expo-secure-store` for tokens (never AsyncStorage), `expo-location` + `expo-task-manager` + `expo-dev-client` (driver duty; **Expo Go no longer runs this app**, see "Driver duty"), `@expo/vector-icons` (Feather), `@expo-google-fonts/inter`.
Expo APIs change every SDK: check `https://docs.expo.dev/versions/v58.0.0/` (or `llms.txt`) before using one from memory.
Install packages with `npx expo install <pkg> -- --legacy-peer-deps` (a react-dom 19.3.1 vs react 19.3.0 peer conflict makes plain installs fail).
TODO: retry WITHOUT `--legacy-peer-deps` after the next Expo SDK update, and drop the flag if it works.

## Layout
```
app/            Expo Router routes: (auth) sign-in + change-password, (student) tabs, (teacher) tabs, (driver) home + setup screens, unsupported, index (the anchor redirect)
src/api/        client.ts (fetch + envelope + refresh lock), errors.ts, types.ts, auth.ts, one typed endpoint module per feature
src/auth/       session.tsx (context), roles.ts (resolveRole), storage.ts (secure store)
src/config/     env.ts (API base URL, validated)
src/features/   one folder per feature (attendance, homework, marks, timetable, notices, leave, ...); shell/ = Phase 1 placeholders
src/components/ Text, Icon, Button, Card, Chip, Input, ListRow, Screen, EmptyState, ErrorState, Skeleton, TabBar (tab options)
src/theme/      tokens.ts (colours, radii, fonts)
__tests__/      jest-expo unit tests
```

## Environment / API URL
The base URL is `EXPO_PUBLIC_API_BASE_URL`, read by `app.config.ts` into `extra.apiBaseUrl` and validated by `src/config/env.ts`
(required; `https://` only, `http://` allowed in dev builds). Local: copy `.env.example` to `.env` (untracked; **never commit the URL**).
EAS builds: set the variable in the EAS environment. Android release builds block cleartext HTTP, so production must be HTTPS.

### Dev tunnel (a phone/emulator cannot reach `https://localhost:7037`)
1. Run the API: `dotnet run --project smss-api/smss-api --launch-profile https` (listens on `https://localhost:7037`).
2. Start a tunnel. The local dev certificate is self-signed, so cloudflared must not verify the ORIGIN:
   `cloudflared tunnel --url https://localhost:7037 --no-tls-verify`
   (ngrok alternative: `ngrok http https://localhost:7037`). Put the printed `https://....trycloudflare.com` URL in `.env`.
3. Restart Metro with a clean cache after changing `.env`: `npx expo start -c`.
The tunnel is for development only, and only with throwaway accounts. Quick tunnels get a new URL each run.

## Commands
- Run: **a development build is required** (Expo Go cannot load the native location modules): `npx expo run:android` once (builds + installs the dev client), then `npx expo start --dev-client` (see "Driver duty" for the full flow).
- Typecheck: `npm run typecheck` · Lint: `npm run lint` · Tests: `npm test` · Health: `npx expo-doctor`
- Build (EAS): `eas build --profile preview --platform android` (profiles in `eas.json`; set `EXPO_PUBLIC_API_BASE_URL` in the EAS environment first).
  **`com.smss.mobile` (bundle id / Android package, in `app.config.ts`) is a development PLACEHOLDER and is PERMANENT after the first store build.
  The owner must decide and confirm it BEFORE any store or production build. Do not build for stores until they do.**
  (`eas.json` is strict JSON and cannot hold comments, so the warning lives here and in `app.config.ts`.)

## API contract (verified against smss-api, Phase 0)
- Every endpoint returns `{ status, statusCode, message, data }`; `src/api/client.ts` unwraps `data`. Dates are `yyyy-MM-dd`, times `HH:mm:ss`.
- The school/tenant comes from the token. **The app never sends a school id and never lets the user pick one.**
- Deletes are soft (the API toggles status). Status values (Paid/Pending/...) are derived server-side: never recompute them.
- Use the API's own 4xx messages in the UI; any 5xx or unknown failure becomes a friendly generic message. Never show stack traces, SQL or raw JSON.
- Use the SERVER's date for every "today" decision (dashboard and attendance responses), never the phone's clock.
- `PATCH` is blocked in the dev environment (see the root CLAUDE.md): the API uses POST for actions; this app only needs GET/POST/PUT/DELETE.

## Auth
- `POST /api/Auth/login {loginId, password, rememberMe}` → `{token, refreshToken, user{roles[], isFirstLogin, ...}}`. **`rememberMe: true` on every
  mobile login** (14-day refresh token). Access token lives 15 minutes.
- Refresh tokens **rotate** (each works once; reusing an old one revokes every session). `client.ts` therefore: single-flight lock (concurrent 401s
  trigger ONE refresh); the new pair is written to SecureStore BEFORE the lock is released; a refresh with no response is retried ONCE with the
  same token (recovers a request that never arrived; if the server rotated and only the reply was lost, the replay gets 401 and the user signs in
  again: verified live, the 10 s grace period only prevents a replay from revoking ALL sessions, it does not re-issue tokens); a 401/403 from refresh clears the session and returns to sign-in; a 5xx/network failure
  keeps the session. Never fire two refreshes in parallel.
- Sign-out: `POST /api/Auth/logout {refreshToken}` (best-effort, 5s cap), then clear secure storage and the query cache whatever happened.
- First login: `user.isFirstLogin` is true → the root layout allows ONLY `(auth)/change-password`. The server enforces this too
  (`FirstLoginEnforcementMiddleware`): every authenticated call except `change-password`, `me`, `refresh` and `logout` answers 403
  "Password change required…". `errors.ts` maps that exact message to `password_change_required` (not a generic error) and the session flips to
  the locked state. `POST /api/Auth/change-password` returns a FRESH token pair with `isFirstLogin=false`: store it, no re-login needed.
- **Sign-out navigates to `app/signing-out.tsx` first**, then ends the session (150 ms later). Removing a whole signed-in route group under the user made expo-router loop ("Maximum update depth exceeded"); found only on a real device with live data. Do not put nested `Stack.Protected` guards inside `(auth)`: reachability is decided by the root layout guards + `app/index.tsx`.
- Route protection is client-side only to decide what to SHOW (`Stack.Protected` guards in `app/_layout.tsx`, fail-closed). The server is the authority.

## Roles
- The API returns role **names** (`"Student"`, `"Teacher"`, `"School Admin"`, ...), not codes. `src/auth/roles.ts#resolveRole` is the ONLY place
  that compares role names (case-insensitive, trimmed). **No role-name string comparisons anywhere else.**
- Multi-role: Student wins, then Teacher, then Driver (`DRIVER`, the app in "Driver duty" below), otherwise the "not supported in the mobile app yet" screen with a sign-out button. A School Admin who is
  also a Teacher lands in the Teacher app (acceptable: the server still enforces what they can do). TODO: role switcher (not built).
- **Known fragility:** if a role is ever renamed in `lut_roles`, the app breaks. The proper long-term fix is a `roleCodes` field in the auth
  response (`AuthUserResponse` in smss-api). Deliberately NOT changed now.
- SUPER_ADMIN, SCHOOL_ADMIN, PRINCIPAL, ACCOUNTANT (and Guardian, Admission Staff) stay on the web.

## Design system (matches smss-web)
Tokens in `src/theme/tokens.ts`: primary `#6D28D9`, deep `#4C1D95`, text `#5B21B6`, tint `#EDE9FE`/border `#C4B5FD`, background `#FAF9FF`,
ink `#1E1B2E`. Inter 400/500/600/700. Radii: cards 18, inputs/buttons 14, chips 10–12. Touch targets ≥ 44. Body text ≥ 4.5:1 contrast.
Status is never colour alone (Present/Absent keep a text label and icon). Bottom tabs: 5, tint pill behind the active icon, 11px/600 labels.
Every list screen has: skeleton, empty state, error state with Retry, pull-to-refresh. Every screen respects safe areas (`Screen` component).
Render real API data only; a screen that has fewer fields than a design shows fewer fields. Do not invent data.

## Security
- Never log tokens, passwords, or response bodies. No secrets in the repo (`.env` is gitignored).
- Attachments (Phase 2/3): authenticated download to the cache directory with `expo-file-system`, then open with `expo-sharing`. No plain URLs.
  Uploads: PDF/PNG/JPG/JPEG/WEBP, ≤ 5 MB (validated client-side too; the server re-checks the signature).
  **Multipart uploads go through `XMLHttpRequest` (`sendMultipart` in `src/api/client.ts`) with React Native's `{ uri, name, type }` FormData part.**
  Do NOT switch to fetch: Expo's fetch throws "Unsupported FormDataPart implementation" for that part, and `expo-file-system`'s `File` cannot read the
  document picker's copy inside Expo Go ("Missing READ permission"). Found on the emulator; unit tests cannot catch it. Homework is two calls
  (create, then attachment); a failed attachment keeps the homework and offers retry / continue without.
- Initials avatars only in v1: student/employee photos are private (`/api/Student/{id}/photo`), not loaded.

## Testing
- **Unit tests:** `npm test` (jest-expo). They cover the API client (refresh lock, rotation, retry), role resolution, error mapping, date helpers and the teacher rules.
  They cannot catch native-module behaviour (multipart upload, router guards, pickers): anything touching those must be run on the emulator.
- **Live checks use a dev database, never `smss-db`.** `smss-db-dev` on the local PostgreSQL is a schema-only copy of `smss-db` plus the `lut_*` lookup data
  (`pg_dump --schema-only` and `--data-only -t 'public.lut_*'`). It holds NO real data and is kept empty of `tb_*` rows between sessions. Do not drop it unless asked.
- **Run the API against it with environment overrides, no config edits** (never put the dev connection string or a key in `appsettings*.json`):
  publish a copy (`dotnet publish smss-api/smss-api -c Release -o <scratch>/api`), then from that folder with
  `ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=smss-db-dev;...`, a random `Jwt__Key`, `ASPNETCORE_ENVIRONMENT=Development`,
  `ASPNETCORE_URLS=http://localhost:5290`, and `FileStorage__RootPath` / `FileStorage__PrivateRootPath` pointing at scratch folders (so uploads never land in the repo).
  Run `dotnet smss-api.dll`. A School's `school_short_code` is required for generated login ids; an "Existing Student" admission type is required before onboarding students.
- **Tunnel (only while emulator checks run):** `cloudflared tunnel --url http://localhost:5290 --no-autoupdate` (plain http origin, so no `--no-tls-verify`), put the printed
  `https://*.trycloudflare.com` URL in `.env` as `EXPO_PUBLIC_API_BASE_URL`, `npx expo start --clear`. Stop the tunnel and delete `.env` as soon as the checks finish.
  The quick-tunnel hostname can take a minute to resolve; one transient failure on first load is normal (Retry recovers it).
- **Throwaway data rule:** every account/row created for a check is a `ZZMOB` throwaway in the dev database's demo school, with random alphanumeric passwords kept only in a scratch file
  (server-generated temporary passwords contain `@#$%*!`, which `adb input text` mangles: set a known temp password with a bcrypt hash via SQL instead). Never use a real account.
  Scripts live outside the repo and are deleted afterwards. Take a row-count fingerprint of all tables before and after; clean up by exact ids or by truncating `tb_*` in the DEV database only.
- **Attachment files are part of cleanup:** homework attachments are files on disk (`<FileStorage:PrivateRootPath>/<school code>/homework/<id>/...`), not just rows. Delete them with the rows.
- **Auth-screen lesson:** `app/(auth)/sign-in.tsx` and `change-password.tsx` each `<Redirect href="/" />` as soon as their state no longer applies (signed in / password changed / signed out); `app/index.tsx` then routes. Without that, a first-login sign-in looped (found by the live first-login check, after the sign-out fix removed the nested guards).
- **Sign-out lesson:** signing out from a screen showing live data once crashed with "Maximum update depth exceeded". Sign-out therefore goes through the neutral
  `app/signing-out.tsx` screen first (see Auth). Always test sign-out from a data screen, not only from the shells/auth screens.
- Day rollover: a long session can cross midnight; re-read the server's "today" (`/api/StudentDashboard`) instead of reusing one saved earlier.


## Driver duty (live location) — Stage B
The DRIVER role (a `tb_users` login with role "Driver", `org_user_id` = `tb_drivers.driver_id`) gets its own app: `app/(driver)` + `src/features/driver/`.
Home shows the vehicle, areas and the duty state; **Mark today's attendance** starts a duty session and location sharing; **End duty** stops it.
Server side: `GET api/Drivers/me`, `POST api/DriverDuty/start` (driver login), `POST api/DriverDuty/location` and `/end` (tracking token), see `smss-api`.

### Rules
- **Expo Go no longer works for this app.** Background location, the foreground service and the dev client are native: use a development build (below).
- **The prominent disclosure comes BEFORE any permission prompt**, with the approved wording in `src/features/driver/copy.ts` (`DISCLOSURE_TEXT`; changing it needs the
  product owner's approval, and bumping `DISCLOSURE_VERSION` shows it to everyone again). Order: disclosure, foreground location, background ("Allow all the time"; on
  Android 11+ the system only offers its Settings page), notifications (Android 13+, `PermissionsAndroid`). Denied: ask again. Denied forever: open Settings.
  Battery screen only OPENS the system battery list; **never request `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`** (Google Play restricts it).
- **The tracking token** (returned once by `/start`, valid only for `/location` and `/end` of that session) lives only in `expo-secure-store` (`smss.duty.token`).
  The background task reads it **on every ping and never caches it** (a resume at duty start issues a NEW token and invalidates the old one), sends it only as
  the `X-Duty-Token` header with plain `fetch` (`dutyHttp.ts`, never `src/api/client.ts`), and **never uses the JWT or the refresh token** (two JS contexts must never refresh at once).
  It is deleted at duty end and on every STOP. It is never logged, put in a URL, a query key, an error message or a screenshot. **Never log coordinates or request bodies either.**
- The background task is defined at module scope and imported from the app entry `index.ts` (`package.json` `main`): Android may start the app with no UI just to run it.
- 60 s `timeInterval` (the server's `intervalSeconds`, never below its `minIntervalSeconds`), balanced accuracy, `distanceInterval` 0, foreground service with a visible notification.
  **Dev builds only:** `EXPO_PUBLIC_DUTY_INTERVAL_SECONDS` (1..600) shortens the interval for quick emulator passes; it is ignored in release builds.
- The first fix is sent at once after `/start` succeeds. **No offline queue**: a missed ping is simply replaced by the next one (latest-only on the server).
- The UI never reads the phone's clock for duty decisions: "On duty since", status (LIVE/STALE/OFFLINE) and "last sent" come from `GET api/Drivers/me` (server time and ages).
- Signing out while on duty warns "This will end your duty" and calls `/end` (login first, tracking token as a fallback) before the session ends. End duty always stops sharing
  locally, even if the server cannot be reached (the school then sees the vehicle go OFFLINE; the open session is closed the next time duty starts).

### Driver duty: what each answer makes the app do (`classifyPing` in `src/features/driver/ping.ts`; unit-tested)
| Answer to `POST /api/DriverDuty/location` | Action | What the app does |
|---|---|---|
| 200 `accepted: true` | OK | nothing; the next ping follows the interval |
| 200 `accepted: false` (`LOW_ACCURACY`, `STALE_FIX`, `FUTURE_FIX`, `OUT_OF_ORDER`, `MOCK_LOCATION`) | **DROP** | ignore this fix; send the next one on schedule. Duty continues. |
| 400 (an impossible value) | **DROP** | same; the next fix is a new reading |
| 401 (unknown / replaced token) | **STOP** | stop the service, delete the token, show "duty ended" (`UNKNOWN_TOKEN`) |
| 409 `SESSION_ENDED`, `SESSION_EXPIRED`, `DRIVER_DEACTIVATED` (any other 409 too) | **STOP** | stop the service, delete the token, show "duty ended" with the reason |
| no network / timeout | **RETRY** | do nothing; the next ping sends a fresh fix (nothing is queued) |
| 429 `TOO_SOON` | **RETRY** | do nothing; the next ping |
| 5xx, and anything unexpected (a gateway page, 403, 404) | **RETRY** | do nothing; never end duty over a transient failure |
| no token on the phone | **STOP** | stop the service (`NO_TOKEN`) |

A STOP is remembered (`smss.duty.stopped`) so the Home screen can say why. On app resume, if the server says a duty is open but `Location.hasStartedLocationUpdatesAsync`
is false, Home shows "Location sharing stopped. Resume?"; Resume calls `/start` again (the server resumes the same session with a NEW token).

### Running and building (Android first)
Everything below needs the Android SDK + JDK 17 (`ANDROID_HOME` set) for local builds.
- **Local development build (primary):** put the API URL in `.env` (see Environment), then `npx expo run:android` (prebuilds `android/`, which is git-ignored, builds the debug APK,
  installs it on the running emulator / USB phone and starts Metro). Afterwards start only Metro with `npx expo start --dev-client`. A debug build allows cleartext HTTP, so an
  emulator can use `http://10.0.2.2:<port>` (the host's localhost); a physical phone over USB can use `adb reverse tcp:<port> tcp:<port>` and `http://localhost:<port>`.
  Release builds block cleartext: they need an `https://` URL.
- **EAS development build (to share an APK):** set `EXPO_PUBLIC_API_BASE_URL` in the EAS `development` environment (never in git), then
  `eas build --profile development --platform android` (apk). Install it on the phone and run `npx expo start --dev-client` on the PC, or use the `preview` profile for a standalone APK.
- The app identifier `com.smss.mobile` is a placeholder that is only acceptable for development builds (see Commands).
- Play Store (later): background location needs a Permissions Declaration with a demo video, the foreground-service type needs its own declaration, the in-app disclosure above, and a
  privacy policy that mentions location. New builds with these permissions go through Google review.

### Emulator testing recipe
Scratch API on a spare port against `smss-db-dev` (see Testing), `.env` with `EXPO_PUBLIC_API_BASE_URL=http://10.0.2.2:<port>`. Move the emulator with
`adb emu geo fix <lon> <lat>` (note: LONGITUDE first) or Extended controls > Location > Routes (GPX playback). Emulator fixes carry the mocked flag: the API's `RejectMockLocations` is OFF in
Development and ON in production. Airplane mode: `adb shell cmd connectivity airplane-mode enable|disable`. Never use a real driver's login: use the throwaway ZZ drivers.

### Findings from the Android emulator run (Android 15, Pixel 9 image) — keep for the next person
- **Windows: the first `npx expo run:android` fails in `buildCMakeDebug` ("ninja: error: Stat(...)")**: the generated codegen object paths exceed 260 characters and the default CMake 3.22.1 ships ninja 1.10 (no long-path support; `LongPathsEnabled` is already 1 on this PC). Fix: use the SDK's CMake **4.1.2** (ninja 1.12) by adding `externalNativeBuild { cmake { version = "4.1.2" } }` inside `android { }` in `android/app/build.gradle` (that folder is generated and git-ignored: redo it after `expo prebuild --clean`). Moving the project to a short path did not help (`subst` breaks the autolinking command).
- **The emulator has no network location**: with the production `Balanced` accuracy it produces NO fixes. For emulator checks put `EXPO_PUBLIC_DUTY_ACCURACY=high` in `.env` (dev builds only; a real phone uses Balanced). Move it with `adb emu geo fix <lon> <lat>`.
- With the screen locked / app in the background / app swiped away from Recents, the foreground service keeps running and sends (process and service survive on the emulator). **Force stop** (Settings > App info > Force stop, or `adb shell am force-stop`) kills the service for good: the school sees STALE then OFFLINE; opening the app again restarts the persisted task by itself (the Home screen shows it). If the task registration is lost (we deleted `TaskManagerModule.xml` to simulate it) Home shows "Location sharing stopped. Resume?". Real phones with aggressive battery managers may behave differently: see `docs/driver-real-phone-test.md`.
- Airplane mode: pings are RETRY (nothing queued); the vehicle goes STALE on the web map and returns to LIVE by itself when the network is back (same session).
- Deactivating the driver on the web ends the session at the next ping (409 `DRIVER_DEACTIVATED`): the service stops, the notification disappears, Home says "Duty ended" with the reason. Deactivation also revokes the driver's refresh tokens, so the LOGIN ends at its next refresh: that path used to crash with "Maximum update depth exceeded" (the signed-in route group unmounting under the user, the same bug the sign-out screen works around). `session.tsx` now leaves through `/signing-out` whenever the session ends by itself, and runs registered end hooks (`onSessionEnded`): the driver app uses that to stop sharing and end the duty with the tracking token, so a signed-out phone never keeps tracking.
- A react-query `refetchInterval` did not trigger the 401 on an idle Home screen in the dev client within several minutes (a pull-to-refresh did). Not investigated further; a real phone run should confirm the Home screen notices a revoked login without a gesture.
- The dev client shows a floating gear button over the top-right corner and a "Development Build" launcher after Force stop (tap the recent server). Gboard may show a stylus tutorial the first time you type with `adb input text`: tap Cancel.

## Status
- Phase 1 (skeleton, theme, API client, auth, role routing, unsupported screen): built; live-verified against the API (see git history).
- Phase 2 (student screens 3-8 + the shared profile sheet): built. Student API calls live in `src/api/student.ts`, hooks in `src/features/student/queries.ts`.
  "Today" always comes from the dashboard's server date (`useServerToday`), never the phone clock. Notices are marked read by `GET /api/Notice/{id}` only.
  Attachments: `src/lib/attachments.ts` (authenticated download to the cache folder, then the share sheet; the folder is purged on sign-out).
- Phase 3 (teacher screens 9-13): built. API in `src/api/teacher.ts`, hooks in `src/features/teacher/queries.ts`, pure rules in `rules.ts` / `derive.ts` (unit-tested).
  Section / subject pickers and "my periods today" come from the teacher's own `/Timetable/my`; the server re-checks what a teacher may do. A tile the server
  lists in `partsUnavailable` is hidden, never shown as 0. Forms that are seeded from the server (attendance roster, marks roster) do not refetch silently
  under unsaved input (explicit pull-to-refresh only). Student leave is fetched WITHOUT a status filter and split into Pending / Decided client-side.
- Driver app (Stage B, Android first): built and run on the Android emulator; see "Driver duty" above and `docs/driver-real-phone-test.md`.
- The profile sheet (`src/components/ProfileSheet.tsx`, opened from the avatar in `HomeHeader`) is where Sign out lives. A Change password entry is deliberately not there yet.
