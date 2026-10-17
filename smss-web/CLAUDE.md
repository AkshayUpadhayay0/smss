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
- School form rules: the school code is the school's UDISE+ code — exactly 11 digits, validated ONLY when adding
  (existing schools have legacy codes like SUAD01; on edit/view the control is disabled so it never blocks saving).
  A primary contact is mandatory: the add form starts with one pre-marked primary row, the last contact can't be
  removed, and at least one contact + exactly one primary is required on edit too. Every contact needs email AND mobile
  (alternate mobile/designation optional) — legacy saved contacts missing them must be completed before an update saves. The server itself only enforces
  `^[A-Za-z0-9_-]+$` for the code and 'exactly one primary IF contacts exist', so these two rules live in the UI only.
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
- ROLE ACCESS (UI): ONE config, `core/config/route-roles.ts` (`ROUTE_ROLES`), read by BOTH `roleGuard`
  (`core/guards/role.guard.ts`, a `canActivateChild` on the main layout so it re-runs on every page change) and the
  sidebar (`SidebarComponent.items`). Fails CLOSED: a route with no entry is denied. A new page needs an entry there —
  a test (`route-roles.spec.ts`) fails if a page has no entry or an entry has no page. `/dashboard` and
  `/change-password` are `'authenticated'` (denied users are redirected to /dashboard, so it must stay open).
  `AuthService.currentUserRoles` (signal) comes from the session restored synchronously from storage → no menu flash.
  This is UX only; the API is the real security boundary.
- ROLES: the API returns role NAMES ("Super Admin", "School Admin", …) in `user.roles` and the JWT role claim — NOT role
  codes — and names are editable in Master Data → Role. Renaming a role there changes access. The API's
  `[Authorize(Roles=…)]` must use the same strings (or the API should switch the claim to RoleCode).
- API SECURITY GAP (found 2026-10-09): the API has NO server-side role checks. A School Admin token can GET all schools,
  and POST/PUT master data (verified: POST /MasterData/board-types returned 201). Needs `[Authorize(Roles=…)]` re-enabled.
- School Setup (school-owned masters, `features/school-setup/`, sidebar group "School Setup", routes `/school-setup/*`,
  School Admin only in ROUTE_ROLES): Academic Year built (list + add/edit modal; status and "current" are row ACTIONS —
  toggle-status, Set as Current — never form fields). API `/api/AcademicYear` is always scoped to the token's school_id
  (shared helper `User.GetSchoolId()` in smss_api/Helpers, also used by School Profile `/me`); another school's id returns 404.
  Rules: first year auto-current; current year can't be deactivated; inactive year can't be set current; duplicate name 409.
  Super Admin's own account also has a school record, so it can reach these endpoints for ITS OWN (empty) school only.
- Class master built (`/school-setup/classes`, API `/api/Classes`, same tenant pattern as Academic Year): list defaults to
  Sequence Order ascending (ties by name); sequence_order is a smallint (1–32767, not unique); duplicate name per school 409;
  toggle-status has no refusal rules yet (Sections will reference class_id later).
- Section master built (`/school-setup/sections`, API `/api/Sections`): belongs to a Class. Unique (class_id, section_name), so the
  same name works under different classes. The DB does NOT tie class_id to school_id, so the service verifies the class belongs
  to the caller's school (400 otherwise). Responses carry joined className + classSequenceOrder; the list's Class column sorts in
  class display order. The form's class dropdown lists active classes only (plus the section's own class when editing).
- Four simple school-owned masters built (Subject, Employee Designation, Employee Department, Admission Type; routes
  `/school-setup/{subjects|employee-designations|employee-departments|admission-types}`, API
  `/api/{Subjects|EmployeeDesignations|EmployeeDepartments|AdmissionTypes}`; Student Category was originally a fifth but was
  reclassified as GLOBAL and removed from here — see below). Backend: four independent
  entity/repo/service/controller sets in the Class style (generated from one template, no shared base, so per-master rules can
  be added freely). Frontend: ONE config-driven pair in `features/school-setup/` (`SchoolLookupListComponent` +
  `SchoolLookupFormModalComponent`, configs in `configs/school-lookups.config.ts`) — deliberately separate from the Super Admin
  `MasterConfig` pattern. Only Subject has a code field. Add a new simple school-owned master = a config + a route + a nav/ROUTE_ROLES entry.
- Class-Subject Mapping built (`/school-setup/class-subjects`, API `/api/ClassSubjects`): a checklist UI (class dropdown + subject
  checkboxes + Save), NOT a list/form. Save calls `POST /ClassSubjects/bulk {classId, subjectIds}` which replaces the class's
  whole set in one transaction (hard add/delete; status_id is only set to Active on insert, never toggled). There is no separate
  Class-Section mapping table: tb_sections.class_id is that relationship. The service verifies class AND subjects belong to the
  caller's school (DB doesn't). Mapped-but-inactive subjects stay visible in the checklist so a save never silently drops them.
- Global lookups added under Super Admin Masters: Religion/Caste Category, Blood Group, Gender (`/masters/{religion-category|
  blood-group|gender}`, API `/api/MasterData/{religion-categories|blood-groups|genders}`; Document Type was later moved to school-owned), written in the exact
  Board Type style (code immutable after create, duplicate code OR name -> 409, isActive toggle) inside MasterDataController/Service/
  Repository, and four more `MasterConfig`s for the same generic list. Columns use `is_active` (Board Type's is `isactive`); description is
  varchar(250); blood group names are max 10 chars. Gender (3) and Blood Group (8) come pre-seeded. Super Admin only in ROUTE_ROLES.
- Student Category is a GLOBAL lookup (Super Admin Masters, `/masters/student-category`, API `/api/MasterData/student-categories`, table
  `lut_student_category`, same style/config pattern as Gender & co.). The earlier school-owned version (tb_student_categories,
  /school-setup/student-categories, /api/StudentCategories) was deleted entirely.
- Document Types is SCHOOL-owned (`tb_document_types`; `/school-setup/document-types`, API `/api/DocumentType/{schoolId}[/update|/{id}/toggle-status]`).
  Fields: name (50), code (20, stored upper-case, unique per school), appliesTo STUDENT|EMPLOYEE|BOTH, isRequired; status via toggle. The routes carry
  {schoolId} (frontend takes it from the session via AuthService), but the controller REFUSES (403) any id that is not the JWT school_id and always
  uses the token value. TODO: drop {schoolId} from the routes. The global lut_document_type version was removed (the table itself still exists, unused).
  The shared table got a `type: 'badge'` column kind (pill, highlighted when the value is "Yes").
- Admissions (status-driven workflow, `features/admissions/`, routes `/admissions`, `/admissions/new`, `/admissions/:admissionId`, School Admin only;
  API `/api/Admission/{schoolId}[/submit|/{id}|/{id}/start-review|approve|reject|cancel|confirm]`). Statuses (lut_status stype ADMISSION_STATUS):
  APPLICATION_SUBMITTED -> UNDER_REVIEW -> APPROVED -> ADMITTED, with REJECTED (from review, reason required) and CANCELLED (from submitted/review).
  The valid actions per status live in ONE place on the client (`ACTIONS_BY_STATUS`) and mirror the server's state machine. `AdmissionActionsComponent`
  renders the buttons + dialogs for both the list rows and the detail page. Confirm Admission creates student + enrollment + guardian + link in ONE
  transaction; `classSectionId` is tb_sections.section_id (the dialog offers active sections of the applied class). Like Document Type, the routes
  carry {schoolId} but the controller refuses (403) anything but the JWT school_id; reviewer/approver ids come from the JWT (NameIdentifier), never the client.
  Aadhaar is masked server-side and never unmasked in the UI. No Student module/route exists yet: the detail page shows "Student ID: n" as plain text.
  The shared table's `type: 'badge'` column accepts a `tone(value)` function (gray|blue|green|red|violet|gray-strike).
- Existing Student Onboarding (`features/student-onboarding/`, route `/students/onboard-existing`, nav "Add Existing Student", School Admin only; API
  `POST /api/StudentOnboarding/{schoolId}/existing-student`). Direct entry into tb_students + tb_student_enrollments + tb_guardians (+ link), NO tb_admissions
  row and no review stage. The student's admission type is the school's own Admission Type NAMED "Existing Student" (tb_admission_types has no code
  column, so it is found by name, case-insensitive; never auto-created — the page shows a banner linking to /school-setup/admission-types when it is
  missing or inactive). Admission number: the school's own if supplied, else ADMNO{admission year}{4-digit seq} per school (same running number as
  Confirm Admission). Admission/enrollment dates are the historical admission date, not today. Needs Docs/smss-students-aadhar-number.sql
  (tb_students.aadhar_number + a per-school partial unique index) — the TbStudent entity maps that column, so RUN THE MIGRATION BEFORE DEPLOYING the API
  build, or every tb_students query/insert (including Confirm Admission) fails.
- Students (`features/students/`, routes `/students` and `/students/:studentId`, nav "Students", School Admin only; read-only — NO edit affordance until a
  student-update backend exists; API `GET /api/Student/{schoolId}?academicYearId=&classSectionId=` and `GET /api/Student/{schoolId}/{studentId}`).
  There is NO separate Class-Section mapping table/service: a section belongs to one class (tb_sections.class_id), and an enrollment's `class_section_id`
  is a tb_sections.section_id. So the list's Section dropdown = active sections of the chosen class (sections are NOT academic-year scoped). The list needs
  Year + Class + Section before it queries. Aadhaar is masked server-side. The profile's "current enrollment" = the enrollment in the school's current
  academic year, else an Active one, else the most recent year (then highest enrollment id) — see StudentService. `students.routes` must be mounted AFTER
  `student-onboarding.routes` in app.routes.ts (so /students/onboard-existing isn't read as a :studentId); a spec guards that.
- Student photos: `POST /api/Student/{schoolId}/{studentId}/photo` (multipart "file") and `.../photo/remove`; `profilePhotoUrl` on the list + profile DTOs
  (relative `/uploads/{schoolCode}/students/{studentId}/photo_<ts>.<ext>`; use `toUploadUrl()` from core/utils). Same rules and storage as the school logo:
  ONE validator `ImageUploadValidator` (helper/UploadRules.cs: 2 MB, PNG/JPG/WEBP, magic-byte check) shared by logo + photo, and the same
  IFileStorageService (`SaveStudentPhotoAsync`). The frontend reuses `LogoUploadComponent` (inputs noun='photo', icon='user'); the student is created
  first, then the photo is uploaded as a best-effort follow-up (a failure only warns). Table has a `type: 'avatar'` column kind.
- school_id generation (backend, `SchoolIdHelper` + `RegisterSchoolAsync`): `{PREFIX}{yyMMdd}{seq:D3}`, e.g. FIRS261013001. PREFIX = first 4 of A-Z/0-9 from the
  short name (else the school name; "SCHL" if neither leaves anything), yyMMdd = the UTC date, seq = schools created that UTC day + 1 (counted with an explicit
  UTC range, NOT `created_at::date`, because the DB session time zone is Asia/Calcutta). The id is also the login username. Legacy ids (sch2026xxx,
  SUAD0000001) are untouched. Two registrations in the same moment compute the same seq: the loser gets 409 "Registration ID collision — please submit again."
  and nothing is saved. Frontend assumes no id format.
- LOGIN ID vs DISPLAY NAME (2026-10-13): `tb_users.org_user_id` is the ONLY login credential (login request field `loginId`);
  `tb_users.display_name` (was `username`) is a non-unique label. The login/me `user` has `displayName` + `orgUserId` (no
  `username`). Needs `Docs/smss-users-login-id-and-school-short-code.sql` run BEFORE the API build is started.
  School admin login id = the school id (e.g. DPS261013001); `tb_schools.school_short_code` (DPS001, immutable, counted per
  prefix) is the base for student ids. STUDENT ID (2026-10-13): `tb_students.student_id` is the varchar business id
  `{short code}ST{nnn}` (DPS001ST001), generated ONCE by the app when the student record is created (confirm admission /
  existing-student onboarding: count of the school's students + 1; retried on a primary-key collision), and reused as the
  login's `org_user_id` — `POST /api/Student/{schoolId}/{studentId}/create-login` does no id generation; temp password =
  first 4 letters of the first name (A-Z, padded with X) + DOB `ddMM`, `is_first_login = true`. studentId is a STRING
  everywhere in the web app (never `Number()` a route param). Schools without a short code (registered before it existed)
  get "no short code configured" when admitting / onboarding. Emails (submitted / approved / id assigned / credentials)
  all go through `INotificationService`: own email, else guardian's, else skipped. Needs `Docs/smss-student-id-varchar.sql`. A
  `isFirstLogin` user is held on `/change-password` by `roleGuard` (it re-runs on every child navigation). The API does NOT yet
  reject a first-login token on other endpoints — UI only.
- IMPORT MASTERS (2026-10-13): `/school-setup/import-masters` (first item of School Setup, School Admin only; also where a new
  School Admin lands after replacing the temporary password). `GET /api/MasterImport/catalog` returns the ready-made choices
  (classes Nursery..Class 12, subjects, designations, departments, admission types incl. "Existing Student", document types)
  flagged `alreadyAdded`; `POST /api/MasterImport/import` with the picked keys per group creates only what is missing, in one
  transaction, always for the token's school. Each imported class gets Section "A" (an existing class that already has
  sections is left alone). Matching is case-insensitive; unknown keys -> 400. The catalog lives in
  `smss-api-service-layer/helper/MasterImportCatalog.cs` — add new choices there.
- FEE SETUP (2026-10-14, Docs/roles-workflow.md section 27; needs `Docs/smss-fee-setup.sql`): backend is complete for Fee Types, Fee Structures and
  Fee Concessions (`/api/FeeType`, `/api/FeeStructure`, `/api/FeeConcession`; all scoped to the token's school, POST toggle-status, 404 for another
  school's row). `isactive` is a BOOLEAN on these tables (not lut_status); responses also carry `statusName` so the generic school lookup list works
  unchanged. `GET /api/FeeStructure/{id}` returns the header plus its lines (fee type + frequency names joined); `GET /api/FeeStructure/frequencies`
  is the global lut_fee_frequency list. Structure = one per school + academic year + class (year/class are fixed after create); PUT replaces the
  lines (matched on fee type). Frontend so far: ONLY Fee Types, `/fee-setup/fee-types`, sidebar group "Fee Setup", via `FEE_TYPE_CONFIG` in
  `school-lookups.config.ts` (config got `codeRequired`, `codeMaxLength`, `group`). Fee Structures screens built (`features/fee-setup/`): list `/fee-setup/fee-structures`, builder `/fee-setup/fee-structures/new` and `/:feeStructureId/edit` (one dedicated
  FormArray page: year + class + name, then fee head rows; fee types already used in another row are disabled via `SelectOption.disabled`; totals are per
  frequency; year/class are locked when editing). Fee Concessions built (`/fee-setup/fee-concessions`, dedicated list + form modal in the class-list style because it has type/value/applies-to fields the generic
  name/code/description modal can't hold; `formatConcessionValue` gives "15%" / "₹2,000"). Unique (school_id, concession_code); value rules: PERCENTAGE 0<v<=100, FIXED v>0.
  Student Fee Assignment is a later module (needs the Student module).
- STUDENT FEE ASSIGNMENT (2026-10-14; needs `Docs/smss-student-fee-assignment.sql`): a student's class and academic year are NOT on tb_students — they live on
  tb_student_enrollments (academic_year_id + class_section_id -> tb_sections.class_id). `tb_student_fee_structure` (one per student per academic year) and
  `tb_student_fee_concessions` (unique student + concession). Confirm Admission and Existing-Student onboarding auto-assign the ACTIVE structure for
  school + class + year inside the same save; no match never blocks — the response carries `warnings` (shown as toasts) and `feeStructureName`.
  API (token school, no school id in the route): `GET /api/StudentFee/{studentId}`, `PUT .../structure`, `POST .../concessions`, `DELETE .../concessions/{concessionId}`;
  GET returns lines + concessions + net payable per frequency + `availableStructures` / `availableConcessions` in one response. Discount rules (StudentFeeService.Discounts):
  PERCENTAGE = % of each line it applies to; FIXED on a fee head = off that line; FIXED on the whole structure is spread over the lines in proportion to their
  amounts; stacked, capped at each line. Student profile has Profile | Fees tabs (`features/students/components/student-fees-tab`). Fee Collection / receipts are a later module.
  The automatic assignment runs AFTER the student is saved (`FeeAutoAssignment`): if it cannot be written the student stays and a warning is returned. Every request
  is wrapped by `GlobalExceptionMiddleware` (ApiResponse 500, full error in the log). RUN THE SQL FILES BEFORE DEPLOYING A BUILD THAT MAPS NEW TABLES.
- STUDENT FEE BILLING, STAGE 2 (2026-10-16, Docs/roles-workflow.md section 28; needs `Docs/smss-student-fee-items.sql`, after the assignment SQL): `tb_fee_structure_details.due_day`
  (required for Monthly/Quarterly/Half-Yearly lines, null otherwise), `tb_student_fee_items` (per student per year per fee head: the frequency THIS student pays at,
  is_applicable, base/concession/net PER PERIOD of the structure line) and `tb_student_fee_dues` (generated installments; `amount_paid` is written only by payments, a later
  stage). student_id is varchar(20) (section 28 says bigint). `StudentFeeBillingService.SyncAsync` is the one place that materialises items + dues; it runs after admission,
  assign/re-assign, concession add/remove, item edit and structure edit, and GET fee-items/fee-dues heal a student that has a structure but no items. Rules: year total of an item =
  net x periods of the STRUCTURE frequency, split over the student's frequency (Monthly 12, Quarterly 4, Half-Yearly 2, Annual/One Time 1; dates start in the academic year's start month,
  day = due day cut to the month length); One Time/Annual fees stay single charges, recurring ones can be paid Monthly/Quarterly/Half-Yearly/Annual (`FeeSchedule`). Regeneration keeps any
  installment with a payment or already past its date and re-spreads the rest over the unpaid future slots (slots = months, so a due-day change never makes a second installment).
  Status (Paid / Partially Paid / Pending / Overdue) is COMPUTED, never stored: Overdue = something owed and date passed (beats Partially Paid), due today is Pending; "today" is IST (`SchoolClock`).
  API: `GET /api/StudentFee/preview?academicYearId=&classId=` (admission form), `GET .../{studentId}/fee-items`, `PUT .../fee-items/{itemId}` {frequencyId?, isApplicable?}, `GET .../fee-dues`;
  Confirm Admission / existing-student requests take optional `feeItems: [{feeTypeId, frequencyId?, isApplicable?}]`. UI: `FeeConfigurationComponent` (existing-student form + confirm dialog),
  Fees tab shows items (edit frequency/applicability) and the due schedule with status pills (table tone `yellow` added). NO is_optional flag exists on tb_fee_types: the
  admission-time include/exclude checkbox is intentionally NOT built (waiting on a decision); applicability is editable per item from the Fees tab.
  UPDATE 2026-10-16 (needs `Docs/smss-fee-types-optional.sql`): `tb_fee_types.is_optional` (default false; "This fee is optional" checkbox on the Fee Types form + an Optional column). Only OPTIONAL
  heads get an include/exclude checkbox on the admission forms and are opt-in (unticked by default; also when `feeItems` is omitted); NON-optional heads always apply, the server ignores an
  attempt to switch one off at admission and `PUT fee-items` returns 400 for it. Mid-year admissions: installments are generated only from the student's start month (their
  enrollment date for that academic year, else the year start) for Monthly / Quarterly / Half-Yearly - same amount per installment, fewer of them; a single charge (One Time / Annual) is still
  billed in full but dated from the start date, never earlier. Schedules created before this rule are left alone (Sync does not count pre-start installments against what is owed).
  The preview endpoint takes `startDate` and reports `remainingInstallments` per frequency.
- STUDENT FEE BILLING, STAGE 3 - PAYMENT COLLECTION (2026-10-16; needs `Docs/smss-fee-payments.sql`): `tb_fee_payments` + `tb_fee_payment_allocations`; the ONLY thing that moves `tb_student_fee_dues.amount_paid`.
  API `/api/FeePayment`: `POST students/{studentId}` {amount, paymentMode, allocations?: [{feeDueId, amount}], remarks} (explicit split, or none = oldest installment first), `GET students/{studentId}` (history, newest first),
  `GET {paymentId}/receipt`. Rules: modes Cash/Cheque/UPI/Card/Bank Transfer/Online; allocations must add up to the amount exactly; every due must be this student's and this school's; an allocation above an installment's
  outstanding is refused (no overpayment, no silent loss); auto mode refuses an amount above the total outstanding. ONE transaction: installments are locked `FOR UPDATE` (two simultaneous payments can't both pay the same
  installment), payment + allocations + amount_paid are one SaveChanges. Receipt number `RCP-{yyyy}-{nnnn}`: per school per (IST) calendar year, max+1 under the unique (school_id, receipt_number) index with a 3-try
  retry. Collector comes from the token. NO void/reverse yet - waiting on a decision (see the report; `DELETE` is deliberately not mapped). Mid-year floor is now "first due date ON OR AFTER the admission date" (a student
  admitted 15 Oct with due day 1 first owes 1 Nov); a Quarterly/Half-Yearly fee whose remaining slots are all before the admission date leaves NO installment for that fee (flagged, not changed).
  UI: Fees tab -> "Collect Payment" (`CollectPaymentDialogComponent`: typing an amount splits it oldest-first, edits override, submit only when the split equals the amount, money compared in paise) -> receipt
  (`FeeReceiptDialogComponent`, Print / Save as PDF via a standalone escaped page in `utils/receipt-print.ts`), and a Payment history card with "Receipt".

## Stage 3 closing — void payment + due-date gap fix
- Payments are SOFT-voided (`POST /api/FeePayment/{id}/void`, body `{voidReason}`): row + allocations kept, `status='Voided'`, voided_by/at/reason set, allocated amounts subtracted from `tb_student_fee_dues.amount_paid` in one transaction. Receipt numbers are never reused. Only School Admin / Accountant (`User.CanManageFees()` on the API, `FEE_MANAGER_ROLES` in `fee-payment.model.ts`); already-voided or other-school payment → 404.
- Installment status is computed live, so a voided Paid installment reads Overdue/Pending again with no extra work.
- Voided receipts still open/print, with a VOID banner + watermark (`receipt-print.ts`, `fee-receipt-dialog`).
- `FeeSchedule.OwedSchedule`: if no configured due date is left on/after the start date, ONE installment dated at the start date for the normal per-period amount (no proration).

## Stage 4 — fee status on the student list + per-item payments
- `GET /api/Student/{schoolId}?academicYearId&classSectionId` now returns `feeStatus` (Overdue | Partially Paid | Pending | Paid | Not Set) + `feeTotalDue/Paid/Outstanding` per student, from ONE grouped query (`StudentRepository.GetFeeAggregatesAsync`) over the whole list. Rule lives in `FeeSchedule.Overall` (Not Set = no installments for that year).
- List badge reuses `dueStatusTone` (Overdue red, Partially Paid blue, Pending yellow, Paid green, Not Set grey) so it matches the Fees tab.
- Fees tab fee-items table has Paid / Outstanding columns; payments and voids reload the items.
- Receipt numbers: `FeePaymentRepository.LockReceiptNumbersAsync` (pg advisory xact lock per school) serializes number allocation; the unique-violation retry stays as a backstop.
- Fee status (closeout): `FeeSchedule.Overall(hasItems, ...)`: Not Set ONLY when no fee items exist for the year; items with nothing owed (optional heads off, full concession) = Paid. List endpoint takes optional `feeStatus` (All/blank = none; unknown = 400), filtered server-side; the list's Fee status dropdown reloads from the server and rides along in the Back-to-Students query params.

## Stage 5 — Fee dashboard + class report (read-only)
- API: `GET /api/FeeDashboard?academicYearId&classId&sectionId&days=7` and `GET /api/FeeReports/class/{classId}?academicYearId&sectionId`. Access = `User.CanViewFees()` (School Admin, Accountant, Principal); responses carry `canManage` (= `CanManageFees()`), false for the Principal, whose UI hides drill-down + CSV export. First view-vs-manage split in the project: just these two helpers in ClaimsPrincipalExtensions.
- Dashboard = 4 SQL statements (year, class check, one aggregate over dues, one monthly sum over Active allocations); class report reuses `IStudentRepository.GetFeeAggregatesAsync` (same rule as the student list).
- Collected = money on Active payments' allocations to in-scope installments (equals sum of amount_paid; voided excluded). Month trend = by payment date, IST.
- No student-wise report endpoint: the student's Fees tab (fee-items + fee-dues + payments) IS the statement.
- Frontend: `features/fee-reports` (/fee-dashboard, /fee-reports/class), nav group "Fees"; month trend is a plain table with CSS bars (no chart lib); CSV export is a Blob, no library.

## Accountant access to the student record
- `/students/:studentId` is open to School Admin + Accountant. For an Accountant the page shows the student summary header and the Fees tab ONLY (Profile tab with photo / student-login actions is School Admin only); "Back" goes to the class report. Inside the Fees tab, structure assign / concessions / per-item edit are School Admin only (UI-only: the API has no role checks on those endpoints yet); collect + void are available to both.
- The Accountant reaches a student via Fee Reports → Class-wise Report → "Fees". `/students` (the list) stays School Admin only.
