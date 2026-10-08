# SMSS — Project Guide for Claude Code

This is a multi-tenant School Management SaaS frontend (Angular), paired
with an existing, working .NET API (smss_api, separate repo/folder — not
rewritten, only consumed). Read this fully before making changes. This
file is the persistent memory for this project across sessions — keep it
updated as the app evolves; don't let it go stale.

## Stack & conventions
- Angular, standalone components only (no NgModules)
- Signals for component state (not plain class fields + manual change
  detection) — `signal()`, `computed()`, `toSignal()` for observables
- Reactive Forms for all forms
- SCSS, no inline styles
- `ChangeDetectionStrategy.OnPush` on every component
- Routes use `loadComponent` (lazy), and feature route files are mounted
  at the ROOT path in app.routes.ts — e.g. a schools feature's routes
  are `/schools`, `/schools/add`, NOT `/organization/schools/add`

## API contract — every backend endpoint returns this shape
    { "status": bool, "statusCode": int, "message": string, "data": T|null }
Model this as a generic `ApiResponse<T>` interface, used everywhere.

## Backend base URL
Configured via `environment.apiUrl`. Default dev: `https://localhost:7037`.

## Design system (match this exactly — see reference screenshot)
- Primary brand color: violet/purple (~#7C5CFC — refine against the
  actual logo gradient), used for: active sidebar item (solid pill with
  left accent bar), primary buttons, logo mark background
- Layout: fixed left sidebar (white/very light background) + top bar
  (white, border-bottom) + content area (light gray `#F8F9FB`-ish
  background) with white rounded cards floating on it
- Sidebar: logo + "SMSS" wordmark top-left, nav items with icons
  (lucide-style line icons), collapsible groups (e.g. "Masters" expands
  to Board Type / School Type / School Level / Status / Role), active
  item highlighted solid violet with white text
- Top bar: hamburger/menu toggle, current org switcher (avatar initial +
  name + code, e.g. "SMSS Admin / SUAD01"), search icon, notification
  bell (with unread dot), user menu (avatar + name + role, e.g.
  "SMSS Admin / Super Admin") with dropdown chevron
- Page header pattern used on every list/form page: small icon in a
  rounded square, bold page title, gray subtitle line, breadcrumb trail
  below (Home / Current Page), action buttons top-right (secondary
  outline + primary solid)
- Data tables: white card container, toolbar row with search input
  (icon-prefixed) + record count on the right, sortable column headers
  (up/down arrow icons), status shown as a pill badge (green bg/text for
  Active, matching pattern for Inactive), row actions as icon buttons
  (view/edit/delete) right-aligned, pagination footer (Showing X–Y of Z,
  items-per-page select, Previous/page-numbers/Next)
- Buttons: primary = solid violet rounded, secondary = white with gray
  border, danger = red, all with small icon + label
- Border radius: generous (8–12px) throughout — cards, buttons, inputs,
  badges all rounded, not sharp
- Typography: clean sans-serif, bold headings, muted gray for secondary
  text/subtitles/breadcrumbs

## Shared component library to build (shared/components/)
Build these as the foundation BEFORE any feature screens:
- `page-header` — icon, title, subtitle, breadcrumbs input, content
  projection for action buttons
- `card` — optional title, optional noPadding input, content projection
- `button` — variant: primary | secondary | danger | danger-ghost, size,
  disabled, type (button/submit), icon support via content projection
- `input` — label, type, required, placeholder, prefixIcon, help text,
  errorText, readonly/disabled support, ControlValueAccessor
- `select` — label, options: {label, value: string}[], required,
  multiple, errorText, ControlValueAccessor — IMPORTANT: value must be
  STRING typed (numeric ids get `.toString()`'d when building options)
- `table` — generic `TableColumn<T>[]` (key, label, sortable, width),
  rows input, loading state, sortKey/sortDirection, hasActions flag with
  an `#rowActions` template ref for custom action buttons per row,
  empty-state message
- `pagination` — currentPage, totalItems, pageSize, page/pageSize change
  events, "Showing X–Y of Z" text
- `icon` — wraps an icon set (lucide or similar), name + size inputs
- Toast/notification service (success/error/info/danger methods) and a
  confirm-dialog service (async confirm() returning boolean) — used
  throughout for feedback and destructive-action confirmation

## Layout structure
- `AuthLayoutComponent` — centered card, no sidebar, used for /login
- `MainLayoutComponent` — sidebar + topbar + router-outlet, used for
  everything else, guarded by an auth guard once real JWT auth exists
- `app.routes.ts` splits into an auth-layout branch (login) and a
  main-layout branch (everything else), each feature's routes file
  lazy-loaded via `loadChildren`

## Core services (core/services/)
- `ToastService`, `ConfirmDialogService` (used everywhere)
- `MasterDataService` — caches lookups (countries/states/districts/
  cities via cascading parent-keyed cache, plus board-types/
  school-types/school-levels/status/role via shareReplay(1)) and
  exposes both raw getters and `SelectOption[]`-mapping convenience
  methods for direct use in `<app-select>`

## Known backend state (DO NOT re-invent — this already works)
- School registration: POST /api/SchoolRegistration/register creates
  school + contacts + login user + role in one transaction. School code
  is immutable after creation. GET /schools, GET /schools/{id},
  PUT /schools/{id}. "Delete" = POST /schools/{id}/toggle-status
  (flips Active/Inactive, never a real delete). Status is NEVER sent by
  the client on create/update — server defaults new schools to Active.
- Logo: POST /schools/{id}/logo (multipart, field name "file"),
  POST /schools/{id}/logo/remove. Response `logoUrl` is a relative path
  like `/uploads/{schoolCode}/logo/....png` — prepend `environment.apiUrl`
  to render it.
- Master data: GET/POST/PUT + POST .../toggle-status under
  /api/MasterData/{board-types|school-types|school-levels|status|role},
  plus read-only cascading location endpoints (countries, states/
  {countryId}, districts/{countryId}/{stateId},
  cities/{countryId}/{stateId}/{districtId}).
- Auth: POST /api/Auth/login, /refresh, /logout, GET /me,
  POST /change-password. JWT bearer tokens. Login UI not yet built in
  the old project — build it fresh here.
- A Super Admin account already exists in the DB (bootstrapped via a
  one-time setup endpoint) — login screen should work against it once
  built: org_user_id SUAD01, standard username/password login.

## House rules
- No hardcoded status/role ids in any logic — those come from the API
- Don't build features not yet requested (no academic/attendance/exam
  modules yet — registration + master data + auth is the current scope)
- Ask before large architectural decisions; don't guess silently on
  anything that would be expensive to undo
## Current state (update as the app evolves)
- Done: design tokens (`src/styles/_tokens.scss`, CSS custom properties), shared component
  library (`shared/components`, import from the barrel `shared/components/index.ts`),
  Toast/ConfirmDialog services (hosts mounted once in `app.html`), AuthLayout + MainLayout
  (`layouts/`), nav in `layouts/main-layout/nav.config.ts`, routes with placeholder pages
  (`features/placeholder`), `environments/` (dev build swaps via fileReplacements),
  `ApiResponse<T>`, `ApiService` (prefixes apiUrl, unwraps envelope), `errorInterceptor`
  (toasts failures; opt out per request with `SKIP_ERROR_TOAST` HttpContext).
- Icons: curated Lucide subset in `shared/components/icon/icons.ts` (no npm dependency) — add
  new icons there.
- Nav URLs: `/dashboard`, `/schools`, `/masters/{board-type|school-type|school-level|status|role}`.
- Placeholders to replace: topbar org/user/notifications (hard-coded in `topbar.component.ts`),
  `/login` (`login-placeholder`), every `PlaceholderPageComponent` route, missing `/change-password`.
- Throwaway: `/dev/components-preview` (`src/app/dev/`) — delete once the look & feel is approved.
- School Registration feature built (`features/schools/`): list, add/edit/view (one `SchoolFormComponent`,
  `mode` + `schoolId` bound from route data/params), logo upload, status toggle, one-time credentials panel.
  Pure form logic lives in `utils/school-form.ts` (build form, map to/from API) and `utils/school-validators.ts`.
- `MasterDataService` rebuilt (`core/services/`) on `ApiService`; failed lookups are not cached.
- API facts learned: SchoolRegistration controller is `[Authorize]` (JWT needed — login not built yet);
  update never deletes contacts (so saved contacts can't be removed in the UI); update OVERWRITES
  `subscriptionStatusId`, so it is part of the form even though the original spec omitted it; there is no
  subscription-plan lookup endpoint (plan ID is a plain number input).
- All School and Master Data API calls were verified live with curl (not clicked through in a browser yet).
  Test records left in the DB (inactive): school ZZ-TEST-001 (+ its login user), ZZTEST board type / school type /
  school level / role, and two "ZZ Test Status" rows.
- Dev server gotcha: a long-running `ng serve` can go stale and serve components WITHOUT their styles (inputs look
  unstyled). Restart it before blaming the CSS.
- Master Data (`features/masters/`): ONE generic `MasterListComponent` + `MasterFormModalComponent`, driven by a
  `MasterConfig` (see `models/master.model.ts`); each master is a config in `configs/` passed as route data
  (`data: { config }`, bound to the `config` input). All five are built (Board Type, School Type, School Level,
  Status, Role). Optional config hooks: `derive` (extra display fields), `deactivateWarning` (stronger confirm text),
  `formNotice`, `filter` (dropdown above the table, used by Status for its type) and per-field `minLength`. CRUD goes through `MasterCrudService`, which invalidates `MasterDataService` caches.
- Master API facts: every GET hides inactive rows unless `?includeInactive=true` (lists must pass it); update DTOs
  omit the code field (immutable); Status HAS POST/PUT/toggle and an `isActive` flag (not read-only); Role returns
  `isActive` + `isProtected` and the server only protects SUPER_ADMIN and SCHOOL_ADMIN; role deactivation with
  assigned users returns 409 with a message. Status names "Active"/"Inactive" are looked up BY NAME server-side —
  renaming them would break the app.
- Every `.subscribe()` on an HTTP call needs an `error` handler (interceptor already toasts) or RxJS rethrows it
  as an uncaught error.
- Auth built (`core/services/auth.service.ts`, `core/interceptors/auth.interceptor.ts`, `core/guards/auth.guard.ts`,
  `features/auth/`): login page, change-password page, bearer interceptor with single-flight refresh + retry,
  `authGuard` on the main layout, `guestGuard` on /login, real user/org in the top bar, logout. Login ID is the org
  user id (e.g. SUAD001); the response `user.username` is the person's DISPLAY NAME. Refresh tokens are single-use and
  rotated — replaying an old one revokes ALL of that user's sessions, so never refresh concurrently (AuthService.refresh
  is single-flight). Interceptor order matters: [errorInterceptor, authInterceptor] so a recovered 401 never toasts.
  Session lives in sessionStorage, or localStorage with "Keep me signed in". A first-login user (isFirstLogin) is sent to
  /change-password after login. Test sessions with `seedSession()` from `core/testing/auth-test-utils.ts`.
