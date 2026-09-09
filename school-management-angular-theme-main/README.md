# Green Valley — School Management System (Angular Theme)

A complete, production-quality **School Management System UI template** built with **Angular 21**, standalone components, Signals, and the new `@if` / `@for` / `@switch` control-flow syntax. This is a **frontend theme**: every screen is wired to realistic mock data through injectable services shaped exactly like the HTTP services you'll swap them for, so connecting a real backend later is a drop-in change, not a rewrite.

---

## ✨ Features

- **40+ screens** across the full school-management workflow: dashboard, students, teachers, classes & sections, subjects, academic sessions, attendance, timetable, homework, examinations, marks entry, results, admissions, fees, library, transport, hostel, calendar, notices, notifications, messages, reports, settings, profile, a parent/student portal, a UI component showcase, auth screens, and error pages.
- **Theme customizer** — 7 color presets + a custom color picker, light/dark/system mode, compact density, and sidebar style (expanded / icon-only), all persisted to `localStorage` and driven by CSS custom properties (`--primary`, `--surface`, `--border`, etc.).
- **Full dark mode** across every screen, table, modal, chart, and form.
- **Responsive, mobile-first layout** with a collapsible sidebar, mobile drawer, and adaptive tables/cards down to 375px.
- **Reusable component library**: data tables, stat cards, status badges, pagination, tabs, modals, empty states, progress bars, and dependency-free SVG donut/bar/line charts.
- **Reactive Forms** with validation across every add/edit screen (students, admissions, notices, homework, etc.).
- **Realistic demo data**: 2,458 students, 126 teachers, 42 classes, full fee/library/transport/hostel/exam records — generated deterministically so the app never looks empty.
- **Lazy-loaded routes** for every feature module, keeping the initial bundle small (~79 KB gzipped).

---

## 🧱 Tech Stack

- Angular 21 (standalone components, Signals, new control flow)
- TypeScript (strict mode)
- SCSS with a CSS-custom-property design system
- Angular Router (lazy loading)
- Reactive Forms
- RxJS (mock services return `Observable`s, matching real HTTP services)
- Zero UI/icon/chart library dependencies — icons and charts are hand-built SVG, so there's nothing extra to license or update

---

## ✅ Requirements

- Node.js 20+ (tested on Node 22)
- npm 10+

---

## 🚀 Installation & Running Locally

```bash
unzip school-management-angular-theme.zip
cd school-management-angular-theme
npm install
ng serve
```

Open `http://localhost:4200`. The app auto-logs in a demo admin user (no backend required) and lands on the Dashboard.

### Production Build

```bash
npm run build
```

Output is written to `dist/school-management-angular-theme/browser`. Since this is a single-page app, **configure your host for SPA fallback** (rewrite all paths to `index.html`) — this is already handled for you by `ng serve`, Netlify, Vercel, and most static hosts with an SPA rewrite rule, but plain static file servers (like `http-server`) need it explicitly.

---

## 📁 Folder Structure

```
src/app/
├── core/               # models, mock data + services, theming, auth, guards
│   ├── services/        # data.service.ts (per-domain services), mock-data.ts, theme.service.ts, branding.service.ts, auth.service.ts
│   ├── models/           # school.models.ts — every domain interface
│   └── guards/
├── layout/              # admin-layout, auth-layout, sidebar, header, footer
├── shared/
│   └── components/       # icon, stat-card, avatar, status-badge, tabs, modal,
│                          # paginator, empty-state, progress-bar, charts/
├── auth/                # login, register, forgot/reset password, OTP
├── features/
│   ├── dashboard/
│   ├── students/         # list, form (add/edit), profile (tabs)
│   ├── teachers/
│   ├── classes/ subjects/ sessions/
│   ├── attendance/       # marking + reports
│   ├── timetable/
│   ├── homework/
│   ├── examinations/ results/
│   ├── admissions/
│   ├── fees/
│   ├── library/ transport/ hostel/
│   ├── calendar/
│   ├── notices/ notifications/
│   ├── reports/
│   ├── ui-showcase/
│   ├── settings/ profile/
│   ├── parent-portal/
│   └── errors/           # 404 / 403 / 500 / maintenance
├── app.routes.ts         # every route, all lazy-loaded
└── app.config.ts
```

---

## 🎨 Theme Customization

All theme state lives in `ThemeService` (`src/app/core/services/theme.service.ts`):

- **Color presets** are defined in `COLOR_PRESETS` — add your own by pushing a new `{ id, name, primary, primaryHover, primaryLight }` entry.
- **CSS variables** are set on `document.documentElement` at runtime (`--primary`, `--primary-hover`, `--primary-light`, `--primary-rgb`), so any component using `var(--primary)` in its styles updates instantly.
- **Dark mode** toggles the `.dark` class and `data-theme` attribute on `<html>`; every color token in `src/styles.scss` has a light and dark value.
- Settings are persisted to `localStorage` under `smt-theme-settings` and `smt-branding`.

To change the **school branding** (name, logo initials, tagline), edit the defaults in `src/app/core/services/branding.service.ts`, or update them live from **Settings → General**.

### Using Google Fonts (optional)

The production build ships with the system font stack for a fully offline-capable build. To use the Inter/Lexend web fonts, the `<link>` tags are already included in `src/index.html` — just make sure your build environment has internet access, or remove `"fonts": false` from the `production` configuration in `angular.json` to let Angular inline them automatically.

---

## 🔌 Connecting a Real Backend

Every screen talks to injectable services in `src/app/core/services/data.service.ts` (e.g. `StudentService`, `FeeService`, `AttendanceService`). Each method currently does:

```ts
getAll(): Observable<Student[]> { return respond(M.STUDENTS); }
```

To connect a real API, inject `HttpClient` and replace the body:

```ts
getAll(): Observable<Student[]> { return this.http.get<Student[]>('/api/students'); }
```

No component code needs to change — every component consumes these services through the same `Observable`-returning interface.

---

## 🧩 Component Usage Examples

```html
<!-- Stat card -->
<app-stat-card label="Total Students" value="2,458" icon="users" change="+8.2%" trend="up" />

<!-- Status badge (auto-colors based on the status string) -->
<app-status-badge status="Overdue" />

<!-- Data table pagination -->
<app-paginator [total]="248" [pageSize]="10" [page]="page()" (pageChange)="page.set($event)" />

<!-- Charts -->
<app-donut-chart [data]="[{label:'Present', value:180, color:'var(--success)'}]" />
<app-bar-chart [series]="[{name:'Score', color:'var(--primary)', values:[72,88,91]}]" [labels]="['A','B','C']" />
```

Browse the full, live catalogue at **`/ui`** inside the running app.

---

## 📊 Mock Data

All demo data is generated deterministically in `src/app/core/services/mock-data.ts` using a seeded PRNG, so the numbers are stable across reloads but not hand-written — meaning you get thousands of realistic records (students, fees, attendance, library issues, etc.) without a database. Regenerate or resize any dataset by editing the generator functions at the top of that file.

---

## 📄 License

This is a UI theme template intended as a starting point for your own school management product. Customize, extend, and connect it to your own backend freely.
