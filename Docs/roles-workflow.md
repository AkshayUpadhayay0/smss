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

Fee Setup is placed before Students because Student Admission assigns a student to an existing Fee Structure — the structure must exist first. It only depends on Academic Year and Classes, both already earlier in this sequence, so it does not block on anything school-specific that hasn't been built yet. See section 27 for the full Fee Setup schema.

## 7. Principal workflow

Dashboard shows: Students, Teachers, Attendance, Admissions, Examinations, Academic Performance, Homework, Leave, Timetable, Notices, School Calendar.

**Academic:** manage academic year, monitor classes/subjects/teacher assignments, review student performance/examinations/report cards, monitor attendance/homework/timetable.
**Staff:** view employees, review teacher attendance, approve teacher leave, monitor teacher workload, assign teachers where permitted.
**Students:** view students, monitor attendance, review academic performance and disciplinary/student notes, approve certain student actions.
**Administration:** notices, school calendar, events, announcements, reports.

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

Setup sequence: **Fee Types → Fee Structures (per Class + Academic Year) → Fee Concessions/Discounts**. Depends only on Academic Year and Class masters (already earlier in the setup wizard) — does not depend on the Student module, so it can be built and tested before Students/Admissions exist. Student Fee Assignment (below) is the one piece that does depend on Students and is deferred until that module exists.

### `tb_fee_types` (school-owned master)
- `fee_type_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `school_id` VARCHAR(100) FK → tb_schools
- `fee_type_name` VARCHAR(100)
- `fee_type_code` VARCHAR(30)
- `description` TEXT
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

### Deferred — depends on Student module (not yet built)
- `tb_student_fee_structure` — assigns a student to a fee structure: `student_id`, `fee_structure_id`, `school_id`, `academic_year_id`
- `tb_student_fee_concessions` — per-student concessions applied (many-to-many, a student can have more than one): `student_id`, `concession_id`, `approved_by`, `remarks`

These two tables are designed now so the schema is already accounted for when the Student/Admission module is built, but are not created until `tb_students` exists.