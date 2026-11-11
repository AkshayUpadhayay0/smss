# SMSS Mobile — Project Guide for Claude Code

React Native (Expo, TypeScript strict) client for the EXISTING SMSS API (`../smss-api`). Sibling of `../smss-web` (Angular).
Students and teachers only in v1. Read this fully before changing anything; the code is the source of truth if this goes stale.

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
`expo-secure-store` for tokens (never AsyncStorage), `@expo/vector-icons` (Feather), `@expo-google-fonts/inter`.
Expo APIs change every SDK: check `https://docs.expo.dev/versions/v58.0.0/` (or `llms.txt`) before using one from memory.
Install packages with `npx expo install <pkg>` (this repo needs `-- --legacy-peer-deps` because of a react-dom peer conflict).

## Layout
```
app/            Expo Router routes: (auth) sign-in + change-password, (student) tabs, (teacher) tabs, unsupported, index (the anchor redirect)
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
- Run: `npx expo start` (then `a` for Android emulator, or scan the QR with Expo Go)
- Typecheck: `npm run typecheck` · Lint: `npm run lint` · Tests: `npm test` · Health: `npx expo-doctor`
- Build (EAS): `eas build --profile preview --platform android` (profiles in `eas.json`; set `EXPO_PUBLIC_API_BASE_URL` in the EAS environment first).
  `com.smss.mobile` in `app.config.ts` is a placeholder identifier: confirm before the first store build.

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
  same token (the server grace period covers a lost reply); a 401/403 from refresh clears the session and returns to sign-in; a 5xx/network failure
  keeps the session. Never fire two refreshes in parallel.
- Sign-out: `POST /api/Auth/logout {refreshToken}` (best-effort, 5s cap), then clear secure storage and the query cache whatever happened.
- First login: `user.isFirstLogin` is true → the root layout allows ONLY `(auth)/change-password`. The server enforces this too
  (`FirstLoginEnforcementMiddleware`): every authenticated call except `change-password`, `me`, `refresh` and `logout` answers 403
  "Password change required…". `errors.ts` maps that exact message to `password_change_required` (not a generic error) and the session flips to
  the locked state. `POST /api/Auth/change-password` returns a FRESH token pair with `isFirstLogin=false`: store it, no re-login needed.
- Route protection is client-side only to decide what to SHOW (`Stack.Protected` guards in `app/_layout.tsx`, fail-closed). The server is the authority.

## Roles
- The API returns role **names** (`"Student"`, `"Teacher"`, `"School Admin"`, ...), not codes. `src/auth/roles.ts#resolveRole` is the ONLY place
  that compares role names (case-insensitive, trimmed). **No role-name string comparisons anywhere else.**
- Multi-role: Student wins, then Teacher, otherwise the "not supported in the mobile app yet" screen with a sign-out button. A School Admin who is
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
  Uploads: PDF/PNG/JPG/JPEG/WEBP, ≤ 5 MB (validate client-side too; the server re-checks the signature).
- Initials avatars only in v1: student/employee photos are private (`/api/Student/{id}/photo`), not loaded.

## Status
Phase 1 (skeleton, theme, API client, auth, role routing, unsupported screen, empty tab shells): built. Phases 2 and 3: not started.
The Home shells carry a TEMPORARY sign-out button; its permanent home is undecided.
