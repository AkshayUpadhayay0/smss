# smss-web

A production-quality, reusable **Angular SaaS admin dashboard template** — multi-organization,
theme-able, and built to grow into a full application (originally scoped for a School
Management System, but generic enough for any SaaS admin product).

Light/white by default, fully responsive, backed by mock/localStorage data so it runs with
**no backend required**.

---

## 1. Project setup

```bash
npm install
ng serve
```

Then open **http://localhost:4200**.

> **A note on the Angular version.** This template targets modern, current Angular practices
> (standalone components, signals, the new control-flow syntax, functional guards, lazy-loaded
> routes). It's built on **Angular 20**, the newest version that installs and runs cleanly in
> this environment's Node.js version at the time of writing. If your local Node.js version
> supports a newer Angular major release, you can upgrade with the official Angular Update
> Guide (`ng update`) — the architecture here doesn't rely on anything version-specific.

### Build

```bash
ng build                # production build -> dist/smss-web
ng build --configuration development
```

### Run unit test scaffolding

```bash
ng test
```

---

## 2. Demo login

The app seeds a demo organization and admin account into `localStorage` the first time it runs
— no signup needed to explore it.

| Field | Value |
|---|---|
| Organization | Demo School (theme: **Blue**) |
| Email | `admin@example.com` |
| Password | `demo1234` |

The login page pre-fills these for convenience — just click **Sign In**. You can also register
a brand-new organization from scratch via the **Create one** link on the login page, or the
**Registration** item in the sidebar.

To reset all demo data (organizations, users, sessions, sidebar state), clear your browser's
localStorage for `localhost:4200` and reload.

---

## 3. Project structure

```
src/
├── app/
│   ├── core/            # Everything not visual: services, models, auth, mock data
│   │   ├── auth/            # AuthService + route guards (login/register/logout/session)
│   │   ├── guards/          # Barrel re-export of the auth guards
│   │   ├── interceptors/    # HTTP interceptors (auth token, error handling) — ready for a real API
│   │   ├── models/          # TypeScript interfaces (Organization, User, Theme, Table types…)
│   │   ├── services/        # ThemeService, ToastService, ConfirmDialogService, LoaderService…
│   │   ├── mock/             # Seed/demo data — organizations, users, dashboard, table rows
│   │   └── utils/           # localStorage helpers, id generation, icon registry, nav config
│   │
│   ├── shared/
│   │   ├── components/      # The reusable UI kit — see section 6 below
│   │   └── directives/      # e.g. appTooltip
│   │
│   ├── layouts/
│   │   ├── auth-layout/     # Wraps Login/Register (no sidebar/header)
│   │   └── main-layout/     # Sidebar + Header + routed page content
│   │       ├── header/
│   │       └── sidebar/
│   │
│   ├── features/            # One folder per page/route
│   │   ├── dashboard/
│   │   ├── table/
│   │   ├── forms/
│   │   ├── ui-elements/
│   │   ├── modals/
│   │   ├── swagger/
│   │   ├── auth/{login,register}/
│   │   └── examples/{example-a,b,c,d}/   # nested-route breadcrumb demo
│   │
│   ├── app.ts / app.html / app.scss   # Root component — mounts the router outlet +
│   │                                     global toast/confirm-dialog/loader overlays
│   ├── app.config.ts        # Providers: router, HttpClient, icon registry
│   └── app.routes.ts        # All routes, lazy-loaded, with guards + breadcrumb data
│
├── environments/            # apiUrl / appName per environment (see section 8)
└── styles/                  # Global SCSS — see section 5 below
```

### Adding a new page

1. Create a folder under `src/app/features/<your-page>/` with
   `<your-page>.component.ts`, `.html`, and `.scss` (never combine markup/styles into the
   `.ts` file for anything non-trivial).
2. Register the route in `src/app/app.routes.ts` inside the `MainLayoutComponent` children
   array, with a `loadComponent()` lazy import and a `data: { breadcrumb: '...' }` entry —
   the breadcrumb trail builds itself from that automatically.
3. Add an entry to `NAV_ITEMS` in `src/app/core/utils/nav.util.ts` so it shows up in the
   sidebar (top-level or nested under an existing group).
4. Reach for the shared component library (section 6) before writing new markup — buttons,
   cards, form fields, tables, modals, badges, etc. are all already there and already
   theme-aware.

---

## 4. Theme system

This is the part of the template most worth understanding before you build on top of it.

### How it works

- Six themes ship out of the box: **Default (white/light)**, **Green**, **Blue**, **Orange**,
  **Yellow**, **Purple** — see `src/styles/themes/_*.scss`.
- Every theme is just a block of **CSS custom properties** (`--primary-color`,
  `--sidebar-active-bg`, `--success-color`, `--border-color`, etc.) scoped under a
  `body.theme-<name>` class selector.
- Components **never** hard-code colors. They only ever reference `var(--something)`. That
  means switching the class on `<body>` re-skins the *entire* application instantly — no
  page reload, no per-component logic.
- `ThemeService` (`src/app/core/services/theme.service.ts`) is the single place that knows how
  to toggle that class. `AuthService` calls `themeService.applyTheme(...)` whenever a session
  starts (login, register) or ends (logout → reverts to Default).

### How organization theme selection works

1. On **Registration**, the admin picks a theme via `<app-theme-selector>` (visual swatch
   previews, backed by the same theme catalog).
2. `AuthService.register()` stores that theme on the new `Organization` record in
   `localStorage` (see `OrganizationService`).
3. On **Login**, the organization tied to the account is loaded, and its `theme` field is
   passed straight to `ThemeService.applyTheme()`.
4. The theme is therefore always restored automatically on every future login/reload — it
   travels with the organization record, not with browser/session state.

### Adding a new theme

1. Duplicate `src/styles/themes/_default.scss` as `_your-theme.scss` and adjust the CSS
   custom property values (keep the same variable names).
2. Add the `@use` line for it in `src/styles.scss`.
3. Add an entry to the `THEMES` array in `src/app/core/models/theme.model.ts` (name, label,
   `className: 'theme-your-theme'`, and swatch colors for the picker preview).

That's it — the registration page, the theme selector component, and `ThemeService` all read
from that single catalog, so nothing else needs to change.

---

## 5. Global SCSS architecture

```
src/styles/
├── _variables.scss     # Spacing, radius, shadows, breakpoints, typography scale, z-index
├── _mixins.scss        # respond-up/down, card-surface, focus-ring, truncate, scrollbars…
├── _reset.scss         # Base element resets
├── _utilities.scss     # .btn, .badge, .form-control, .alert, .dropdown-item, animations…
└── themes/
    ├── _default.scss
    ├── _green.scss
    ├── _blue.scss
    ├── _orange.scss
    ├── _yellow.scss
    └── _purple.scss
```

`angular.json` sets `stylePreprocessorOptions.includePaths: ["src/styles"]`, so any component
SCSS file can do `@use 'variables' as v;` / `@use 'mixins' as m;` without a relative path.

---

## 6. Reusable shared components

All under `src/app/shared/components/`, each with separate `.ts` / `.html` / `.scss` files:

`icon` · `button` · `card` · `modal` · `confirm-dialog` · `input` · `select` · `textarea` ·
`checkbox` · `radio` · `switch` · `table` · `pagination` · `breadcrumb` · `page-header` ·
`loader` (full-page) · `spinner` · `skeleton` · `progress-bar` · `circular-progress` · `toast` ·
`alert` · `badge` · `avatar` · `empty-state` · `tabs` · `accordion` · `dropdown` ·
`theme-selector`, plus an `appTooltip` directive.

A few are worth knowing about specifically:

- **`app-table`** is a generic, typed data-table shell (`columns` + `rows` inputs). Per-row
  actions are projected in via `<ng-template #rowActions let-row>`, so the table component
  itself never needs to know what a given page wants to do with a row.
- **`app-input` / `app-select` / `app-textarea` / `app-checkbox` / `app-radio-group` /
  `app-switch`** all implement Angular's `ControlValueAccessor`, so they drop straight into
  Reactive Forms with `formControlName` exactly like a native control.
- **Toasts, confirmation dialogs, and the full-page loader** are all mounted *once*, globally,
  in `app.html`. Trigger them from anywhere via `ToastService`, `ConfirmDialogService`
  (`await confirmDialogService.confirm({...})`), or `LoaderService` — no page needs to render
  its own copy.

---

## 7. Authentication

```
core/auth/
  auth.service.ts   # login / register / logout / session signals
  auth.guard.ts      # authGuard (protects the app) + guestGuard (protects login/register)
```

- Fully mock/local — accounts and organizations live in `localStorage`
  (`OrganizationService`, and the account list inside `AuthService`).
- `authGuard` redirects unauthenticated users to `/login`; `guestGuard` redirects already
  logged-in users away from `/login` and `/register`.
- `AuthService` exposes Angular **signals** (`currentUser`, `currentOrganization`,
  `isAuthenticated`) so components react automatically without manual subscriptions.

---

## 8. Environment configuration & backend readiness

```
src/environments/
  environment.ts               # production defaults (apiUrl, appName)
  environment.development.ts   # local dev overrides
```

`angular.json`'s `development` build/serve configuration swaps in the dev file automatically —
components always just `import { environment } from '../environments/environment'` and never
hard-code a URL.

The whole app is intentionally layered so swapping mock data for a real API later is a
service-level change, not a component-level one:

```
Component → Service (e.g. OrganizationService) → [ currently: localStorage ] → later: HttpClient
```

- `core/interceptors/auth.interceptor.ts` already attaches the session token as a Bearer
  header to any outgoing `HttpClient` request.
- `core/interceptors/error.interceptor.ts` is a ready-made place to centralize API error
  handling (e.g. routing errors into `ToastService`).
- `provideHttpClient(...)` is already wired up in `app.config.ts`, so adding real HTTP calls
  is a matter of injecting `HttpClient` into a service and replacing its localStorage reads/
  writes — components and templates don't need to change at all.

---

## 9. Routing

```
/login
/register

/dashboard
/table
/form
/ui-elements
/modals
/swagger

/example/a
/example/a/b
/example/a/b/c
/example/a/b/c/d
```

All routes are **lazy-loaded** (`loadComponent`). `/login` and `/register` sit under
`AuthLayoutComponent`; everything else sits under `MainLayoutComponent` (sidebar + header +
breadcrumb) and is protected by `authGuard`.

---

## 10. What's mocked vs. real

| Area | Status |
|---|---|
| UI, layout, responsiveness, theming | Fully real |
| Forms, validation, modals, tables (search/sort/paginate) | Fully real, client-side |
| Auth, organizations, users | Real logic, **mock/localStorage persistence** |
| Dashboard stats, activity feed, table rows | Static mock data (`core/mock/`) |
| Swagger / API docs page | Placeholder UI — wire up a real OpenAPI/Swagger UI once a backend exists |

Everything above is deliberately structured so replacing the "mock/localStorage" pieces with
real HTTP calls only touches files inside `core/services` and `core/auth` — never the
components or templates that consume them.
