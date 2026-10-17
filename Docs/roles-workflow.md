# SMSS — Complete Role & Workflow (baseline reference)

This is the business/role workflow baseline for SMSS. Every database table, API, permission, Angular menu, dashboard, and screen should map back to one of these responsibilities rather than being designed independently.

## 1. Overall hierarchy

```
                         SMSS SaaS
                            │
                     ┌──────┴──────┐
                     │             │
                 SUPER ADMIN   System Config
                     │
             ┌───────┴────────┐
             │                │
         SCHOOL A          SCHOOL B
             │                │
        School Admin     School Admin
             │
       ┌─────┼─────────┬──────────┐
       │     │         │          │
 Principal Teachers Accountant Admission
       │     │         │          │
       └─────┴─────────┴──────────┘
                    │
             Students / Guardians
```

## 2. Main Roles

| Role | Scope |
|---|---|
| Super Admin | Entire SaaS |
| School Admin | One school |
| Principal | One school — academic + administrative |
| Teacher | One school — teaching/academic |
| Accountant | One school — finance |
| Admission Staff | One school — admissions |
| HR/Employee Admin | One school — employees |
| Librarian | One school — library |
| Student | Own student account |
| Guardian/Parent | Linked student(s) |

Some of these can initially be implemented as permissions under SCHOOL_ADMIN or another role and split into dedicated roles later.

## 3. Super Admin workflow

The Super Admin manages the SaaS platform, not the day-to-day operation of an individual school.

```
Super Admin Login
       ↓
Super Admin Dashboard
       ↓
Manage Schools
       ├── Register School
       ├── View Schools
       ├── Edit School
       ├── Activate School
       ├── Deactivate School
       ├── Suspend School
       └── View School Details
```

### School registration

```
Super Admin
    ↓
Register School
    ↓
Enter School Information
    │
    ├── School Name
    ├── Short Name
    ├── School Code
    ├── School Type
    ├── School Level
    ├── Board
    ├── Establishment Year
    ├── PAN
    ├── GSTIN
    ├── Address
    ├── Country
    ├── State
    ├── District
    ├── City
    ├── Email
    ├── Mobile
    └── Website
    ↓
Select Subscription Plan
    ↓
Create School
    ↓
Generate school_id
    ↓
Create School Admin Login
    ↓
Generate Temporary Password
    ↓
Hash Password
    ↓
Create tb_users
    ↓
Assign SCHOOL_ADMIN Role
    ↓
School Registration Completed
```

Example: `school_id = sch2026001`. The Super Admin can then see: ABC Public School, School ID: sch2026001, Status: Active, Plan: Professional.

## 4. Super Admin — School Management

```
Schools
│
├── Active
├── Inactive
├── Suspended
├── Trial
├── Subscription Expired
└── Payment Due
```

Super Admin can: view school profile, edit school information, view subscription, change subscription plan, extend subscription, suspend/activate/deactivate school, reset School Admin credentials, view school usage (user/student/employee count), view subscription/payment status.

Super Admin should not normally perform daily school operations such as entering student attendance or marks.

## 5. School Admin workflow

School Admin is the primary operational administrator of a school.

```
School Admin Login
        ↓
School Dashboard
        ↓
School Setup
```

School Admin manages: School Profile, Academic Year, Classes, Sections, Subjects, Departments, Designations, Employees, Teachers, Students, Guardians, Admissions, Fees, Attendance, Timetable, Examinations, Homework, Notices, Calendar, Documents, Reports. Exact permission level can later be restricted depending on school configuration.

## 6. School initial setup

```
School Admin
      ↓
First Login
      ↓
Change Temporary Password
      ↓
School Setup Wizard
```

Recommended setup sequence: School Profile → Academic Year → Classes → Sections → Subjects → Departments → Designations → Employees → Teachers → **Fee Setup (Fee Types → Fee Structures → Fee Concessions)** → Students → Guardians → Examination Configuration → Timetable.

Fee Setup is placed before Students because Student Admission assigns a student to an existing Fee Structure — the structure must exist first. It only depends on Academic Year and Classes, both already earlier in this sequence, so it does not block on anything school-specific that hasn't been built yet. See section 27 for the full Fee Setup schema, and section 28 for Student Fee Billing & Collection (the actual per-student items, installments, payments, dashboard, and reports — this is now complete end to end).

## 7. Principal workflow

Dashboard shows: Students, Teachers, Attendance, Admissions, Examinations, Academic Performance, Homework, Leave, Timetable, Notices, School Calendar.

**Academic:** manage academic year, monitor classes/subjects/teacher assignments, review student performance/examinations/report cards, monitor attendance/homework/timetable.
**Staff:** view employees, review teacher attendance, approve teacher leave, monitor teacher workload, assign teachers where permitted.
**Students:** view students, monitor attendance, review academic performance and disciplinary/student notes, approve certain student actions.
**Administration:** notices, school calendar, events, announcements, reports. Principal also has read-only access to the Fee Dashboard and class-wise fee report (section 28) — numbers only, no collection/void actions and no per-student Fees tab link.

## 8. Teacher workflow

Dashboard shows: My Classes, My Subjects, Today's Timetable, Attendance, Homework, Examinations, Marks, Students, Leave, Notices.

**Classes:** view assigned classes/sections/subjects/students.

**Attendance:** `Select Class → Select Section → Select Date → Mark Attendance → Save`

**Homework:** `Create Homework → Select Class → Select Section → Select Subject → Set Due Date → Publish`

**Examination:** view assigned examinations/schedule, enter marks, update marks before lock, view student performance, add remarks.

**Leave:** `Apply Leave → Principal / Authorized Admin → Approve / Reject`

## 9. Accountant workflow

Dashboard modules:
```
Fees
├── Fee Structure
├── Student Fees
├── Fee Collection
├── Receipts
├── Pending Fees
├── Discounts
├── Concessions
└── Reports

Finance
├── Income
├── Expenses
├── Transactions
├── Ledger
├── Bank
└── Reports
```

**Fee collection workflow:** `Student → Fee Due → Accountant → Collect Payment → Record Transaction → Generate Receipt → Update Student Fee Balance`

See section 28 for the full schema and screens behind this workflow (`tb_fee_payments`, `tb_fee_payment_allocations`, `tb_student_fee_dues`, Fee Dashboard, class-wise report) — this module is now complete.

**Known gap to fix:** the student record route (`/students/:id`) is currently School Admin-only in the Angular route rules, which blocks the Accountant from opening a student's Fees tab even though Accountant is the role that collects and voids payments there. Decided fix: give Accountant access to the student record scoped to the Fees tab (not full profile edit rights elsewhere on that page).

Accountant should not be able to modify academic marks, attendance, or student academic records unless explicitly authorized.

## 10. Admission Staff workflow

```
Enquiry → Application → Document Collection → Application Review → Admission Decision
→ Admission Approval → Student Registration → Guardian Registration
→ Class/Section Allocation → Fee Setup → Student Account Creation
```

After admission: `Student → tb_students → Enrollment → Class / Section`

## 11. HR / Employee Admin workflow

```
Employee Management
│
├── Employee Registration
├── Employee Profile
├── Department
├── Designation
├── Joining Details
├── Documents
├── Employment Status
├── Attendance
├── Leave
└── Salary/Payroll
```

HR/Admin can manage employee information. Principal can view/manage relevant staff information according to permissions.

## 12. Librarian workflow (if Library Management enabled)

Manage books, authors, publishers, categories, book copies; issue/return/renew books; track overdue books; manage fines; view student/teacher borrowing history; generate reports.

## 13. Student workflow

Self-service portal. Student can view: My Profile, My Attendance, My Timetable, My Subjects, My Homework, My Exams, My Marks, My Report Cards, My Fees, My Notices, My Calendar, My Documents. Student generally cannot modify academic records.

## 14. Guardian/Parent workflow

```
Guardian Login → Guardian Dashboard → Select Child
```

If a guardian has two children, they switch between Child 1 / Child 2. Guardian can view: student profile, attendance, homework, examination schedule, marks, report cards, fees, fee receipts, notices, school calendar, teacher communication where enabled.

## 15. Attendance workflow

**Teacher:** `My Class → Date → Mark Attendance → Submit`
**Principal/Admin:** view attendance class-wise, student-wise, date-wise, monthly.
**Guardian:** view child attendance.

## 16. Examination workflow

```
School Admin / Principal → Create Examination → Create Exam Schedule → Assign Subjects
→ Assign Teachers → Publish Schedule → Teacher → Enter Marks → Review / Lock
→ Generate Result → Generate Report Card → Student / Guardian → View Result
```

## 17. Homework workflow

```
Teacher → Create Homework → Select Class / Section / Subject → Publish
→ Student → View Homework → Submit Homework → Teacher → Review Submission → Feedback / Marks
```

## 18. Leave workflow

**Teacher/Employee:** `Apply Leave → Principal / Authorized Admin → Review → Approve / Reject`
**Student:** `Student/Guardian → Leave Request → Class Teacher / Authorized Staff → Approve / Reject`

## 19. Timetable workflow

```
School Admin / Principal → Configure (Classes, Sections, Subjects, Teachers, Periods)
→ Create Timetable → Publish → Teacher / Student → View Timetable
```

## 20. Notice / Communication workflow

Authorized users: Super Admin, School Admin, Principal — can publish notices according to their scope.

```
Principal → Create Notice → Target (All Students / Teachers / Parents / Class 8 / Specific Section) → Publish → Notification
```

## 21. School Calendar

School Admin/Principal can manage: Academic Events, Holidays, Exams, Parent Meetings, School Events, Activities, Important Dates. Users view events according to their permissions.

## 22. Student Transfer / SLC / TC

```
Student → Request Transfer / Leaving → Verify Fees → Verify Documents
→ Academic Verification → Generate SLC / TC → Student Status Updated
```
Authorized by School Admin/Principal.

## 23. Finance workflow

```
School → Fee Structure → Student Fee Assignment → Fee Due → Payment → Receipt
→ Transaction → Ledger → Financial Reports
```

Other income: `Other Income → Transaction → Ledger`
Expenses: `Expense → Approval → Payment → Transaction → Ledger`

See section 28 for the full Fee Due / Payment / Receipt / Dashboard / Reports schema — this module is complete. Ledger/Other Income/Expenses are a separate future module, not yet designed. Refunds of actual money paid out are also part of that future module — voiding a payment (section 28) only reverses the fee-due bookkeeping, it does not track or move cash.

## 24. Role responsibility summary

| Module | Super Admin | School Admin | Principal | Teacher | Accountant | Admission | Student | Guardian |
|---|---|---|---|---|---|---|---|---|
| SaaS Management | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| School Registration | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| School Profile | Manage | Manage | View/Manage* | View | View | View | View | View |
| Academic Setup | ❌ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Employee Management | ❌ | ✅ | Manage/View | View | ❌ | ❌ | ❌ | ❌ |
| Student Management | ❌ | ✅ | ✅ | Assigned Students | View | ✅ | Own | Child |
| Admissions | ❌ | ✅ | Approve/View | ❌ | Fee-related | ✅ | ❌ | ❌ |
| Attendance | ❌ | View/Manage | Monitor | Mark | ❌ | ❌ | View Own | View Child |
| Examinations | ❌ | Manage | Manage | Enter Marks | ❌ | ❌ | View | View |
| Homework | ❌ | Monitor | Monitor | Create | ❌ | ❌ | Submit | View |
| Timetable | ❌ | Manage | Manage | View | ❌ | ❌ | View | View |
| Leave | ❌ | Manage | Approve | Apply | Apply | Apply | Request | Request |
| Fees | Subscription | Manage | View | ❌ | Manage | View | View | Pay/View |
| Finance | SaaS Billing | Manage | View | ❌ | Manage | ❌ | ❌ | ❌ |
| Library | ❌ | Manage | View | Borrow | ❌ | ❌ | Borrow | View |
| Notices | SaaS | Manage | Publish | View | View | View | View | View |
| Reports | SaaS | School | Academic | Own Classes | Finance | Admission | Own | Child |

\* Exact permissions should be configurable through RBAC.

## 25. Most important RBAC principle

Do **not** implement permissions as `if role == Principal` throughout the code. Instead:

```
User → Role → Permissions → Resource / Action
```

Example:
```
PRINCIPAL
    ↓
STUDENT_VIEW, STUDENT_EDIT, ATTENDANCE_VIEW, EXAM_VIEW, EXAM_MANAGE,
REPORT_CARD_VIEW, TEACHER_VIEW, LEAVE_APPROVE, NOTICE_CREATE

TEACHER
    ↓
STUDENT_VIEW_ASSIGNED, ATTENDANCE_MARK, HOMEWORK_CREATE,
HOMEWORK_REVIEW, EXAM_MARKS_ENTER, LEAVE_APPLY
```

This is far more flexible than hard-coding permissions directly into roles.

**Known current gap:** there is no real permission system yet — role checks in code currently test role **names** directly rather than resolving permissions. This is an accepted stopgap, consistent project-wide, to be replaced once `lut_roles`/`tb_user_roles` grows real permission mapping. Do not treat each new instance of this pattern as a fresh decision — it matches existing practice.

The Fees module (section 28) is the first place a view-vs-manage distinction was needed, and it was done as two small role-name helpers rather than a generic framework:
- `CanManageFees()` — School Admin, Accountant. Used by payment recording and voiding.
- `CanViewFees()` — School Admin, Accountant, Principal. Used by the Fee Dashboard and class-wise report; responses also carry a `canManage` flag so the frontend can hide manage-only actions (class-report button, per-student Fees link, CSV export) for a view-only caller (Principal).

This pair is the template to reuse if another module needs the same read/write split, rather than inventing a new helper per module.

## 26. Final high-level workflow

```
                    SUPER ADMIN
                         │
                         ↓
                  Register School
                         │
                         ↓
                  Create School
                         │
                         ↓
                Create School Admin
                         │
                         ↓
                 SCHOOL ADMIN LOGIN
                         │
          ┌──────────────┼──────────────┐
          ↓              ↓              ↓
      School Setup   User Setup    Academic Setup
          │              │              │
          └──────────────┼──────────────┘
                         ↓
                    PRINCIPAL
                         │
          ┌──────────────┼──────────────┐
          ↓              ↓              ↓
       Teachers       Students       Operations
          │              │              │
          ↓              ↓              ↓
     Attendance      Admissions      Timetable
     Homework        Guardians       Exams
     Marks            Fees           Notices
          │              │              │
          └──────────────┼──────────────┘
                         ↓
                 STUDENT / GUARDIAN
                         │
                         ↓
              Self-Service Portal
```

## 27. Fee Setup module (school-owned, per class / per academic year)

Mirrors School Setup: a sequence of fee masters the School Admin configures once per school, then reuses when students are admitted. All tables are school-owned (carry `school_id`, not global `lut_` masters) because fee types, amounts, and concessions vary school to school.

Setup sequence: **Fee Types → Fee Structures (per Class + Academic Year) → Fee Concessions/Discounts**. Depends only on Academic Year and Class masters — does not depend on the Student module, so it was built and tested before Students/Admissions existed. Status: **complete** (Fee Types, Fee Structures, Fee Concessions all built and tested, backend + Angular UI).

### `tb_fee_types` (school-owned master)
- `fee_type_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `school_id` VARCHAR(100) FK → tb_schools
- `fee_type_name` VARCHAR(100)
- `fee_type_code` VARCHAR(30)
- `description` TEXT
- `is_optional` BOOLEAN default false — marks fee heads that can be individually included/excluded per student at admission (e.g. Transport, Hostel). Non-optional heads (e.g. Tuition, Admission Fee) are always applied and don't show an include/exclude toggle on the admission form. Enforced server-side too: a request trying to switch off a non-optional head at admission is ignored, and `PUT fee-items` on a non-optional head returns 400.
- `isactive` BOOLEAN
- `created_at` / `updated_at` TIMESTAMPTZ
- Unique: `(school_id, fee_type_code)`

Each school seeds/edits its own fee types (Tuition, Admission, Annual, Exam, Transport, Library, Computer, Sports, Hostel, Other, etc.) — not a shared global lookup, since schools name and structure these differently.

### `lut_fee_frequency` (global lookup, small/fixed list)
- `frequency_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `frequency_name` VARCHAR(50) — One Time, Monthly, Quarterly, Half-Yearly, Annual
- `frequency_code` VARCHAR(30)
- `isactive` BOOLEAN

### `tb_fee_structures` (header — one per Class per Academic Year per School)
- `fee_structure_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `school_id` VARCHAR(100) FK → tb_schools
- `academic_year_id` BIGINT FK → tb_academic_years
- `class_id` BIGINT FK → tb_classes
- `structure_name` VARCHAR(150)
- `isactive` BOOLEAN
- `created_at` / `updated_at`
- Unique: `(school_id, academic_year_id, class_id)`

### `tb_fee_structure_details` (line items within a structure)
- `fee_structure_detail_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `fee_structure_id` BIGINT FK → tb_fee_structures (cascade delete)
- `fee_type_id` BIGINT FK → tb_fee_types
- `frequency_id` BIGINT FK → lut_fee_frequency
- `amount` NUMERIC(12,2)
- `due_day` SMALLINT — default day-of-month this fee head is due for recurring frequencies (e.g. 10 = "10th of every month"); built as an input per row in the Fee Structures builder, enabled/required only for Monthly/Quarterly/Half-Yearly rows. See section 28 for how this drives due-date generation and what happens when it changes. A legacy recurring line with no due_day set falls back to the 1st of the month.
- `created_at` / `updated_at`
- Unique: `(fee_structure_id, fee_type_id)`

### `tb_fee_concessions` (school-owned discount/concession master)
- `concession_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `school_id` VARCHAR(100) FK → tb_schools
- `concession_name` VARCHAR(100) — e.g. Sibling Discount, Staff Ward, Merit Scholarship, EWS
- `concession_code` VARCHAR(30)
- `concession_type` VARCHAR(20) — `PERCENTAGE` or `FIXED`
- `value` NUMERIC(10,2)
- `applicable_fee_type_id` BIGINT NULL FK → tb_fee_types (NULL = applies to the whole structure, not just one fee head)
- `isactive` BOOLEAN
- `created_at` / `updated_at`

### Student-level linkage (built, see section 28 for the full billing/payment layer on top)
- `tb_student_fee_structure` — links a student to a fee structure: `student_id`, `fee_structure_id`, `school_id`, `academic_year_id`. Built and working; auto-assigned at admission based on class + academic year, with manual assignment/reassignment as fallback.
- `tb_student_fee_concessions` — per-student concessions applied (many-to-many): `student_id`, `concession_id`, `approved_by`, `remarks`. Built and working.

**Note on `student_id` type:** `tb_students.student_id` is `VARCHAR(20)` (a business-generated identifier, same pattern as `tb_schools.school_id`), not `BIGINT` as originally assumed when this section was first drafted. Every FK to `tb_students` below in section 28 uses `VARCHAR(20)` — corrected to match the real schema.

## 28. Student Fee Billing & Collection (per-student items, installments, payments, dashboard, reports) — MODULE COMPLETE

This is the layer that turns a class-level Fee Structure into actual per-student amounts owed, due dates, recorded payments, and the dashboard/reports built on top — what section 9 (Accountant workflow) and section 23 (Finance workflow) actually run on. `tb_student_fee_structure` (section 27) only says which structure governs a student; it does not say what frequency the student chose for each fee head, whether an optional fee applies to them, what's actually been paid, or what's overdue. These tables add that.

Requirements this satisfies: fee configuration at admission (per-student frequency choice and optional-fee opt-in/out), fee status in the student list (Paid/Partially Paid/Pending/Overdue/Not Set with amounts, filterable), configurable monthly due dates per fee head, automatic overdue detection that recalculates cleanly when a due date changes, and a fee dashboard with reports. Payment history is authoritative — status is always derived from recorded `tb_fee_payments`/`tb_fee_payment_allocations` rows, never a manually settable flag.

### `tb_student_fee_items` (per-student resolved billing lines)
- `student_fee_item_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `student_id` VARCHAR(20) FK → tb_students
- `school_id` VARCHAR(100) FK → tb_schools
- `academic_year_id` BIGINT FK → tb_academic_years
- `fee_type_id` BIGINT FK → tb_fee_types
- `frequency_id` BIGINT FK → lut_fee_frequency — the frequency **this student** chose for this fee head (e.g. a parent may choose to pay tuition quarterly even though another student in the same class pays monthly); seeded from the fee structure's default but independently stored and editable per student. Allowed frequencies: One Time/Annual lines stay fixed; Monthly/Quarterly/Half-Yearly lines can be paid Monthly, Quarterly, Half-Yearly, or Annual (student's choice).
- `base_amount` NUMERIC(12,2) — the **per-period** amount from the fee structure (e.g. ₹2,500/month). The year total for an item is `net_amount × periods-per-year-at-the-structure's-default-frequency`, then re-split across whatever frequency the student actually chose — so a student paying quarterly pays the full proportional share (e.g. ₹7,500/quarter for a ₹2,500/month line), never a fraction of the annual total.
- `concession_amount` NUMERIC(12,2) default 0 — resolved from any applicable `tb_student_fee_concessions`
- `net_amount` NUMERIC(12,2) — `base_amount - concession_amount`, the resolved per-period billed amount
- `is_applicable` BOOLEAN — lets an optional fee head (`tb_fee_types.is_optional = true`) be toggled on/off per student without deleting the row or losing history. Included on the admission form only for fee types marked optional; **defaults to unticked (opt-in)**, including when an API caller omits it, so a forgotten tick never silently bills an unwanted fee.
- `isactive` BOOLEAN
- `created_at` / `updated_at`
- Unique: `(student_id, academic_year_id, fee_type_id)`

Seeded from `tb_fee_structure_details` for the student's class at admission time (one row per applicable fee head), with frequency and applicability editable per student afterward from the Fees tab. Re-synced automatically whenever the underlying fee structure, a concession, or the item itself changes.

### `tb_student_fee_dues` (generated installments)
- `fee_due_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `student_fee_item_id` BIGINT FK → tb_student_fee_items (cascade delete)
- `student_id` VARCHAR(20) FK → tb_students (denormalized for fast list/dashboard queries without a join)
- `school_id` VARCHAR(100) FK → tb_schools
- `due_date` DATE
- `amount_due` NUMERIC(12,2)
- `amount_paid` NUMERIC(12,2) default 0 — a cached running total, kept in sync transactionally whenever a `tb_fee_payment_allocations` row is written against this due; never edited directly
- `created_at` / `updated_at`
- Unique: `(student_fee_item_id, due_date)`

Generated when a `tb_student_fee_items` row is created/activated: a Monthly item gets 12 due rows across the academic year (`due_date` = each month's `due_day` from the source `tb_fee_structure_details`), Quarterly gets 4, Half-Yearly gets 2, Annual/One Time gets 1.

**Mid-year admissions:** the floor for the first generated due is the **first due date on or after the student's admission date** (not the start of the admission month) — so a student admitted Oct 15 with a due day of the 1st gets their first installment due Nov 1, not an Oct 1 installment that's already overdue on day one of enrollment. A student admitted before a due date in their admission month still gets that month's installment. One Time/Annual charges are dated from the admission day instead of the academic year start, so they're never overdue for a period before the student joined.

**Gap case:** it's possible every remaining configured due date in the academic year falls before the admission date (most commonly for Quarterly/Half-Yearly, e.g. a Half-Yearly fee due Apr 1 and Oct 1, student admitted Oct 15 — both dates have passed; the same rule applies uniformly to Monthly too if ever admitted after the last due date in the year). In that case, generate exactly **one** installment dated at the admission date, for the normal per-period amount (no proration) — the student still owes the fee, there just wasn't a remaining scheduled date for it. **Built, tested, and confirmed.**

**Status is never a stored column** — always computed at read time:
- `Paid`: `amount_paid >= amount_due`
- `Partially Paid`: `0 < amount_paid < amount_due`, and `due_date` is still in the future
- `Overdue`: `amount_paid < amount_due` and `due_date` has passed (this wins over Partially Paid — a partly-paid installment past its due date is Overdue, since that's the state needing action)
- `Pending`: `amount_paid = 0` and `due_date` is in the future

This is what makes "recalculate overdue status without incorrectly marking students overdue" automatic when a due date changes — there's no stored flag to go stale.

**Due date changes:** if the School Admin updates `due_day` on `tb_fee_structure_details` (e.g. extends the monthly deadline from the 10th to the 15th), only **unpaid** (`amount_paid = 0`) future `tb_student_fee_dues.due_date` values are regenerated. Any due with a payment already allocated keeps its original `due_date` — payment history is never rewritten. A concession applied to an in-progress schedule behaves the same way: installments already due keep their amount, and the full-year reduction comes off the future ones.

### `tb_fee_payments` (payment transactions — the only thing that marks something paid)
- `payment_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `student_id` VARCHAR(20) FK → tb_students
- `school_id` VARCHAR(100) FK → tb_schools
- `payment_date` TIMESTAMPTZ
- `amount` NUMERIC(12,2) — total of this transaction, may cover multiple dues at once
- `payment_mode` VARCHAR(30) — Cash, Cheque, UPI, Card, Bank Transfer, Online
- `receipt_number` VARCHAR(50) — school-scoped, sequential, human-readable (e.g. `RCP-2026-0001`, per school per year). Allocated under a per-school database lock so concurrent payments queue instead of colliding (an earlier retry-only approach could exhaust its 3 tries and 500 under load — fixed).
- `collected_by` BIGINT FK → tb_users, nullable
- `remarks` TEXT, nullable
- `status` VARCHAR(20) default `Active` — `Active` or `Voided`
- `voided_by` BIGINT FK → tb_users, nullable
- `voided_at` TIMESTAMPTZ, nullable
- `void_reason` TEXT, nullable — required when voiding
- `created_at` / `updated_at`
- Unique: `(school_id, receipt_number)`

**Voiding (soft void, never hard-deleted):** `POST /FeePayment/{id}/void` (not `DELETE`). Body requires a non-blank `voidReason`. In one transaction: lock the affected `tb_student_fee_dues` rows, subtract each of the payment's allocations from the corresponding due's `amount_paid`, set `status = Voided` with `voided_by`/`voided_at`/`void_reason`. The payment row, its allocations, and its receipt number are never deleted or reused — a voided receipt stays printable, clearly marked VOID. Affected installment status simply recomputes from the reduced `amount_paid`. Voiding an already-voided payment returns 404 (not 409 — accepted as-is). Only School Admin and Accountant may void or record a payment (`CanManageFees`, section 25). No time limit — the audit trail is the control. Voiding does not track or move actual cash (see section 23).

### `tb_fee_payment_allocations` (splits one payment across one or more dues)
- `allocation_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `payment_id` BIGINT FK → tb_fee_payments (cascade delete)
- `fee_due_id` BIGINT FK → tb_student_fee_dues
- `allocated_amount` NUMERIC(12,2)
- `created_at` TIMESTAMPTZ
- Unique: `(payment_id, fee_due_id)`

A parent paying off 3 months of tuition arrears in one transaction produces one `tb_fee_payments` row and 3 `tb_fee_payment_allocations` rows, each updating the corresponding due's `amount_paid`. The caller can send an explicit split, or omit it for auto-allocation (oldest due first). Validated: allocations must sum to the payment amount exactly, every due must belong to the same student+school, and no allocation can push a due's `amount_paid` above its `amount_due`.

### Student list fee status (built)
`GET /Student/{schoolId}` returns, per student, `feeStatus`, `feeTotalDue`, `feeTotalPaid`, `feeOutstanding` — scoped to the list's academic year, computed in one grouped query across all listed students (no N+1). Overall status priority:
- `Overdue` — any due is Overdue
- `Partially Paid` — else, any due has `0 < amount_paid < amount_due` (this also covers "some installments fully paid, rest untouched, none overdue")
- `Pending` — else, no amount paid yet on anything
- `Paid` — else (every due fully paid, **or** the student has fee items but zero amount owed — e.g. every optional fee switched off, or a 100% concession — since nothing is outstanding)
- `Not Set` — the student has no `tb_student_fee_items` for the year at all (no fee structure assigned)

A `feeStatus` filter is available on the same list endpoint (`All` / `Overdue` / `Partially Paid` / `Pending` / `Paid` / `Not Set`), applied server-side after status is computed; an unrecognized value returns 400.

### Fee Dashboard (built)
`GET /api/FeeDashboard?academicYearId={required}&classId=&sectionId=&days=` — scoped to one academic year (optionally narrowed to class/section), returns:
- `totalCollected` — sum of **Active** payment allocations to installments in scope for the year (not raw `tb_fee_payments.amount`, since one payment carries no year of its own and summing the raw amount could mix years for a payment spanning two — allocations are scoped correctly)
- `totalOutstanding` — sum of `amount_due - amount_paid` across all dues in scope
- `overdueAmount` / `overdueCount`
- `upcomingAmount` / `upcomingCount` — unpaid/partially-paid installments due from today through today+`days` (default 7, caller can widen), excluding anything already Overdue so nothing is double-counted
- a month-by-month collected total for the academic year (plain table in the frontend — no new charting library was introduced)

Access: `CanViewFees` (section 25) — School Admin and Accountant get full access including the `canManage`-gated actions (class-wise report, CSV export, per-student Fees link); Principal gets identical numbers read-only. Runs as 4 queries per load regardless of student count (2 validation lookups + 1 grouped installment aggregate + 1 monthly payment sum) — no per-student loop.

### Reports (built)
- **Student-wise fee statement**: not a separate endpoint — the existing student Fees tab (items, dues, payment history, void) already is this report for the student's current enrollment year, so the dashboard links into it rather than duplicating it. A past-year statement would need a year parameter added to the existing Fees tab endpoints (not yet needed/built).
- **Class-wise collection report**: `GET /api/FeeReports/class/{classId}?academicYearId=` — per-student total due/collected/outstanding/status plus a class total row, sortable client-side (defaults to highest outstanding first), reuses the same grouped query as the student list (no N+1). CSV export is a client-side, no-dependency export, available to School Admin and Accountant only (not Principal, per `canManage`).

### Build status — MODULE COMPLETE
- **Stage 1** (fix existing-student load crash): done.
- **Stage 2** (`tb_student_fee_items`, `tb_student_fee_dues`, due-day config, due generation, admission-time fee configuration, Fees tab with due schedule + status badges): done and deployed.
- **Stage 3** (`tb_fee_payments`, `tb_fee_payment_allocations`, payment collection, receipts, voiding, mid-year/gap fix, RBAC on both fee-payment endpoints, receipt-number concurrency lock): done and deployed, confirmed against real `smss-db`.
- **Stage 4** (student list fee-status column + filter, Fees tab Paid/Outstanding columns): done and deployed, confirmed against real `smss-db`.
- **Stage 5** (Fee Dashboard, class-wise report, `CanViewFees`): done and deployed, dashboard figures cross-checked to the rupee against real `smss-db` and the Fees tab.
- **Open follow-up** (not part of the Fee module itself, a pre-existing route-access gap it surfaced): give Accountant access to the student record's Fees tab — see section 9.
