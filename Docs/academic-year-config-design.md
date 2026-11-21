# Academic Year Configuration and Management: Stage 0 audit and design

Status: **design for review. Nothing in this document has been built.** No code, no migration and no database was changed to produce it.
Evidence base: the repository at commit `d2bb891`, and **read-only SQL on a scratch cluster** (database `smss_tpl_push` on port 55432: a restore of
`Docs/smss-db(14Nov).sql` plus the later RUN-ME scripts). Row counts below are from that scratch copy, **not from the live `smss-db`**; the migration's verification
block therefore compares before/after snapshots taken inside its own transaction instead of hard-coding numbers.

---

## 0. Summary

Today the system has **one year-free set of masters** (`tb_classes`, `tb_sections`, `tb_subjects`, `tb_class_subjects`, `tb_periods`) and **a handful of year-scoped
tables** (`tb_class_teacher_assignments`, `tb_timetable_entries`, `tb_student_enrollments`, fee, attendance, homework, exam tables). A second academic year would
silently share the first year's classes, sections, subject mapping and bell schedule. All three schools have exactly one year (`2026-27`), so nothing is broken yet.

The design keeps **every existing primary key and foreign key target unchanged** (`class_id`, `section_id`, `subject_id`, `period_id`, `academic_year_id`) and adds
the missing **per-year layer beside them**: offering tables for classes and sections, a year column on the subject mapping, schedules for periods, effective dates on
class teachers, a close marker on the year, and a log for the year-to-year copy. Dependants (fees, enrolments, attendance, exams, homework) are not re-keyed.

| Design rule | Consequence |
|---|---|
| Masters stay year-agnostic and keep their ids | no dependant table needs a key change; no data moves |
| "Which classes/sections/subjects exist in year Y" is a new per-year row, never a copy of the master | a master is never duplicated |
| Status values are **derived** (year state, current class teacher, resolved schedule); stored only as soft-delete `status_id` and as `closed_at`/`effective_to` timestamps | nothing drifts |
| Every multi-row config save and every copy takes one per-school advisory lock | two admins cannot interleave a copy and a save |
| Every migration is additive, idempotent, one transaction, with a verification block that raises (and so rolls back) on any mismatch | same pattern as `Docs/smss-fee-effective-from-RUN-ME.sql` |

---

## A. Audit: what exists today

### A.1 Tables (schema read from `smss_tpl_push`)

| Table | Keys and constraints that matter | Gap for this feature |
|---|---|---|
| `tb_academic_years` | PK `academic_year_id`; `UNIQUE (school_id, year_name)`; `UNIQUE (school_id, academic_year_id)`; `CHECK end_date > start_date`; `is_current boolean`; `status_id` | **No DB rule for one current year** (service only: `AcademicYearService.SetCurrentAsync`). **No overlap guard.** No closed marker. Dates editable freely even when dues, attendance or events exist. |
| `tb_classes` | PK `class_id`; `UNIQUE (school_id, class_name)`; `sequence_order`; `status_id`; **no year** | Year-free master. Fine as a master; nothing says which classes exist in which year. |
| `tb_sections` | PK `section_id`; `UNIQUE (class_id, section_name)`; `UNIQUE (section_id, class_id)`; `max_strength`; **no year** | A section row *is* "class + section name". Year-free. `max_strength` cannot differ by year. |
| `tb_subjects` | PK `subject_id`; `UNIQUE (school_id, subject_name)`; `subject_code` | Adequate as a master. |
| `tb_class_subjects` | PK; **`UNIQUE (class_id, subject_id)`**; no `school` composite key; **no year, no section** | Mapping is the same in every year; cannot vary by section. |
| `tb_periods` | PK `period_id`; **`UNIQUE (school_id, period_number)`**; `CHECK end_time > start_time`; no overlap rule in the DB (service-only, `PeriodService`) | One bell schedule per school. No break slots (breaks exist only as gaps, e.g. APS 11:15–11:45), no named schedules, no per-class or per-day variation, no year. |
| `tb_timetable_entries` | PK; FK `(section_id, class_id)`; FK `year`, `period`, `subject`, `employee`; `CHECK day_of_week 1..6`; **partial unique `uq_tb_tte_one_active_per_slot (school, year, class, section, day, period) WHERE status=1`** and **`uq_tb_tte_teacher_one_active_per_period (school, year, employee, day, period) WHERE status=1`** | Double-booking is already impossible at DB level for the same `period_id`. Not covered: a teacher in two sections whose periods have different `period_id` but overlapping clock times (only possible once several schedules exist). |
| `tb_class_teacher_assignments` | PK; FKs to year, class, `(section_id, class_id)`, employee, user; **partial unique `uq_tb_cta_one_active_per_section (school, year, class, section) WHERE status_id = 1`**; no dates | One current teacher per section per year is enforced. "History" is only inactive rows ordered by `updated_at`: no `effective_from/to`, no reason, no who-ended. A teacher may hold several sections (no rule; no such data today). |
| `tb_student_enrollments` | PK; **`UNIQUE (student_id, academic_year_id)`**; `class_section_id` (= `tb_sections.section_id`, **no FK**); `roll_number` | Placement is already per year. The section is not checked against "offered in that year" (nothing to check against). |
| Teacher↔subject link | none (no table) | Derived from `tb_timetable_entries` only. CLAUDE.md lists teacher-subject mapping as not built. |

### A.2 Data in the scratch copy (read-only counts)

| School | Years | Classes | Sections | Subjects | Class-subject rows | Periods | Timetable rows | Class-teacher rows (active) | Enrolments / students |
|---|---|---|---|---|---|---|---|---|---|
| APS `APS261013001` (`12345678912`) | 1 (`2026-27`, current) | 8 | 8 | 5 | 4 | 8 | 26 | 1 (1) | 2 / 2 |
| GHS `GHS261103002` (`GHS-RKE-2026`) | 1 (current) | 13 | 15 | 21 | 109 | 6 | 540 | 15 (15) | 176 / 176 |
| LAS `LAS261103001` (`LAS-DDN-2026`) | 1 (current) | 13 | 15 | 21 | 109 | 6 | 541 | 15 (15) | 177 / 177 |

Other facts that make the backfill safe: no inactive class, section, subject or mapping row anywhere; no timetable row uses a subject that is not mapped to its class;
no enrolment points at a missing or inactive section; no teacher is class teacher of two sections; exactly one current year per school and no overlapping years; the
`btree_gist` extension is already installed (it is used by the leave module), so exclusion constraints are available.

### A.3 Code that touches these tables

| Layer | Where |
|---|---|
| Entities (`smss-api-db-layer/entity`) | `TbAcademicYears`, `TbClasses`, `TbSections`, `TbSubjects`, `TbClassSubjects`, `TbPeriod`, `TbTimetableEntry`, `TbClassTeacherAssignment`, `TbStudent`/enrolment entities |
| Services (`smss-api-service-layer/service`) | `AcademicYearService` (205 lines), `ClassService`, `SectionService`, `SubjectService`, `ClassSubjectService` (bulk replace with optimistic 409), `PeriodService`, `TimetableService` (308), `ClassTeacherService` (256) |
| Controllers (`smss-api/Controllers`) | `AcademicYearController`, `ClassesController`, `SectionsController`, `SubjectsController`, `ClassSubjectsController`, `PeriodController`, `TimetableController`, `ClassTeacherController`, `MasterImportController` |
| Web (`smss-web/src/app/features`) | `school-setup/pages/academic-year-list`, `class-list`, `section-list` (with a Class Teacher dropdown per section), `class-subject-mapping` (checklist + bulk save), `school-lookup-list` (subjects), `import-masters`; `timetable/pages/period-list`, `timetable-admin`, `my-timetable`, `timetable/components/slot-modal`, `timetable-grid` |
| Mobile (`smss-mobile`) | read-only: `src/features/student/TimetableScreen.tsx`, teacher `HomeScreen`/`AttendanceTab`/`HomeworkTab`/`MarksTab` (sections come from `/api/Timetable/my`, `/api/Attendance/my-sections`) |

What the services already validate (so the design reuses it rather than re-inventing it): `TimetableService` checks the year and section belong to the school, the section
and class are Active, the period exists and is Active, the subject is Active and **mapped to the class** (`MappedToClass`), the teacher is an Active **Teaching** employee whose employment
status does not block login, a teacher clash (`FindTeacherConflictAsync`) and the slot (`GetActiveSlotForUpdateAsync`), all under a per-(school, year, day, period) advisory lock
(`TimetableRepository.LockSlotAsync`). `ClassTeacherService` requires an Active Teaching employee with an Active login holding the Teacher role and locks per section.
`PeriodService` refuses overlapping times, duplicate numbers, and deactivating a period used by timetable rows.

### A.4 Gap table for requirements 1 to 8

| # | Requirement | Verdict | Evidence and what is missing |
|---|---|---|---|
| 1 | Create and manage multiple academic years independently | **Exists, needs change** | Create/edit/toggle/set-current work (`AcademicYearService`). Missing: one-current and no-overlap as DB rules, a closed state, a guard on editing dates once dated data exists, derived state (upcoming/current/past/closed), a per-year summary. |
| 2 | Class and section masters, configured separately per year | **Exists (masters adequate), per-year layer missing** | `tb_classes`/`tb_sections` are year-free. Nothing records which classes/sections exist in a year or a per-year capacity. |
| 3 | Subject master; different subjects per class per year | **Master adequate; per-year mapping missing** | `tb_subjects` is fine. `tb_class_subjects` has no year and no section (unique on `class_id, subject_id`). |
| 4 | Reusable period definitions, timings, schedules (breaks, per-day) | **Exists, needs change** | `tb_periods` is a single per-school list with `UNIQUE (school_id, period_number)`; no break slots, no schedules, no per-class/per-day variation; overlap guarded only in the service. |
| 5 | Class-subject mapping to class **and section**, per year | **Exists, needs change** | See 3. Every consumer filters by class only (see B). |
| 6 | Class teacher assign/reassign with full history | **Exists, needs change** | Year-scoped, one current per section enforced by a partial unique index, history kept as inactive rows. Missing: effective dates, assign/end reasons, who ended it, a stated multi-section rule, a DB rule that history periods do not overlap. |
| 7 | Section-wise timetable per year with conflict validation | **Exists, largely adequate** | Both double-booking rules are DB partial unique indexes; the service validates teacher, subject mapping, section/class status. Missing: break slots, "period belongs to the schedule that applies to this class and day", cross-schedule clock overlap, closed-year block, a dry-run validate endpoint. |
| 8 | Year transition: copy config from the previous year | **Missing** | Nothing copies between years. `MasterImportService` only creates masters. |

---

## B. Dependency map: what references these concepts

Principle: **no key that any module already uses is renamed, retyped or repointed**, so the "what breaks" column is about *behaviour that must start to respect the year*,
not about schema.

| Module | What it references | What could break | How the design avoids it |
|---|---|---|---|
| Students and enrolment (`StudentRepository`, `StudentOnboardingService`, `AdmissionService.ConfirmAdmissionAsync`) | `tb_student_enrollments (year, class_section_id)`; section lists "active sections of the class" | A student placed into a section that is not offered in that year | Section pickers read the **year's offered sections** (`?academicYearId`, default current). Service rule on write. Optional composite FK to the offering after an orphan check (stage 8). Backfill makes today's behaviour identical. |
| Fee structures, dues, Collect Payment (`FeeStructureService`, `StudentFeeBillingService`, `FeePaymentService`) | `tb_fee_structures UNIQUE (school, year, class)`; dues are keyed by year through items | Creating a structure for a class not offered that year; closing a year blocking collection of old dues | Structure create checks the class is offered in the year. **Payments and voids stay allowed in a closed year** (money owed does not vanish). No fee row is touched by the migration. |
| Exams and report cards (`ReportSetupRepository`, `ReportMarksRepository`, `ReportSchemeService`) | `tb_report_schemes (year)`, `tb_report_scheme_classes UNIQUE (year, class)`, `tb_report_scheme_subjects (class, subject)`, term-section and publication tables key by `(section, class)`; **read `tb_class_subjects` by class** | Once the mapping is per year, a year-2027 scheme would read year-2026 mappings | The repositories gain `AND academic_year_id = <scheme year>` (stage 3, listed explicitly). Scheme subjects must be a subset of the year's mapping (service). |
| Attendance (`AttendanceRepository`, `AttendanceService`) | `tb_student_attendance (year, class, section)`, roster from enrolments; "my sections" | Marking in a closed year; sections not offered | Closed-year block on submit/correct; section list from the year's offering. Rows untouched (`UNIQUE (student_id, attendance_date)` stays). |
| Homework, class tests (`HomeworkRepository`, `AssessmentRepository`) | `(year, section, subject)`; subject must be mapped to the class | Mapping lookup ignores the year | Same `academic_year_id` filter as exams; subject check uses the **effective** subjects of the section (class mapping ± override). |
| Notices | school-level audiences only | none (no class/section/year link) | no change |
| Transport (`StudentTransportRepository`) | `tb_student_transport (school, year)` composite FK to years | none for classes; year must stay referenceable | Years are never deleted, only deactivated/closed; no change to the table. |
| ID cards (`StudentIdCardRepository`) | current year via `is_current`, class/section **names** via enrolment | Name lookups | Names still come from the master rows; `is_current` untouched. |
| Dashboards (School Admin, Principal, Teacher, Student, Accountant, Admission) | `is_current` year; counts of active sections; class-teacher lists | Counting masters instead of the year's offering | Counts switch to the offering of the current year (identical after backfill). The 25 repository reads of `IsCurrent` are not changed in meaning. |
| Imports (`MasterImportService`, `StudentImportService`) | creates classes + Section A; student import lists active classes/sections and the current year | Imported classes would not appear in the current year | Import Masters also upserts the **current year's offering** for what it creates (stage 2). Student import reads the offering. |
| Onboarding gate (`OnboardingRepository`, shipped 2026-11) | active classes, sections, fee structure for the current year | Gate would judge masters, not the year | Gate switches to "classes offered in the current year"; backfill makes today's answer identical; a test pins it. |
| Reports (class fee report, section fee status, fee dashboard filters, attendance report) | class/section/year pickers | Pickers offering classes absent in the chosen year | Pickers pass `academicYearId` to the offering endpoints. |
| Subscription billing (`SubscriptionRepository` lines ~68, ~285) | counts enrolments in the year with `is_current` | A year transition or "set current" changing the billed count | **`is_current` semantics are not changed.** Documented as a coupling: setting a new current year changes the billed student count. |
| Push notifications (`NotificationRepository`, `FeeReminderService`) | current year, fee aggregates | none | none |
| Mobile (student timetable, teacher sections/attendance/homework/marks) | `/api/Timetable/student/my`, `/api/Timetable/my`, `/api/Attendance/my-sections`, section ids | A changed response shape | **No DTO shape changes** on existing endpoints; new parameters are optional. Mobile impact is read-only and optional (section E). |
| Web pickers everywhere (`/api/Classes`, `/api/Sections`) | master lists | Pages that expect all classes | Both endpoints stay as masters; `?academicYearId=` returns the year's offering; without it the response is exactly today's. |

---

## C. Proposed design (normalized)

Conventions used by every table below: `school_id VARCHAR(100) NOT NULL` with an FK to `tb_schools`; keys `BIGINT GENERATED ALWAYS AS IDENTITY`; `created_at`/`updated_at`
`TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP`; `created_by`/`updated_by BIGINT NULL` FK `tb_users`; soft delete = `status_id` FK `lut_status` (Active/Inactive, **resolved by name
inside each script, never hard-coded**: the partial-index literal is filled in when the script runs, as `smss-timetable.sql` does). The tenant comes only from the token.

Cross-tenant safety uses the project's composite-key pattern: parent tables get a harmless `UNIQUE (school_id, <id>)` so children reference `(school_id, <id>)`, making
it impossible for a row of school A to point at school B (today `tb_class_subjects` and `tb_classes` cannot guarantee that; only the service does).

### C.1 Overview

| Table | Kind | Change |
|---|---|---|
| `tb_academic_years` | master of years | ALTER |
| `tb_classes`, `tb_sections`, `tb_subjects` | masters | ALTER only to add `UNIQUE (school_id, id)` helper keys |
| `tb_academic_year_classes` | per-year config | **NEW** |
| `tb_academic_year_sections` | per-year config | **NEW** |
| `tb_class_subjects` | per-year mapping | ALTER (adds the year) |
| `tb_section_subject_overrides` | per-year mapping exception | **NEW** |
| `tb_period_schedules` | master | **NEW** |
| `tb_periods` | master (slots of a schedule) | ALTER |
| `tb_academic_year_schedules` | per-year config | **NEW** |
| `tb_class_teacher_assignments` | per-year + history | ALTER |
| `tb_timetable_entries` | per-year | ALTER (two FKs) |
| `tb_year_copy_log` | log | **NEW** |

Six new tables, five altered, three masters touched only for helper keys.

### C.2 `tb_academic_years` (master of years): ALTER

New columns: `closed_at TIMESTAMPTZ NULL`, `closed_by BIGINT NULL FK tb_users`, `close_note VARCHAR(250) NULL`.

New constraints and indexes:
- `CREATE UNIQUE INDEX uq_tb_ay_one_current ON tb_academic_years (school_id) WHERE is_current`: **one current year per school, enforced by the database**.
- `EXCLUDE USING gist (school_id WITH =, daterange(start_date, end_date, '[]') WITH &&) WHERE (status_id = <Active>)`: **no overlapping Active years** (inclusive ends, so 31 Mar and 1 Apr are fine; a year that starts on the day the last one ends is rejected). Deactivated years may overlap (they are retired); re-activating one that overlaps is refused by the service with a readable 409.
- `CHECK (closed_at IS NULL OR is_current = false)`: the current year cannot be closed.
- Existing `UNIQUE (school_id, year_name)` and `UNIQUE (school_id, academic_year_id)` stay.

**Derived (never stored) year state**: `UPCOMING` (today < start), `CURRENT` (`is_current`), `PAST` (end < today and not current), plus `CLOSED` (`closed_at IS NOT NULL`, which wins for display and for write blocking). "Today" is the India date (`SchoolClock.Today()`).

**What closing means** (proposed; see question Q2): `POST /years/{id}/close` stamps `closed_at/closed_by`. A closed year **blocks every write to its configuration and operational records**: offerings, subject mapping and overrides, schedule assignment, class teachers, timetable entries, new enrolments, attendance submit/correct, homework and class-test create/edit, report-setup edits. It **does not block** fee payments and voids against its dues, receipts, report-card viewing/PDF, or any read. A School Admin can `reopen` (logged in the audit columns). The current year cannot be closed, and a year cannot be made current while closed.

**What is blocked once data exists** (service rules, 409 with counts):
- Start/end dates can be changed only to a range that still contains every dated row of the year: attendance dates, fee due dates and fee-line `effective_from`, school events, enrolment dates. Name edits are always allowed.
- Deactivating a year that has enrolments, fee structures, attendance or timetable rows is refused (today only the current year is protected).
- Years are never deleted.

### C.3 Masters: `tb_classes`, `tb_sections`, `tb_subjects`: unchanged except helper keys

Add `UNIQUE (school_id, class_id)` on `tb_classes`, `UNIQUE (school_id, section_id)` on `tb_sections`, `UNIQUE (school_id, subject_id)` on `tb_subjects` (all are supersets of the primary key, so they can never fail on existing data). Nothing else changes: ids, names, `sequence_order`, `max_strength`, status stay. Deactivating a master that is offered in any **non-closed** year with Active enrolments or a fee structure is refused by the service (today there is no such rule).

*Section master.* A `tb_sections` row is "class + section name" and stays so: the name `A` is reusable across classes by naming, and per-year capacity lives in the offering (below). A separate name-only master was considered and rejected as a pure rename with no consumer (every dependant already FKs `(section_id, class_id)`); see Q1.

### C.4 `tb_academic_year_classes` (per-year config): NEW

Purpose: which classes exist in which year.

| Column | Type | Notes |
|---|---|---|
| `year_class_id` | BIGINT identity | PK |
| `school_id` | VARCHAR(100) NN | |
| `academic_year_id` | BIGINT NN | |
| `class_id` | BIGINT NN | |
| `status_id` | INT NN | Active = offered, Inactive = withdrawn from this year |
| audit | | `created_at/updated_at/created_by/updated_by` |

FKs: `(school_id, academic_year_id) → tb_academic_years (school_id, academic_year_id)`; `(school_id, class_id) → tb_classes (school_id, class_id)`.
Unique: `UNIQUE (academic_year_id, class_id)` (also the target of child FKs). Index `(school_id, academic_year_id)`.
Delete behaviour: never deleted; Inactive when withdrawn; withdrawing is refused while the year has Active enrolments or a fee structure for that class.

### C.5 `tb_academic_year_sections` (per-year config): NEW

| Column | Type | Notes |
|---|---|---|
| `year_section_id` | BIGINT identity | PK |
| `school_id`, `academic_year_id`, `class_id`, `section_id` | NN | |
| `max_strength` | SMALLINT NULL | per-year capacity; NULL = use the master's `max_strength` |
| `status_id` | INT NN | |
| audit | | |

FKs: `(school_id, academic_year_id) → tb_academic_years`; **`(academic_year_id, class_id) → tb_academic_year_classes (academic_year_id, class_id)`** (a section can only be offered when its class is); `(section_id, class_id) → tb_sections (section_id, class_id)` (existing key).
Unique: `UNIQUE (academic_year_id, section_id)`. `CHECK (max_strength IS NULL OR max_strength > 0)`.
Delete behaviour: as above; withdrawing refused while Active enrolments, class teacher or timetable rows exist for that section and year.

### C.6 `tb_class_subjects`: ALTER (subjects per class per year)

Add `academic_year_id BIGINT` (backfilled, then `NOT NULL`). Replace `UNIQUE (class_id, subject_id)` with `UNIQUE (academic_year_id, class_id, subject_id)`.
New FKs: `(school_id, academic_year_id) → tb_academic_years`; `(academic_year_id, class_id) → tb_academic_year_classes` (a subject is mapped only to a class offered that year); `(school_id, subject_id) → tb_subjects (school_id, subject_id)`.
Existing FKs and `status_id` stay. Index `(school_id, academic_year_id, class_id)`.
Consequence: `ClassSubjectRepository` and the five other readers (B) gain the year filter. The existing bulk-replace save (`POST /ClassSubjects/bulk`, optimistic 409) keeps its behaviour per (year, class).

### C.7 `tb_section_subject_overrides`: NEW (optional per-section exception)

Purpose: a subject taught in one section but not its class (ADD), or dropped for one section (REMOVE).

| Column | Type | Notes |
|---|---|---|
| `override_id` | BIGINT identity | PK |
| `school_id`, `academic_year_id`, `class_id`, `section_id`, `subject_id` | NN | |
| `action` | VARCHAR(6) NN | `CHECK IN ('ADD','REMOVE')` |
| `status_id` | INT NN | |
| audit | | |

FKs: `(academic_year_id, section_id) → tb_academic_year_sections (academic_year_id, section_id)`; `(school_id, subject_id) → tb_subjects`. Unique: `UNIQUE (academic_year_id, section_id, subject_id)`.
Service rules: `REMOVE` requires the class mapping to exist; `ADD` requires it **not** to exist (otherwise it is a no-op); no override may remove a subject that already has a timetable entry, homework or marks for that section in the year.
**Effective subjects of a section = class mapping − REMOVE + ADD**, computed in one repository method (never stored), and used by the timetable, homework, assessment and exam checks.

### C.8 Periods and schedules

`tb_period_schedules` (**NEW**, master): `schedule_id` PK, `school_id`, `schedule_name VARCHAR(60) NN`, `is_default boolean NN DEFAULT false`, `status_id`, audit.
`UNIQUE (school_id, schedule_name)`; `UNIQUE (school_id, schedule_id)`; **partial unique `(school_id) WHERE is_default`** (one default per school).

`tb_periods` (**ALTER**, slots of a schedule):
- add `schedule_id BIGINT` (backfilled to the school's default schedule, then NOT NULL) with FK `(school_id, schedule_id) → tb_period_schedules`;
- add `slot_type VARCHAR(8) NN DEFAULT 'PERIOD' CHECK IN ('PERIOD','BREAK')`, `slot_label VARCHAR(30) NULL` (for example "Lunch"), `slot_order SMALLINT NN` (display order of every slot, backfilled from `period_number`);
- `period_number` becomes nullable with `CHECK ((slot_type = 'PERIOD') = (period_number IS NOT NULL))` (breaks have no period number);
- **drop `UNIQUE (school_id, period_number)`, add `UNIQUE (schedule_id, period_number)` and `UNIQUE (schedule_id, slot_order)`**;
- add `EXCLUDE USING gist (schedule_id WITH =, timerange(start_time, end_time) WITH &&) WHERE (status_id = <Active>)`: **overlap is impossible in the database** (today only `PeriodService` checks);
- keep `CHECK end_time > start_time` and all existing `period_id` values.

`tb_academic_year_schedules` (**NEW**, per-year config): which schedule applies to which class on which day, in a year.
`year_schedule_id` PK, `school_id`, `academic_year_id`, `class_id BIGINT NULL` (NULL = all classes), `day_of_week SMALLINT NULL` (NULL = all days, `CHECK 1..6`), `schedule_id`, `status_id`, audit.
FKs: year (composite), `(school_id, schedule_id)`, and `(academic_year_id, class_id) → tb_academic_year_classes` when `class_id` is not null.
Unique: `UNIQUE (academic_year_id, COALESCE(class_id,0), COALESCE(day_of_week,0)) WHERE active`.
**Resolution (derived, never stored)** for (year, class, day): the first existing of (class, day) → (class, any day) → (any class, day) → (any class, any day) → the school's default schedule. A school that never uses this table gets the default schedule everywhere, which is exactly today's behaviour. A "winter timing" is another schedule assigned to the year; a half-day Saturday is a (any class, day 6) row.

### C.9 `tb_class_teacher_assignments`: ALTER (assign, reassign, full history)

Add: `effective_from DATE NN`, `effective_to DATE NULL`, `assign_reason VARCHAR(250) NULL`, `ended_by BIGINT NULL FK tb_users`, `end_reason VARCHAR(250) NULL`.
- `CHECK (effective_to IS NULL OR effective_to >= effective_from)`.
- **Current teacher := `effective_to IS NULL`** (derived). New **`UNIQUE INDEX (school_id, academic_year_id, section_id) WHERE effective_to IS NULL`** enforces exactly one current class teacher per section per year without depending on a status literal.
- **`EXCLUDE USING gist (school_id WITH =, academic_year_id WITH =, section_id WITH =, daterange(effective_from, COALESCE(effective_to,'infinity'), '[)') WITH &&)`**: history periods of one section cannot overlap (half-open range, so reassigning on the same day is valid).
- New FK `(academic_year_id, section_id) → tb_academic_year_sections`: a teacher can only be assigned to a section offered that year.
- The existing partial unique on `status_id = 1` stays (the service keeps `status_id` and `effective_to` consistent: ending sets `Inactive` and `effective_to` together; the verification block and a test assert the two agree).
Reassign = end the current row (`effective_to`, `ended_by`, `end_reason`) and insert the new row with `effective_from` = the same day, in one transaction under the per-section advisory lock (the lock already exists in `ClassTeacherRepository`). Nothing is deleted; the history screen reads rows by `effective_from`.
**One teacher, several sections:** proposed rule is *allowed*, because `TeacherDashboardService` and attendance already support a teacher who is class teacher of several sections; the UI shows a confirm warning and no DB rule is added (Q3).

### C.10 `tb_timetable_entries`: ALTER (two FKs only) and the validation list

Keep both existing partial unique indexes (slot and teacher), `CHECK day_of_week 1..6`, and all columns. Add FKs: `(academic_year_id, section_id) → tb_academic_year_sections` (no entry for a section not offered that year) and `(school_id, period_id) → tb_periods (school_id, period_id)` (after adding the helper key to `tb_periods`).

**What the database guarantees:** one entry per (section, day, period) per year, and one entry per (teacher, day, period) per year across the whole school, both for Active rows; entries only for offered sections; periods of the right school.
**What the service validates** (all existing except the marked ones):
1. year belongs to the school, **is not closed** (*new*);
2. section offered in the year and Active, class Active;
3. period exists, Active, **is a PERIOD not a BREAK** (*new*), and **belongs to the schedule resolved for (year, class, day)** (*new*);
4. subject Active and in the section's **effective subjects for that year** (class mapping ± override; *changed from "mapped to class"*);
5. teacher is an Active **Teaching** employee whose employment status does not block login;
6. teacher not already teaching elsewhere in that (year, day, period) (existing) **and not teaching anywhere whose clock time overlaps** once several schedules exist (*new*; see Q6, option to denormalise `slot_start/slot_end` onto entries and add exclusion constraints);
7. day 1..6; same-details idempotent no-op; lock per (school, year, day, period).
A dry-run `POST /api/Timetable/validate` returns the same verdicts without writing.

### C.11 `tb_year_copy_log`: NEW (log)

`copy_id` PK, `school_id`, `source_year_id`, `target_year_id`, `parts JSONB NN` (which parts were selected), `result JSONB NN` (per part: created / skipped-existing / skipped-with-reason counts), `status VARCHAR(10) CHECK IN ('COMPLETED','FAILED')`, `error_note VARCHAR(250) NULL`, `run_by BIGINT`, `started_at`, `finished_at TIMESTAMPTZ`.
`CHECK (source_year_id <> target_year_id)`; composite FKs to both years; index `(school_id, target_year_id, started_at DESC)`. Only real runs are logged (a dry run writes nothing).

### C.12 Year transition (copy operation)

`POST .../years/{target}/copy-from/{source}/preview` and `.../apply`, body `{ parts: [CLASSES_SECTIONS, SUBJECTS, SCHEDULES, CLASS_TEACHERS, TIMETABLE] }`.
- **Order and prerequisites:** CLASSES_SECTIONS → SUBJECTS → SCHEDULES → CLASS_TEACHERS → TIMETABLE. A part whose prerequisite is neither selected nor already present in the target is refused in the preview with a clear message (for example "Timetable needs subjects and schedules").
- **Dry-run preview:** a read-only run of the same algorithm returning, per part, `wouldCreate`, `skippedExisting`, `skippedWithReason[]` (capped at 200 rows with the reason, e.g. "teacher no longer eligible", "section not offered"), and `blockers[]`. Preview and apply share the same code path; apply only adds the writes.
- **Skip-if-exists:** every row is matched on its natural key (`(year, class)`, `(year, section)`, `(year, class, subject)`, `(year, section, subject)` overrides, `(year, class, day)` schedule rows, current class teacher per section, `(year, section, day, period)` timetable slot). An existing target row is never modified; a second run therefore reports zero created and is a no-op. A later run with more parts selected only fills what is missing.
- **Never duplicates masters:** the copy only inserts per-year rows pointing at the existing master ids; it never inserts into `tb_classes`, `tb_sections`, `tb_subjects`, `tb_periods` or `tb_period_schedules`.
- **Never touches the source or history:** the source year is only read (`SELECT`), no UPDATE/DELETE statement mentions a source row; students, enrolments, fees, dues, attendance, homework, marks and report cards are **never** copied or changed. (Promoting students into next year's classes is a separate feature: Q8.)
- **Rules per part:** class teachers are copied as new rows with `effective_from` = target start date, only for teachers still eligible; the timetable copies only slots whose class/section are offered, whose subject is in the target's effective subjects, whose teacher is eligible and whose slot and teacher are free (conflicts become `skippedWithReason`).
- **Target rules:** must be a different, non-closed year of the same school; may already contain data (merge by skip-if-exists).
- **Locking and atomicity:** one transaction, `pg_advisory_xact_lock(hashtextextended('acad-setup:' || school_id, 0))`; the same lock is taken by **every** multi-row config save in this feature (offering save, subject-mapping bulk save, schedule save, copy), so two admins cannot interleave. A `tb_year_copy_log` row is written on success inside the transaction; on failure everything rolls back and a `FAILED` log row (no data) is written in a separate small transaction.

---

## D. Existing-data migration plan (APS, GHS, LAS)

Goal: **after the migration every user sees exactly what they saw before.** The old semantics were "every class/section/mapping/period applies to every year", so the backfill
creates per-year rows **for every existing year of every school** (all schools have exactly one year, so it is one-to-one), never merging or dropping anything.

| Step | Action | Counts that must hold (APS / GHS / LAS, from the scratch copy) |
|---|---|---|
| 1 | Helper `UNIQUE (school_id, id)` keys on `tb_classes`, `tb_sections`, `tb_subjects`, `tb_periods`; years: add `closed_*`, `uq_tb_ay_one_current`, no-overlap exclusion | 3 schools each 1 current year; pre-check raises if any school has 0 or >1 current years or overlapping Active years |
| 2 | `tb_academic_year_classes`: one row per (existing year, class), status copied from the class | 8 / 13 / 13 |
| 3 | `tb_academic_year_sections`: one row per (existing year, section), `max_strength` NULL (inherit), status copied | 8 / 15 / 15 |
| 4 | `tb_class_subjects`: add `academic_year_id`; each existing row is **copied once per existing year** of its school (then the year-free original is removed in the same step); rows stay Active/Inactive as they were | 4 / 109 / 109 (one year each) |
| 5 | `tb_section_subject_overrides`: empty | 0 |
| 6 | One default schedule `Regular` per school that has periods; every period gets `schedule_id`, `slot_type='PERIOD'`, `slot_order = period_number` | schedules 1 / 1 / 1; periods 8 / 6 / 6 with ids unchanged |
| 7 | `tb_academic_year_schedules`: empty (default schedule applies everywhere) | 0 |
| 8 | `tb_class_teacher_assignments`: `effective_from = GREATEST(year.start_date, created_at::date)`; Active rows `effective_to = NULL`; Inactive rows `effective_to = GREATEST(effective_from, updated_at::date)` | 1 / 15 / 15 rows, all current; the two "one current" rules agree |
| 9 | `tb_timetable_entries`: only the two new FKs are added (all rows already satisfy them) | 26 / 540 / 541 rows unchanged |
| 10 | `tb_year_copy_log`: created empty | 0 |

**What happens to existing rows of other modules: nothing.** Fee structures (3 / 13 / 13), student fee items (4 / 1172 / 1178), dues (12 / 3775 / 3996) and every payment/allocation/receipt are not read-modified; students and enrolments (2 / 176 / 177) are untouched; attendance (8 / 478 / 748), homework, class tests, report schemes/terms/marks and timetable rows keep their ids and values. (The migration is the only thing that must never run against the real DB without a `pg_dump` first; backup note in the file header.)

**Verification block (raises, and so rolls back everything, on any mismatch).** It snapshots counts and sums **before** the first change and compares them **after**: per school the counts of classes, sections, subjects, class-subject rows (expected = old rows × years), periods, timetable rows, class-teacher rows, enrolments, fee structures, items, dues (count, `SUM(amount_due)`, `SUM(amount_paid)`), payments and allocations, attendance, homework, assessments, report rows; every offering row has a real class/section; every old class-subject row has its copy; every Active class-teacher row has `effective_to IS NULL` and vice versa; every period has a schedule and exactly one default schedule exists per school with periods; no timetable row violates the new FKs; the one-current and no-overlap constraints exist and hold. It prints `VERIFIED` and a per-school summary table; a re-run prints `ALREADY MIGRATED: nothing changed`.

**Deploy order:** run the RUN-ME script **before** starting the API build that maps the new columns, and deploy the new build immediately after. Stages S1, S2, S5 and S7 are additive (new columns are nullable or have defaults, new tables are unused by the old build), so the old build keeps working against them. **S3 and S4 are not:** once `tb_class_subjects.academic_year_id` and `tb_periods.schedule_id` are NOT NULL, the old build can still *read* but cannot *save* a subject mapping or a period. Those two migrations therefore need the new API build in the same short maintenance window (the same arrangement as the fee effective-from migration). Take a `pg_dump` first; rolling back means restoring it.

---

## E. API and UI plan

### E.1 Roles (existing helpers, one new)
School Admin configures (`IsSchoolAdmin`); Principal views (new `CanViewAcademicSetup()` = School Admin + Principal, beside `CanViewReportSetup`); Teacher keeps only the existing "my" reads (`Timetable/my`, `Attendance/my-sections`, class-teacher `check`). Super Admin has no school and is refused. The school always comes from the token; no endpoint takes a school id.

### E.2 Endpoints (envelope `{status, statusCode, message, data}`; POST for actions because PATCH is blocked in dev)

| Endpoint | Roles | Purpose / DTO outline |
|---|---|---|
| `GET /api/AcademicYear` (extend) | Admin, Principal | add `state` (derived), `isClosed`, counts `{classes, sections, enrolments, structures}` |
| `POST /api/AcademicYear/{id}/close`, `/reopen` | Admin | body `{note}`; 409 for the current year |
| `GET /api/AcademicYear/{id}/summary` | Admin, Principal | dependants counts for the guard messages |
| `GET /api/AcademicSetup/years/{y}/classes` | Admin, Principal | classes offered with their sections `{classId, className, sections:[{sectionId, name, maxStrength, effectiveMax}]}` |
| `PUT /api/AcademicSetup/years/{y}/classes` | Admin | replace the offered set: `{classes:[{classId, sections:[{sectionId, maxStrength?}]}]}` under the advisory lock; refuses withdrawing in-use rows with counts |
| `GET/POST /api/Classes`, `/api/Sections` (existing) | Admin | masters; **new optional `?academicYearId=`** on reads returns the year's offering; default unchanged |
| `GET/PUT /api/ClassSubjects` (existing, bulk save) | Admin | **`academicYearId` added (optional; absent = current year, so today's page keeps working until the screen is updated)** |
| `GET/PUT /api/AcademicSetup/years/{y}/sections/{s}/subject-overrides` | Admin | `[{subjectId, action}]` |
| `GET /api/AcademicSetup/years/{y}/sections/{s}/subjects` | Admin, Principal | effective subjects (class mapping ± override) |
| `GET/POST/PUT /api/PeriodSchedule`, `POST .../{id}/toggle-status` | Admin (read: + Principal) | schedule master `{scheduleName, isDefault}` |
| `GET/POST/PUT /api/Period` (existing) | Admin | gains `scheduleId` (default = default schedule), `slotType`, `slotLabel` |
| `GET/PUT /api/AcademicSetup/years/{y}/schedules` | Admin | `[{classId?, dayOfWeek?, scheduleId}]`; `GET .../resolved?classId&day` |
| `POST /api/ClassTeacher/assign`, `/unassign` (existing) | Admin | body gains `effectiveFrom?` (default today, clamped into the year), `reason?` / `endReason?` |
| `GET /api/ClassTeacher/history` (existing) | Admin, Principal | now ordered by `effectiveFrom`, returns reasons and who |
| `POST /api/Timetable/assign`, `/unassign` (existing) | Admin | unchanged shape; stricter validation (C.10) |
| `POST /api/Timetable/validate` | Admin | same body as assign, returns `{ok, conflicts:[{code, message}]}`, writes nothing |
| `POST /api/AcademicSetup/years/{t}/copy-from/{s}/preview` | Admin | `{parts[]}` → per-part `{wouldCreate, skippedExisting, skipped:[{reason, ref}], blockers[]}` |
| `POST /api/AcademicSetup/years/{t}/copy-from/{s}/apply` | Admin | same body → same shape with `created`; writes the log |
| `GET /api/AcademicSetup/years/{t}/copy-log` | Admin, Principal | last runs |

### E.3 Angular (one "Academic Setup" area; reuse existing components)
- New sidebar group **Academic Setup** (School Admin; Principal read-only), routes in `ROUTE_ROLES` (the spec that fails on an unlisted page enforces this). Masters stay where they are under School Setup (Class, Section, Subject, Employee pages) and get a short hint linking to Academic Setup.
- **Academic Years**: the existing list gains a state chip (derived), Close / Reopen, and **"Copy from previous year"** (opens the wizard: pick source, tick parts, **Preview** table, then Apply).
- **Year Configuration**: a year picker at the top (a shared signal, default current year, past years read-only banner) and tabs that reuse the existing screens with an `academicYearId` input: Classes & Sections (new, built from `class-list` / `section-list` patterns), Subjects (the existing `class-subject-mapping` checklist plus an "exceptions for this section" drawer), Periods & Schedules (`period-list` plus a schedule selector and break rows), Class Teachers (the existing `section-list` dropdown plus a history drawer), Timetable (`timetable-admin`, same grid, `Validate` button).
- Interceptors/guards from the setup-gate work (`setupRefreshInterceptor`) get the new endpoints added to their watch list so the onboarding checklist refreshes.

### E.4 Mobile
Read-only and optional. No existing response shape changes, so the shipped app keeps working. Later, optionally show the academic year name on the student and teacher timetable screens from the existing response; nothing is required for this feature.

---

## F. Risks and assumptions

### F.1 Assumptions
1. The scratch copy mirrors the live DB's shape; the real counts are checked by the migration's own pre-check and verification, not by this document.
2. Today every school has exactly one year, so the "copy mapping per existing year" backfill is one-to-one; the rule is nevertheless written for several years.
3. `is_current` keeps its meaning everywhere (about 25 reads, plus billing).
4. Day-of-week stays 1 to 6 (Monday to Saturday), as today.
5. Time zone for "today" is India (`SchoolClock`), as everywhere else.
6. Past years are read-only only after they are explicitly **closed**; merely being in the past blocks nothing.

### F.2 What could break existing behaviour (and the mitigation)
| Risk | Mitigation |
|---|---|
| Six repositories read `tb_class_subjects` by class only (`AssessmentRepository`, `HomeworkRepository`, `ReportMarksRepository`, `ReportSetupRepository`, `TimetableRepository`, `ClassSubjectRepository`); once a second year has a different mapping they would read the wrong one | stage 3 adds the year filter to all six in the same change as the migration, with a test per repository using two years with different mappings |
| Dropping `UNIQUE (school_id, period_number)` and `UNIQUE (class_id, subject_id)`, and the new NOT NULL year/schedule columns | replaced by wider uniques in the same transaction; the old build can read but not save subject mappings or periods afterwards, so S3 and S4 deploy with the new API build (section D) |
| New exclusion constraints fail on dirty live data | pre-check section lists the offenders (year overlaps, period overlaps, overlapping class-teacher history) and raises before changing anything |
| Import Masters / Onboarding gate / dashboards counting masters | explicitly switched to the current-year offering in stage 2 with tests pinning identical answers after backfill |
| Cross-schedule teacher double-booking is not caught by the unique index | service validation now; DB exclusion optional (Q6) |
| Closing a year blocking something users still need | collection/void/read stay open; reopen exists; the block list is in C.2 and is testable |
| Subscription billing counts the current year's enrolments | unchanged semantics; documented so nobody "fixes" it |
| Large PUTs (offering, mapping) racing | one per-school advisory lock for all multi-row saves; optimistic 409 kept where it exists |
| `status_id` literal inside partial indexes differs per environment | resolved by name inside each script (existing convention) |

### F.3 Decisions I need from you (see the questions in the report)
Q1 section master shape; Q2 meaning of closing; Q3 teacher with several sections; Q4 year date rules; Q5 per-section subject overrides; Q6 several schedules and cross-schedule clash; Q7 Sunday; Q8 student promotion out of scope; Q9 legacy `ClassSubjects` default year; Q10 copy into a year that already has data; Q11 deploy order and backups.

---

## G. Stage plan

Each stage ships one RUN-ME script (`Docs/smss-acad-sN-<name>-RUN-ME.sql`: header with backup note, pre-check, additive DDL, backfill, verification block that raises, idempotent re-run message), backend, frontend, tests, and a regression run. Stages are independently deployable and each leaves the app behaving as before until its UI is used.

| Stage | Migration | Backend | Frontend | New tests |
|---|---|---|---|---|
| **S1 Years** | `s1-years`: `closed_*`, one-current index, no-overlap exclusion | derived state, close/reopen, date-edit and deactivate guards, summary; closed-year guard helper used by later stages | Academic Years list: state chip, Close/Reopen, counts | unit (state derivation, guards), DbFact (constraints reject overlap / second current; backfill keeps data), live smoke |
| **S2 Offerings** | `s2-offerings`: helper keys, `tb_academic_year_classes`, `tb_academic_year_sections`, backfill | offering GET/PUT, `?academicYearId` on Classes/Sections, Import Masters writes the offering, Onboarding gate and dashboards read the offering, enrolment/fee-structure rules | Year Configuration shell + Classes & Sections tab | before/after count equality, gate answer unchanged, tenant isolation, withdraw-with-data refused |
| **S3 Subjects** | `s3-subjects`: year on `tb_class_subjects`, overrides table, backfill | year on all six repositories, effective-subjects method, bulk save per (year, class), overrides | Subjects tab + section exceptions drawer | two-year fixtures proving each of the six repositories reads its own year; timetable/homework use effective subjects |
| **S4 Periods** | `s4-periods`: schedules, slot type/order, overlap exclusion, year schedule assignments | schedule CRUD, period changes, resolution, timetable break/schedule validation | Periods & Schedules tab | resolution precedence matrix, overlap rejected by DB and service, break not schedulable |
| **S5 Class teachers** | `s5-class-teachers`: effective dates, reasons, current-index, history exclusion | assign/unassign with dates and reasons, history, multi-section confirm | history drawer, Class Teachers tab | reassign same day valid, overlap rejected, one-current agrees with status, attendance/leave "is class teacher" unchanged |
| **S6 Timetable hardening** | none, or an optional `s6-tt-times` (denormalised times + exclusion) if Q6 says yes | `validate` endpoint, closed-year block, cross-schedule overlap | Validate button, conflict panel | clash matrix (teacher, section, break, closed, unmapped, inactive, wrong schedule) |
| **S7 Year transition** | `s7-copy-log`: `tb_year_copy_log` | preview/apply, per-school lock, log, idempotency | wizard on Academic Years | idempotent re-run, partial parts then more parts, never modifies source (row hash before/after), conflicts become skips, rollback on forced failure |
| **S8 Hardening and docs** | `s8-fks` (optional composite FKs from enrolments and fee structures to the offering, only after an orphan report is empty) | remove deprecated default-year behaviour if agreed, docs | mobile docs note | orphan report, final full regression |

**Regression covered at the end of every stage** (a stage is not done until all pass): login and first-login password change; onboarding gate and the setup checklist; School Setup screens; Import Masters; fee types, fee structures, student fee items and dues, **Collect Payment, void, receipts**; student admission (Confirm) and existing-student entry and student import; attendance mark/correct/report; exams and report cards (setup, marks, results, PDF); homework; class tests; notices; all role dashboards (School Admin, Principal, Teacher, Student, Accountant, Admission); ID cards; transport (areas, vehicles, drivers, student transport, live duty); employees and class-teacher flows; leave; calendar; subscription billing counts.

**How it is run end to end (no real DB, no real credentials):**
1. Scratch PostgreSQL on a spare port (port 5432 is refused by the test kit): restore the latest dump into a template, apply every RUN-ME script **including the earlier ones the current template lacks** (partner schools, enquiries) plus the new stage script; snapshot per-school counts first.
2. Unit tests, and the `[DbFact]` tests against a per-test database cloned from the template. Compare the set of failing test **names** with a baseline run of the previous commit (a worktree), so only new failures count.
3. Publish the API to a scratch folder (never over the Visual Studio copy) and run it on a spare port against a scratch database with the real SMTP host overridden; serve the web app from a clean worktree pointed at that API.
4. Drive the app with Playwright (Edge) using throwaway users created by SQL with a bcrypt hash of a known throwaway password: Super Admin, a fresh School Admin (full onboarding), an existing school (GHS), Teacher, Student, Accountant, Principal. Scripts walk the regression list above, screenshot each stage at 1440 and 390, and compare every API figure with independent SQL (the same cross-check pattern used for the School Admin dashboard).
5. Run the migration twice (second run must print `ALREADY MIGRATED`), run the verification block, and diff the before/after count snapshot.
6. Delete throwaway rows and drop the scratch databases; stop the processes; report counts and screenshots.

---

## H. Out of scope (confirm)
Student promotion into the next year's classes, roll-number regeneration, fee-structure copying (fee setup already has its own per-year structures; a "copy fee structures" part can be added to the transition later), subject teachers beyond the timetable, a 7-day week, and any mobile write screens.

---

## I. Addendum (decisions after review): demo-only data, and several timetables per section

### I.1 Changes to the earlier plan
- **The software is not live and holds only demo schools (APS, GHS, LAS).** The expand-then-contract and "old build keeps working" rules in sections D, F and G are dropped:
  a migration may add `NOT NULL` columns and the final constraints directly, after backfilling the demo schools. Every migration is still a RUN-ME file in `Docs/`:
  one transaction, no psql meta-commands, a pre-check that lists offenders and raises before anything changes, a header with backup and rollback notes, and a verification
  block that compares before/after counts and raises (rolling everything back) on a mismatch. A second run prints `ALREADY MIGRATED`.
- Decisions taken: Q1 section = class + section name, capacity per year on the offering. Q2 closing a year blocks configuration, attendance, enrolments, homework and
  timetable edits, allows fee payments, voids, receipts and reads; a School Admin can reopen; the current year can never be closed. Q3 a teacher may be class teacher of
  several sections (with a confirm). Q4 no overlap among Active years only; no ordering rule. Q5 section-level add/remove subject overrides stay (later stage). Q6 several
  timetables per section (below), several schedules per class and per day, no database teacher clock-time exclusion yet. Q7 Monday to Saturday. Q8 promotion, roll
  numbers and fee-structure copy are out of scope. Q9 a caller that sends no year gets the current year (with a log line). Q10 year copy merges by skip-if-exists, with a preview.

### I.2 Several timetables per section (built in the timetable stage; S1 and S2 are compatible with it)

**Model.** A new master-of-versions table, `tb_timetables`: one row per timetable of a **section offering in a year**.

| Column | Notes |
|---|---|
| `timetable_id` BIGINT identity PK | |
| `school_id`, `academic_year_id`, `class_id`, `section_id` | composite FK `(academic_year_id, section_id) -> tb_academic_year_sections` (S2) and `(section_id, class_id) -> tb_sections`, so a timetable can exist only for a section that is offered in that year |
| `timetable_name` VARCHAR(80) NN | `UNIQUE (academic_year_id, section_id, timetable_name)` |
| `schedule_id` BIGINT NN | the bell schedule (S4) its entries use |
| `state` VARCHAR(8) NN | `CHECK IN ('DRAFT','ACTIVE','ARCHIVED')` |
| `is_active` BOOLEAN NN | `CHECK (is_active = (state = 'ACTIVE'))`; kept as a real column only so entries can reference it (see below) |
| `ever_active` BOOLEAN NN DEFAULT false | set true at the first activation, never reset; `CHECK (NOT is_active OR ever_active)` |
| `first_activated_at`, `activated_by`, `deactivated_at` | audit of the history |
| audit columns | as everywhere |

- **Exactly one ACTIVE timetable per section per year:** `CREATE UNIQUE INDEX uq_tb_tt_one_active ON tb_timetables (academic_year_id, section_id) WHERE is_active`.
- **Activate** = one transaction under the per-school advisory lock: the old active one is set `ARCHIVED` (`is_active=false`), the new one `ACTIVE` (`is_active=true`, `ever_active=true`).
  The previous timetable stays as history. **Delete** is allowed only for a timetable with `ever_active = false` (a draft that never went live); anything that was ever
  active is archived, never deleted. Enforced by the service and by `ON DELETE RESTRICT` from a history table; the verification block asserts it.
- **Drafts never block anything.** Only ACTIVE timetables take part in clash checks.

**How teacher double-booking stays impossible in the database among active timetables.** A unique index cannot look at another table, and a partial index cannot say
"where the owning timetable is active". So the entry carries a mirror of the flag, and the database itself keeps the mirror honest:

1. `tb_timetables` gets `UNIQUE (timetable_id, is_active)`.
2. `tb_timetable_entries` gets `timetable_id BIGINT NOT NULL` and `tt_is_active BOOLEAN NOT NULL`, with
   `FOREIGN KEY (timetable_id, tt_is_active) REFERENCES tb_timetables (timetable_id, is_active) ON UPDATE CASCADE`.
   An entry therefore **cannot** carry a flag different from its timetable's, and when activation flips `tb_timetables.is_active` the database cascades the flip to every
   entry of that timetable in the same statement. Application code never writes `tt_is_active`.
3. Teacher uniqueness: `CREATE UNIQUE INDEX uq_tb_tte_teacher_active ON tb_timetable_entries (school_id, academic_year_id, employee_id, day_of_week, period_id) WHERE tt_is_active AND status_id = <Active> AND employee_id IS NOT NULL`.
   Two ACTIVE timetables cannot both place the same teacher in the same day and period, whatever the section; drafts and archived ones are outside the index.
4. Section slot uniqueness is **per timetable**: `UNIQUE (timetable_id, day_of_week, period_id) WHERE status_id = <Active>`. Plus the one-active-per-section index above, so a
   section has one slot occupant at a time in the live view.
5. Activating a timetable whose teacher is already busy in another active timetable makes the cascade fail on the unique index; the service checks first and answers with a
   readable 409 listing the conflicts, and the index is the backstop for a race (the lock makes a race impossible within one school anyway).

Why this and not triggers or a view: it needs no trigger code, the foreign key makes the mirror impossible to forge, the cascade makes activation one `UPDATE`, and the
index is the same shape as the one that exists today (`uq_tb_tte_teacher_one_active_per_period`), so the existing 540 and 541 demo rows migrate with a mechanical change.
Cross-schedule clock-time overlap stays a service check (Q6).

**Teacher on a slot is optional.** `employee_id` becomes nullable and a slot with no teacher is allowed; entries copied without a teacher carry `needs_teacher = true`. The
activation answer includes the warning count of slots without a teacher. Index 3 ignores null teachers.

**"Apply this timetable to other sections of the class".** For each chosen section (offered in the same year, same class) a **new independent timetable** is created
(name from the request, status DRAFT), with the same day, period and subject for every slot. The teacher is copied only when that teacher is free (no ACTIVE slot of theirs
at that day and period); otherwise the slot is created with no teacher and `needs_teacher = true`. Never overwrites an existing timetable (a name clash on the target section
returns a skip with a reason). One transaction per request under the advisory lock; returns per section `{created, slotsCopied, slotsNeedingTeacher}`.

**Readers.** The teacher dashboard, `Timetable/my`, `Timetable/student/my`, homework and assessment "does this teacher teach this section and subject" checks, mobile and
the student/parent view read **only** `tt_is_active` entries. They keep their response shapes. Editing an active timetable re-runs the teacher clash check for the touched slot.

**Backfill (what existing rows become).** For every (year, section) that has at least one existing timetable entry (Active or Inactive history), one timetable is created:
name `Timetable <year name>`, schedule = the school's default schedule, `state = ACTIVE`, `ever_active = true`; **all** existing entries of that section and year (including
their Inactive history rows) are attached to it, with `tt_is_active = true`. Sections with no entries get no timetable. Counts the verification block checks: timetables =
distinct (year, section) of existing entries (APS 3 sections / GHS 15 / LAS 15 in the scratch copy), and every entry has a timetable; entry counts are unchanged
(26 / 540 / 541).

**Compatibility with S1 and S2.** S2's `tb_academic_year_sections (academic_year_id, section_id)` unique key is exactly what `tb_timetables` references; years are never deleted;
closing a year (S1) blocks timetable create/edit/activate through the same guard helper. Nothing in S1/S2 names a column the timetable work would have to rename.
