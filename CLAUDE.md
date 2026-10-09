# SMSS — Project Guide for Claude Code

Multi-tenant School Management SaaS. This folder contains two projects
side by side — read this file fully before touching either:

- `smss-api/` — ASP.NET Core Web API, C#, EF Core, PostgreSQL
- `smss-web/` — Angular frontend
- `Docs/` — reference material, including the full role & workflow
  baseline (ask to see it if a module's scope is unclear)

Both projects are real and working for the features listed below — do
not rewrite working code without a stated reason. Read the actual
current files before changing anything; this doc can go stale, the
code is the source of truth.

## Core development philosophy
Production readiness, scalability, maintainability, security,
multi-tenancy, data integrity, clean architecture, and simplicity over
cleverness. Don't introduce microservices, CQRS, MediatR, Redis,
message brokers, or other heavy infrastructure unless there's an actual
requirement for it — this is intentionally a modular monolith.

Before a large feature: understand it, identify affected
tables/modules/dependencies, flag problems, then implement. Don't
silently change an established decision — state "Previous decision /
New decision / Reason / Impact" if one needs to change.

---

## Backend (smss-api)

### Stack & layering
Controller → Service → Repository → DbContext. Projects:
- `smss_api` — controllers only, thin, no business logic
- `smss_api_service_layer` — dto/ service/ interface/ helper/
- `smss_api_db_layer` — entity/ repository/ interface/ context/

### Database conventions
- PostgreSQL only. `BIGINT GENERATED ALWAYS AS IDENTITY` for DB-generated
  numeric ids. `TIMESTAMPTZ` for timestamps. No `gen_random_uuid()`.
- Entities use explicit `[Table("...", Schema = "public")]` and
  `[Column("...")]` attributes (no EF naming-convention package).
- `tb_` prefix = tenant/business tables, carry `school_id` where
  relevant. `lut_` prefix = global lookup/master tables, no tenant.
- **Never hardcode status or role ids.** Always resolve by name from
  `lut_status` (e.g. "Active" + stype "general status") or `lut_roles`
  (e.g. "School Admin") — sids/role_ids are not stable across
  environments.
- "Delete" generally means toggling an `isactive`/`status_id` flag, not
  removing the row — see School's toggle-status pattern below.

### API response contract — every endpoint returns this shape
    { "status": bool, "statusCode": int, "message": string, "data": T|null }

### Known quirk
`PATCH` requests get silently blocked somewhere in this dev environment
(not CORS, not the API itself — root cause never fully isolated). All
non-idempotent single-purpose actions (toggle-status, logo upload,
logo remove) use `POST` instead, by convention, on this project.

### What's built and working
- **School registration** (`SchoolRegistrationController`): tb_schools,
  tb_school_contacts, tb_users, tb_user_roles. `POST /register` creates
  school + contacts + login user + role assignment in one transaction.
  `school_code` immutable after creation. `school_status_id` is NEVER
  client-settable — server defaults new schools to Active (resolved by
  name). `POST /schools/{id}/toggle-status` flips Active/Inactive and
  cascades the flip to the school's login user's status too.
  `GET /schools`, `GET /schools/{id}`, `PUT /schools/{id}`.
- **Logo upload**: `POST /schools/{id}/logo` (multipart, field "file"),
  `POST /schools/{id}/logo/remove`. Stored at
  `uploads/{schoolCode}/logo/logo_<timestamp>.<ext>`, served at
  `/uploads/{schoolCode}/logo/...`. Max 2MB, PNG/JPG/WEBP, signature
  checked (not just extension). `logoUrl` is server-controlled, never
  accepted from register/update payloads.
- **Master data** (`MasterDataController`): countries/states/districts/
  cities are READ-ONLY (LGD hierarchy, composite keys cid/sid/did/
  cityId). board-types, school-types, school-levels have full CRUD +
  toggle-status (isActive boolean). status (lut_status) — confirm
  current read/write state in Swagger before assuming. role (lut_roles)
  — has two special rules: protect system/seed roles from deactivation,
  block deactivating a role that has active users assigned.
- **Auth** (`AuthController`): login, refresh, logout, me,
  change-password. JWT bearer, config in appsettings `Jwt` section.
  `[Authorize(Roles = "...")]` attributes are CURRENTLY COMMENTED OUT
  across controllers for testing — re-enabling them is a pending task,
  do NOT re-enable blindly without confirming the exact role string
  format the token actually carries (check a real decoded token).
- **Super Admin bootstrap** (`SystemSetupController`): one-time
  `POST /bootstrap-super-admin`, guarded by `X-Setup-Key` header +
  config `Bootstrap:Enabled` flag + a check blocking it once any Super
  Admin exists. A Super Admin already exists (org_user_id SUAD01) —
  this endpoint should now be effectively retired; don't re-trigger it
  casually, and double-check `Bootstrap:Enabled` is `false` in a shared
  environment.
- **Email**: MailKit/Gmail SMTP, wired into school registration
  (credentials emailed to primary contact or school email) and Super
  Admin bootstrap (welcome email, no password). Failures are logged,
  never block the main response — this pattern (don't let a
  non-critical side effect fail the primary operation) should be
  followed for any future "also send an email/notification" feature.
- A test school exists: `sch2026006` (First Test School, code
  12345678910) with a working School Admin login.

### What's NOT started yet
- Re-enabling `[Authorize(Roles = ...)]` (blocked on confirming token
  role claim format — this is the first thing to nail down)
- RBAC via permissions rather than hardcoded roles (long-term intent,
  see Docs/ for the full baseline — not urgent yet)
- Everything for Core School Masters (see Angular section) — these are
  new tables, not yet designed or built on the API side at all
- Student/Employee/Attendance/Exam/Fee modules — out of scope for now

---

## Frontend (smss-web)

### Stack & conventions
- Angular, standalone components only (no NgModules)
- Signals for state (`signal()`, `computed()`, `toSignal()`), not
  plain fields + manual change detection
- Reactive Forms, SCSS, `ChangeDetectionStrategy.OnPush` everywhere
- Feature route files mounted at ROOT path (e.g. `/schools`, not
  `/organization/schools`)
- `environment.apiUrl` for the backend base URL (dev: `https://localhost:7037`)

### Design system
Violet/purple primary (~#7C5CFC), white sidebar + topbar, light-gray
content background with white rounded cards, generous border radius
(8–12px), icon-prefixed buttons, status shown as colored pill badges.
See `shared/components/` for the built library: page-header, card,
button, input, select, table, pagination, icon, plus ToastService and
ConfirmDialogService. `MasterDataService` in `core/services/` caches
all lookup data and exposes `SelectOption[]`-mapping convenience
methods for direct use in `<app-select>`.

### What's built and working
- Auth layout + main layout (sidebar/topbar/router-outlet), login flow
  functional end to end
- School Registration: list (search/sort/paginate/status badge/
  view/edit/toggle-status), add/edit/view form (cascading location
  dropdowns, dynamic contacts FormArray, logo upload with preview,
  one-time credentials panel after registration)
- Master Data admin screens for board-type, school-type, school-level
  (generic config-driven list+form-dialog pattern) — status and role
  masters' frontend state should be confirmed against the backend
  before assuming they're done

### KNOWN BUG — fix before building more school-side screens
A logged-in School Admin can currently see and open Super Admin-only
menu items (Registered Schools, Masters) because nothing filters the
sidebar or guards routes by role. This is the next task. Needs: (1)
confirming where the role actually lives in the login/me response or
JWT claims (check both backend DTOs and a real decoded token — don't
guess), (2) a single ROUTE_ROLES config read by both the sidebar menu
and a role-aware route guard (never duplicate the role list in two
places), (3) fail-closed behavior for any route not explicitly listed.

### Planned: School Admin UI flow (not built yet)
After a School Admin logs in: (1) School Dashboard — stats like total
students/employees/fee collection this month/expenses/profit this
session (defer real numbers until the underlying modules exist — don't
fake data); (2) School Profile page — school completes remaining
details; (3) User Profile page — shows the school's primary contact
(also treated as an employee).

### Planned: Core School Masters (not built on either side yet)
When a school first registers, before anything else they set these up.
Tentative global-vs-school-owned classification (CONFIRM with the user
before building any of these — getting this wrong means a painful
migration later):

| Master | Likely scope |
|---|---|
| Academic Year | school-owned (`tb_`, carries school_id) |
| Class | school-owned |
| Section | school-owned |
| Subject | school-owned |
| Employee Designation | school-owned |
| Employee Department | school-owned |
| Admission Type | school-owned |
| Religion/Caste Category | global (`lut_`), school opts in/out |
| Blood Group | global |
| Gender | global |
| Document Type | school-owned (`tb_document_types`) — reclassified from global; the earlier lut_document_type version was removed |
| Student Category | global (`lut_student_category`) — reclassified from school-owned; the earlier tb_student_categories version was removed |
| Class-Subject Mapping | school-owned |
| Class-Section Mapping | school-owned |
| Teacher-Subject Mapping | school-owned, BLOCKED on Employee/Teacher module not existing yet |

Build order once RBAC is fixed: School Profile + User Profile → Academic
Year → Class → Section → Class-Section Mapping → Subject →
Class-Subject Mapping → Employee Department/Designation → Student
Category → Admission Type. Global masters (Gender, Blood Group,
Religion/Caste) can reuse the existing Super-Admin
generic master-list pattern.

---

## House rules (apply to both projects)
- Don't build features not yet requested — current scope is
  registration + master data + auth, nothing from Students/Attendance/
  Exams/Fees yet
- Ask before any decision that's expensive to undo (schema choices,
  tenant-isolation boundaries, auth model changes) — don't guess
  silently on these
- DTOs never expose entities directly; blank optional strings become
  null server-side before validation (empty "" fails
  `[EmailAddress]`/`[Url]`)
- When changing API responses or DTO shapes, check smss-web for every
  place that shape is consumed — keep both sides in sync in the same
  change, don't let them drift