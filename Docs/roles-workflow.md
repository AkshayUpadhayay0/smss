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

Recommended setup sequence: School Profile → Academic Year → Classes → Sections → Subjects → Departments → Designations → Employees → Teachers → **Fee Setup (Fee Types → Fee Structures → Fee Concessions)** → Admissions → Students → Guardians → Examination Configuration → Timetable.

Fee Setup is placed before Admissions/Students because Student Admission assigns a student to an existing Fee Structure — the structure must exist first. It only depends on Academic Year and Classes, both already earlier in this sequence, so it does not block on anything school-specific that hasn't been built yet. See section 27 for the full Fee Setup schema, section 28 for Student Fee Billing & Collection, section 29 for Employee Management (registration and login — Stage 1 complete), section 30 for Class-Teacher Assignment, section 31 for Attendance, section 32 for the Academic Calendar, section 33 for Teacher Timetable, section 34 for Leave Management, section 35 for Homework, and section 36 for the Admission pipeline — all nine complete and deployed.

## 7. Principal workflow

Dashboard shows: Students, Teachers, Attendance, Admissions, Examinations, Academic Performance, Homework, Leave, Timetable, Notices, School Calendar.

**Academic:** manage academic year, monitor classes/subjects/teacher assignments, review student performance/examinations/report cards, monitor attendance/homework/timetable.
**Staff:** view employees, review teacher attendance, approve teacher leave (**built — section 34**, via `CanApproveLeave()`), monitor teacher workload, assign teachers where permitted. (Employee reads are currently School Admin only — see section 29 — so Principal's general employee visibility is not yet built, Leave being the one exception.)
**Students:** view students, monitor attendance, review academic performance and disciplinary/student notes, approve certain student actions. Principal can also decide admission applications (**built — section 36**, via `CanDecideAdmission()`), the first place "approve/reject" on a pipeline this formal exists for Principal outside Leave.
**Administration:** notices, school calendar, events, announcements, reports. Principal also has read-only access to the Fee Dashboard and class-wise fee report (section 28) — numbers only, no collection/void actions and no per-student Fees tab link. Principal can also create/edit/delete Academic Calendar events (section 32, `CanManageEvents`) — the first place Principal got a real write permission outside Fees' read-only case.

Principal does not yet have attendance, timetable, or homework read access (sections 31, 33, 35) — currently only the relevant class teacher/creator and School Admin can read those. Future work, following the same `CanViewFees`-style read-only pattern already established (section 25).

## 8. Teacher workflow

Dashboard shows: My Classes, My Subjects, Today's Timetable, Attendance, Homework, Examinations, Marks, Students, Leave, Notices.

**Classes:** view assigned classes/sections/subjects/students.

**Attendance:** `Select Class → Select Section → Select Date → Mark Attendance → Save`. **Built and deployed (section 31).** Only the section's current class teacher (section 30) may mark attendance for that section, once per day, same calendar day only — enforced server-side via `IClassTeacherService.IsClassTeacherAsync`, not just hidden in the UI for other teachers. Reachable from "Mark Attendance" in the Teacher/School Admin nav.

**My Timetable:** **Built and deployed (section 33).** A read-only weekly grid of the teacher's own periods across all sections they teach, at `/timetable/my` — self-only, 403 for anyone else's.

**Homework:** `Create Homework → Select Class → Select Section → Select Subject → Set Due Date → Publish`. **Built and deployed (section 35), v1 scope: publish and view only, no submission/review/marks.** A Teacher may only create homework for a class/section/subject they're currently timetabled to teach (section 33), and may only edit/delete homework they personally created — even another teacher eligible for the same section/subject can't touch it. At `/homework`.

**Examination:** view assigned examinations/schedule, enter marks, update marks before lock, view student performance, add remarks.

**Leave:** `Apply Leave → Principal / Authorized Admin → Approve / Reject`. **Built and deployed (section 34).** At `/leave/my` — apply, cancel while still Pending, see your own request history. Approval is by School Admin or Principal, never the applicant themselves, even for a Principal's own leave.

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

**Fee collection workflow:** `Student → Fee Due → Accountant → Collect Payment → Record Transaction → Generate Receipt → Update Student Fee Balance`, via the student's Fees tab (`/students/:id`), the only place in the app that collects or voids a payment.

See section 28 for the full schema and screens behind this workflow (`tb_fee_payments`, `tb_fee_payment_allocations`, `tb_student_fee_dues`, Fee Dashboard, class-wise report) — this module is now complete, including server-side role checks.

**Accountant's access, precisely:** Accountant can open a student record, but sees only the header (name, admission number, status, class/section) and the Fees tab — no Profile tab, no photo actions, no student login creation. They reach a student via Fees → Class-wise Report → the "Fees" link on each row (there is no general student list for Accountant — not needed, since the class report covers it). On the Fees tab they can Collect Payment, Void Payment, and view/print receipts — but **cannot** change what a student is billed (assign/change fee structure, add/remove a concession, change a fee item's frequency or opt-out) — that is School Admin only (`CanConfigureFees`, section 25), enforced server-side as well as hidden in the UI, since a bad concession is effectively a cash decision. Accountant has no role in the Admission pipeline (section 36) — fee structure assignment at admission happens automatically when School Admin/Principal confirms an application, not through a separate Accountant step.

Accountant should not be able to modify academic marks, attendance, or student academic records unless explicitly authorized.

## 10. Admission Staff workflow

```
Application → Document Collection → Application Review → Admission Decision
→ Admission Approval → Confirm Admission (creates Student + assigns Fee Structure)
```

**Built and deployed — see section 36 for the full schema, decisions, and build status.** A real admission pipeline already existed in the codebase before this round of work (application intake, review, approve/reject, a Confirm step that creates the student and assigns a fee structure) — this doc previously and incorrectly said the module wasn't built at all. What this round of work added on top of the existing pipeline: a real `ADMISSION_STAFF` role with its own login (previously only School Admin could touch admissions), a two-tier authorization split (`CanManageApplications()` for day-to-day intake/review vs `CanDecideAdmission()` for the actual approve/reject, so Admission Staff can run the pipeline but not make the final call), a dedicated application-documents table, and a closed security gap where any logged-in user of the school — not just staff — could previously approve, reject, or confirm an application.

No Enquiry/lead-tracking stage exists (deliberately out of scope, a lighter CRM-style feature of its own). There is no applicant/guardian self-service application form — staff enter everything on the applicant's behalf.

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

HR/Admin can manage employee information. Principal can view/manage relevant staff information according to permissions (not yet built — see section 29).

See section 29 for the full Employee Management schema and build status: registration, designation/department, documents, employment status, and login creation are built and deployed. Employee **Leave** (apply/approve/reject, section 34) is also now built and deployed — see section 34, not this section, for its schema. The Employee login allow-list now includes `ADMISSION_STAFF` alongside `SCHOOL_ADMIN`/`PRINCIPAL`/`TEACHER`/`ACCOUNTANT` (section 36). Employee Attendance (staff clocking in/out — distinct from the student-facing Attendance in section 31) and Salary/Payroll shown above are still future modules, not yet designed. Don't conflate Employee Attendance (not built) with Student Attendance (section 31, built).

## 12. Librarian workflow (if Library Management enabled)

Manage books, authors, publishers, categories, book copies; issue/return/renew books; track overdue books; manage fines; view student/teacher borrowing history; generate reports.

## 13. Student workflow

Self-service portal. Student can view: My Profile, My Attendance, My Timetable, My Subjects, My Homework, My Exams, My Marks, My Report Cards, My Fees, My Notices, My Calendar, My Documents. Student generally cannot modify academic records.

Student-facing attendance and timetable viewing are not yet built (sections 31, 33 have the data and read endpoints shaped to support them later, but no Student-facing screen exists yet). The Academic Calendar (section 32) **is** visible to Student today — every role reads the same calendar, no targeting yet. **My Homework (section 35) is also built and live for Student** — a read-only list of homework assigned to their own current section, soonest due first; this is the first Student-facing screen built alongside its module rather than deferred, since it carries no submission logic. Student leave (section 34) is likewise not yet self-service — a class teacher or School Admin records it on the student's behalf for now. A Student login now has no access at all to any other student's record — see section 36's student-endpoint hardening note — which closed a real gap rather than changing any intended behavior.

## 14. Guardian/Parent workflow

```
Guardian Login → Guardian Dashboard → Select Child
```

If a guardian has two children, they switch between Child 1 / Child 2. Guardian can view: student profile, attendance, homework, examination schedule, marks, report cards, fees, fee receipts, notices, school calendar, teacher communication where enabled.

Guardian login itself is not yet built (only Student and Employee/Teacher logins exist so far). Not yet scoped.

## 15. Attendance workflow

**Teacher:** `My Class → Date → Mark Attendance → Submit`
**Principal/Admin:** view attendance class-wise, student-wise, date-wise, monthly.
**Guardian:** view child attendance.

**Built and deployed — see section 31 for the full schema, decisions, and build status.** The Teacher row above is live exactly as described: class-teacher-only, once per section per day, same-day resubmit allowed, locked after that (School Admin can still correct or backfill, with a required reason, audited). The Principal/Admin and Guardian rows above are not yet built as UI — School Admin has full read/correct access via the API, but no dedicated admin "view attendance" or "who hasn't marked" screen exists yet, and Principal/Guardian have no attendance access at all yet (see sections 7, 13, 14).

## 16. Examination workflow

```
School Admin / Principal → Create Examination → Create Exam Schedule → Assign Subjects
→ Assign Teachers → Publish Schedule → Teacher → Enter Marks → Review / Lock
→ Generate Result → Generate Report Card → Student / Guardian → View Result
```

Not yet built. A lighter-weight Internal Assessment / Class Test tracking module (separate from formal Examination/Report Card, doesn't affect final results) is also planned as future work, not yet designed.

## 17. Homework workflow

```
Teacher → Create Homework → Select Class / Section / Subject → Publish
→ Student → View Homework → Submit Homework → Teacher → Review Submission → Feedback / Marks
```

**Built and deployed — see section 35 for the full schema, decisions, and build status, with one deliberate scope cut.** Only the first row is built: Teacher (or School Admin) creates homework for a class/section/subject with a due date and optional attachment, and Student reads it for their own section. The second half of the diagram — student submission, teacher review, feedback, and marks — is explicitly **not** built in v1; it was scoped out up front as a materially bigger addition (a new submission table, file-upload-from-Student handling, and the first truly interactive Student screen in the app) and deferred rather than attempted alongside the simpler publish-and-view flow.

## 18. Leave workflow

**Teacher/Employee:** `Apply Leave → Principal / Authorized Admin → Review → Approve / Reject`
**Student:** `Student/Guardian → Leave Request → Class Teacher / Authorized Staff → Approve / Reject`

**Built and deployed — see section 34 for the full schema, decisions, and build status.** Both rows are live largely as described, with two scoping notes: no leave-quota/balance tracking (pure request/approve/reject, decided deliberately simple for v1), and Student leave is currently entered and approved by the class teacher or School Admin on the student's behalf (Guardian login doesn't exist yet, section 14, and there's no Student-facing self-apply screen yet either — the API is shaped to support one later without a schema change).

## 19. Timetable workflow

```
School Admin / Principal → Configure (Classes, Sections, Subjects, Teachers, Periods)
→ Create Timetable → Publish → Teacher / Student → View Timetable
```

**Built and deployed — see section 33 for the full schema, decisions, and build status.** School Admin configures the period/bell schedule and assigns subject+teacher+room per class/section/day/period; a Teacher reads their own weekly grid at `/timetable/my`. Principal, Student, and Guardian read access is not yet built (same conservative-by-default pattern as Attendance, section 31) — endpoints exist, screens don't yet.

## 20. Notice / Communication workflow

Authorized users: Super Admin, School Admin, Principal — can publish notices according to their scope.

```
Principal → Create Notice → Target (All Students / Teachers / Parents / Class 8 / Specific Section) → Publish → Notification
```

Not yet built. Planned to also carry fee-due and absent alerts (now that Attendance exists, section 31) once this module is scoped — a general notification delivery layer, not yet designed. If/when this module needs audience targeting (All / Staff / Students+Guardians / a specific class), note that the Academic Calendar (section 32) deliberately skipped that for v1 — the same design question will come up here and may be worth solving once, shared between the two.

## 21. School Calendar

School Admin/Principal can manage: Academic Events, Holidays, Exams, Parent Meetings, School Events, Activities, Important Dates. Users view events according to their permissions.

**Built and deployed — see section 32 for the full schema, decisions, and build status.** School Admin and Principal can create/edit/delete events; every role in the school reads the same calendar (no audience targeting in v1 — every event is visible to everyone). Note: Attendance's "who hasn't marked" list (section 31) still has no concept of holidays/non-working days — it will list every calendar day including Sundays and school holidays until that gets explicitly wired to this calendar.

## 22. Student Transfer / SLC / TC

```
Student → Request Transfer / Leaving → Verify Fees → Verify Documents
→ Academic Verification → Generate SLC / TC → Student Status Updated
```
Authorized by School Admin/Principal. Not yet built.

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
| Employee Management | ❌ | Manage | ❌† | ❌ | ❌ | ❌ | ❌ | ❌ |
| Class-Teacher Assignment | ❌ | Manage | ❌ | View own (via check) | ❌ | ❌ | ❌ | ❌ |
| Student Management | ❌ | ✅ | ✅ | Assigned Students | View | ❌ | Own | Child |
| Admissions | ❌ | Manage/Decide†† | Manage/Decide†† | ❌ | ❌ | Manage†† | ❌ | ❌ |
| Attendance | Manage/Correct‡ | Manage/Correct‡ | ❌‡ | Mark (class teacher only) | ❌ | ❌ | ❌‡ | ❌‡ |
| Timetable | Manage§ | Manage§ | ❌§ | View own§ | ❌ | ❌ | ❌§ | ❌§ |
| School Calendar | Manage | Manage | Manage | View | View | View | View | View |
| Examinations | ❌ | Manage | Manage | Enter Marks | ❌ | ❌ | View | View |
| Homework | ❌ | Manage‖ | ❌‖ | Create/Manage own‖ | ❌ | ❌ | View own section‖ | ❌‖ |
| Leave | ❌ | Apply/Approve¶ | Apply/Approve¶ | Apply/Cancel own | Apply/Cancel own | ❌ | Recorded for¶ | ❌ |
| Fees | Subscription | Manage | View | ❌ | Collect/Void | View | View | Pay/View |
| Finance | SaaS Billing | Manage | View | ❌ | Manage | ❌ | ❌ | ❌ |
| Library | ❌ | Manage | View | Borrow | ❌ | ❌ | Borrow | View |
| Notices | SaaS | Manage | Publish | View | View | View | View | View |
| Reports | SaaS | School | Academic | Own Classes | Finance | Admission | Own | Child |

\* Exact permissions should be configurable through RBAC. Fees row for Accountant: "Collect/Void" (not "Manage") — collecting/voiding payments is Accountant's job, but configuring what's billed (fee types, structures, concessions, a student's fee items) is School Admin only. See section 28.
† Employee Management row currently reads ❌ for every non-School-Admin role because all employee endpoints, including reads, are School Admin only as a conservative starting point (section 29) — this row (Principal in particular) is expected to loosen once that's revisited, not a permanent design choice.
‡ Attendance (section 31) is live, but only Teacher (class-teacher-only, mark/same-day-edit) and School Admin (full read, correct, backfill) have any access today — Super Admin's row shown as Manage/Correct reflects that a Super Admin impersonating/configuring at the school level has the same reach as School Admin, not a separate superadmin-specific screen. Principal, Student and Guardian all have ❌ for now — not a permanent design choice, just not built yet (see sections 7, 13, 14).
§ Timetable (section 33) is live, but only School Admin (full manage, including the period/bell-schedule setup) and Teacher (their own grid, self-only) have access today. Principal, Student and Guardian are ❌ for now, same reasoning as the Attendance footnote above — endpoints exist, screens don't yet.
¶ Leave (section 34) is live. Employee leave: any employee with a login applies for themselves and can cancel their own still-Pending request; School Admin or Principal approve/reject (never the applicant, even a Principal approving their own). Student leave: there's no Student-facing apply screen yet, so a request is recorded on the student's behalf by their class teacher or School Admin, and approved by the class teacher or School Admin — Student shows "Recorded for" rather than "Apply" to reflect this; Accountant and Guardian have no access (Guardian login doesn't exist yet, Accountant was never in scope for leave).
‖ Homework (section 35) is live, v1 scope (publish + view, no submission). School Admin can create/edit/delete homework for any Active section/subject (subject must still be mapped to the class via Class-Subject Mapping — enforced for School Admin too, not just Teacher). A Teacher can create homework only for a section/subject they're currently timetabled to teach, and can edit/delete only homework they personally created — not another teacher's, even for the same section/subject. Student reads only their own current section's homework, read-only, across all subjects. Principal, Accountant, Admission and Guardian all have ❌ — not built yet (Guardian login doesn't exist at all; Principal/Accountant weren't in this module's scope, consistent with the conservative default most recent modules have shipped with).
†† Admissions (section 36) is live (it already existed — this is not a new module, see section 10). `CanManageApplications()` (School Admin, Principal, Admission Staff) covers intake, document collection, and review. `CanDecideAdmission()` (School Admin, Principal only — not Admission Staff) covers the final Approve/Reject and the Confirm Admission step that creates the student and assigns a fee structure. Student Management's row is now ❌ for Admission Staff specifically because a real gap was closed this round: student-creation endpoints previously had no role check at all beyond same-school, so this table's earlier "✅" for Admission under Student Management reflected an unintentional hole, not a design choice — Admission Staff's actual job is the application pipeline, not general student-record management.

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

**Known current gap:** there is no real permission system yet — role checks in code currently test role **names** directly rather than resolving permissions, and parts of the project still have no server-side role checks at all (Admissions did, until section 36 closed it — a reminder that this gap is real and worth checking for, not just a theoretical risk). This is an accepted stopgap, consistent project-wide, to be replaced once `lut_roles`/`tb_user_roles` grows real permission mapping. Do not treat each new instance of this pattern as a fresh decision — it matches existing practice. **A project-wide RBAC pass is explicitly deferred as separate future work, not bundled into any single feature** — but when work on a module surfaces a concrete, exploitable gap in that module (as Fees' payment-recording endpoint did, and Admissions' student-creation endpoints just did), closing it immediately has been the consistent call, not deferring it with the rest.

**Also project-wide now:** `[Authorize(Roles = ...)]` attributes are commented out across the API (a leftover from early development, predating real login). Where a module needs an actual role check today, it's done with an in-controller claim check instead (e.g. Employee's `IsSchoolAdmin`) rather than the attribute. Don't assume the attribute is enforcing anything anywhere in this codebase until this is revisited.

**Also project-wide now:** first-login enforcement is a genuine server-side check, not just a client guard. `FirstLoginEnforcementMiddleware` reads `tb_users.is_first_login` live (no caching, no token claim — can't be forged or go stale) on every authenticated request and returns 403 "Password change required..." except for `/Auth/change-password`, `/Auth/me`, `/Auth/refresh`, `/Auth/logout`. Covers every account type on a temporary password, including School Admin right after school registration. This closed a real gap: previously only the Angular route guard blocked a first-login user, so a valid token could hit any endpoint directly while bypassing the browser.

The Fees module (section 28) is the first place view/manage/configure distinctions were needed, and all of them were done as small role-name helpers rather than a generic framework:
- `CanManageFees()` — School Admin, Accountant. Payment recording and voiding only.
- `CanViewFees()` — School Admin, Accountant, Principal. Fee Dashboard and class-wise report; responses carry a `canManage` flag so the frontend hides manage-only actions (class-report button, per-student Fees link, CSV export) for a view-only caller (Principal).
- `CanConfigureFees()` — School Admin only. Every write on fee types, fee structures/details, fee concessions, and a student's fee items (frequency, opt-out, structure/concession assignment). Deliberately narrower than `CanManageFees` — Accountant can collect/void money but cannot change what's owed; this is enforced server-side, not just hidden in the UI.

This three-helper pattern (manage vs. view vs. configure, named for what they actually gate) is the template to reuse if another module needs a similar split, rather than inventing new helpers per module or building a generic permission framework prematurely. Employee Management (section 29), Class-Teacher Assignment (section 30), and Attendance (section 31) all currently use a single `IsSchoolAdmin` check (plus, for Class-Teacher and Attendance, a narrow self-only exception for Teacher callers on their own section) rather than this three-way pattern. The Academic Calendar (section 32) introduced `CanManageEvents()` (School Admin + Principal) since Principal needed a real write permission for the first time outside Fees' read-only case. Timetable (section 33) went back to the single-`IsSchoolAdmin`-plus-self-only-Teacher-read pattern. Leave Management (section 34) introduced `CanApproveLeave()` (School Admin + Principal, same shape as `CanManageEvents`) for employee leave decisions, plus a self-only block (nobody, including a Principal, can decide their own leave request) and reuses `IClassTeacherService.IsClassTeacherAsync` (section 30) to gate student-leave decisions by the student's current class teacher. Homework (section 35) introduced `CanManageHomework()` (School Admin or Teacher, same shape as `CanManageEvents`), layered with an eligibility check (`ITimetableService.IsTeachingSectionSubjectAsync`, reusing Timetable data rather than a new mapping table) and an ownership check (only the creating teacher, not any eligible one, can edit/delete). The Admission pipeline (section 36) introduced the project's first explicit two-tier split on a single module in this style: `CanManageApplications()` (School Admin, Principal, Admission Staff) vs `CanDecideAdmission()` (School Admin, Principal only) — Admission Staff can run the whole intake/review flow but never the final decision, mirroring how Leave already keeps "apply" and "decide" as separate powers. Revisit any of these if/when Principal gets broader employee, class-teacher, attendance, timetable, or homework visibility.

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

Setup sequence: **Fee Types → Fee Structures (per Class + Academic Year) → Fee Concessions/Discounts**. Depends only on Academic Year and Class masters — does not depend on the Student module, so it was built and tested before Students/Admissions existed. Status: **complete** (Fee Types, Fee Structures, Fee Concessions all built and tested, backend + Angular UI; all writes are now `CanConfigureFees`-gated, School Admin only — see section 25).

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
- `isactive` BOOLEAN — **note:** this column is spelled `isactive` here, a legacy naming inconsistency from when the Fee module was first built. Every lookup table built since (`lut_gender`, `lut_employment_status`, `lut_attendance_status`, `lut_event_category` — sections 29, 31, 32) uses `is_active` instead, which is now the standard going forward. Not worth a breaking migration to fix retroactively on this one table.

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
- `tb_student_fee_structure` — links a student to a fee structure: `student_id`, `fee_structure_id`, `school_id`, `academic_year_id`. Built and working; auto-assigned at admission based on class + academic year, with manual assignment/reassignment as fallback (School Admin only).
- `tb_student_fee_concessions` — per-student concessions applied (many-to-many): `student_id`, `concession_id`, `approved_by`, `remarks`. Built and working (School Admin only).

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
- `frequency_id` BIGINT FK → lut_fee_frequency — the frequency **this student** chose for this fee head (e.g. a parent may choose to pay tuition quarterly even though another student in the same class pays monthly); seeded from the fee structure's default but independently stored and editable per student (School Admin only — `CanConfigureFees`). Allowed frequencies: One Time/Annual lines stay fixed; Monthly/Quarterly/Half-Yearly lines can be paid Monthly, Quarterly, Half-Yearly, or Annual (student's choice).
- `base_amount` NUMERIC(12,2) — the **per-period** amount from the fee structure (e.g. ₹2,500/month). The year total for an item is `net_amount × periods-per-year-at-the-structure's-default-frequency`, then re-split across whatever frequency the student actually chose — so a student paying quarterly pays the full proportional share (e.g. ₹7,500/quarter for a ₹2,500/month line), never a fraction of the annual total.
- `concession_amount` NUMERIC(12,2) default 0 — resolved from any applicable `tb_student_fee_concessions`
- `net_amount` NUMERIC(12,2) — `base_amount - concession_amount`, the resolved per-period billed amount
- `is_applicable` BOOLEAN — lets an optional fee head (`tb_fee_types.is_optional = true`) be toggled on/off per student without deleting the row or losing history. Included on the admission form only for fee types marked optional; **defaults to unticked (opt-in)**, including when an API caller omits it, so a forgotten tick never silently bills an unwanted fee.
- `isactive` BOOLEAN
- `created_at` / `updated_at`
- Unique: `(student_id, academic_year_id, fee_type_id)`

Seeded from `tb_fee_structure_details` for the student's class at admission time (one row per applicable fee head), with frequency and applicability editable per student afterward from the Fees tab (School Admin only). Re-synced automatically whenever the underlying fee structure, a concession, or the item itself changes.

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
- `receipt_number` VARCHAR(50) — school-scoped, sequential, human-readable (e.g. `RCP-2026-0001`, per school per year). Allocated under a per-school database lock so concurrent payments queue instead of colliding (an earlier retry-only approach could exhaust its 3 tries and 500 under load — fixed). The same lock pattern was reused for admission application numbers (section 36).
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

### Student-record access for Accountant (built)
`GET /api/Student/{schoolId}/{studentId}` returns a **limited view** (`limited: true`) when the caller is an Accountant who isn't also School Admin: student id, admission number, name, status, class, section, year, roll number. Everything else — DOB, gender, blood group, category, email, mobile, masked Aadhaar, photo, login id, and the entire guardian list — is stripped server-side, not just hidden in the UI. School Admin, Principal, and Teacher continue to get the full profile. Photo upload/remove and create-login on this endpoint are refused (403) for Accountant. Frontend-side, the student page opens straight on the Fees tab for Accountant with no Profile tab rendered, and "Back" returns to the class-wise report with filters preserved. (Student-endpoint role checks were hardened further during the section 36 work — see that section for the Student/Guardian-blocking fix and the School-Admin-only write restriction.)

### Build status — MODULE COMPLETE
- **Stage 1** (fix existing-student load crash): done.
- **Stage 2** (`tb_student_fee_items`, `tb_student_fee_dues`, due-day config, due generation, admission-time fee configuration, Fees tab with due schedule + status badges): done and deployed.
- **Stage 3** (`tb_fee_payments`, `tb_fee_payment_allocations`, payment collection, receipts, voiding, mid-year/gap fix, RBAC on both fee-payment endpoints, receipt-number concurrency lock): done and deployed, confirmed against real `smss-db`.
- **Stage 4** (student list fee-status column + filter, Fees tab Paid/Outstanding columns): done and deployed, confirmed against real `smss-db`.
- **Stage 5** (Fee Dashboard, class-wise report, `CanViewFees`): done and deployed, dashboard figures cross-checked to the rupee against real `smss-db` and the Fees tab.
- **Security closeout** (Accountant frontend access to the Fees tab, plus the server-side RBAC it surfaced — `CanConfigureFees` on all fee-setup/billing-config writes, limited student-profile view for Accountant): done and deployed, confirmed against real `smss-db`.
- **No open items.** A project-wide RBAC pass covering whatever's still unchecked elsewhere remains separate future work (section 25) — not part of this module, and not blocking it.

## 29. Employee Management module (registration + login) — STAGE 1 COMPLETE

One table holds every staff member — Principal, Teacher, Accountant, etc. There is no separate Teacher table: which system role(s) an employee holds is assigned via the existing `tb_user_roles` mechanism (same pattern School Admin already uses), not by which row they're in. Designation (section-specific job title, e.g. "Senior Teacher") and system role (TEACHER, PRINCIPAL, ...) are deliberately separate things — an employee's designation label doesn't drive their login permissions, the role assignment at login creation does.

Depends on the existing school-owned `tb_employee_designations` / `tb_employee_departments` masters (built earlier as Core School Masters). Decided to use `status_id → lut_status` for the "hide this row" convention rather than a boolean, matching how `tb_students`, designations, departments and document types already do it project-wide — the Fee module's `isactive` boolean naming doesn't apply here since the Student/Employee side of the project uses `status_id` instead.

### `lut_employment_status` (global lookup, small/fixed list)
- `employment_status_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `status_name` VARCHAR(50) — Active, On Leave, Resigned, Terminated, Retired
- `status_code` VARCHAR(30)
- `blocks_login` BOOLEAN — true for Resigned, Terminated, Retired; false for Active, On Leave. This flag, not a hardcoded status-name check in code, is what the login logic reads — so adding a new blocking status later is a data change, not a code change.
- `is_active` BOOLEAN — column naming note: see section 27's `lut_fee_frequency` entry and section 31 for the project-wide standard (`is_active`, not `isactive`); this table follows the standard.

### `tb_employees`
- `employee_id` VARCHAR(20) PK — business-generated: `{schoolShortCode}EM{nnn}` (e.g. `APS001EM001`), assigned via a per-school **locked max-plus-one** (not a simple count-plus-one, which can collide under concurrent creates — confirmed with 6 simultaneous creates giving 6 distinct consecutive ids)
- `school_id` VARCHAR(100) FK → tb_schools
- `designation_id` BIGINT FK → tb_employee_designations
- `department_id` BIGINT FK → tb_employee_departments
- `employment_status_id` BIGINT FK → lut_employment_status
- `employee_type` VARCHAR(20) — `Teaching` or `Non-Teaching`. Consumed by Class-Teacher Assignment (section 30) and Timetable (section 33), both of which require `Teaching` plus the TEACHER role to be eligible.
- `first_name` / `middle_name` (nullable) / `last_name`
- `gender_id` BIGINT FK → `lut_gender`, `blood_group_id` BIGINT FK → `lut_blood_group` — real foreign keys, unlike the Student module's equivalent plain-integer columns (students were left as-is; Employee doesn't inherit that gap)
- `date_of_birth` DATE
- `email` VARCHAR, nullable — unique per school, case-insensitive (the Student module doesn't enforce an equivalent uniqueness; Employee does)
- `mobile` VARCHAR — required
- `alternate_mobile` VARCHAR, nullable
- `address` TEXT
- `date_of_joining` DATE
- `date_of_leaving` DATE, nullable
- `qualification` TEXT, nullable
- `photo_path` VARCHAR, nullable — `{schoolCode}/employees/{employeeId}/photo_<timestamp>.<ext>`, same validation as student photos (2 MB limit, file-signature check, not just extension)
- `status_id` BIGINT FK → lut_status — the project-wide "hide this row" toggle, independent of `employment_status_id`
- `created_at` / `updated_at`

### `tb_employee_documents`
Mirrors the document-upload pattern built fresh for this module (the Student module had no equivalent — only `tb_admission_documents` existed, nothing reusable). FK'd to `employee_id`, uses the existing document-type master filtered to types that apply to Employee or Both. Files are stored in a **private** folder, never served by direct URL — only through an authenticated, School-Admin-gated download endpoint. Accepts PDF and images up to 5 MB with a file-signature check. Removing a document hides the row and keeps the file (consistent with the project's no-hard-delete convention). A school needs at least one document type with "applies to" Employee or Both configured before employee document upload will work — the UI surfaces this rather than failing silently.

### Login creation
`POST /api/Employee/{id}/create-login` — a separate action after the employee record exists, not automatic at registration (mirrors how Student login creation already works in this codebase, deliberately not a new pattern). Takes `{roleIds}`. Login id is the employee id; a random 12-character temporary password is shown once and emailed (not the Student flow's name-plus-birthday pattern — a weaker scheme that's fine for a student but not appropriate to carry over here). Returns 409 if a login already exists, and 409 if the employee's employment status would leave the new login blocked from the start (see login gating below) — no point creating a login that can't be used.

**Assignable roles are an allow-list by `role_code`**: `SCHOOL_ADMIN`, `PRINCIPAL`, `TEACHER`, `ACCOUNTANT`, `ADMISSION_STAFF` (the last added in section 36), read from `lut_roles` but filtered to this set — not "every active role except Super Admin" (an earlier draft that would have offered Student/Guardian for an employee login, which made no sense). `SUPER_ADMIN`, `STUDENT`, `GUARDIAN` are excluded in code regardless of what's in the table. Adding a future staff role to the allow-list is a one-line change, no schema change — exactly what happened when `ADMISSION_STAFF` was added.

### Login gating — two independent switches
A login works only when **both** are true:
1. `tb_employees.status_id` resolves to Active (the "hide this row" toggle), **and**
2. The employee's current `employment_status_id` has `blocks_login = false`.

Changing one does not touch the other — e.g. deactivating the row (switch 1 off) then later marking the employee Active again in employment status (switch 2) does **not** re-enable the login, because switch 1 is still off; both have to say "allow" independently. This was specifically tested: toggle record Inactive → set Terminated → set Active (still refused, record switch is off) → toggle record Active (now works). The API's token-refresh check also re-validates both switches, so an already-active session is cut off the moment either one flips, not just new logins.

### Authorization (current state)
`[Authorize(Roles = ...)]` attributes are commented out project-wide (see section 25) — Employee uses an in-controller `IsSchoolAdmin` claim check instead, applied to **every** endpoint including reads. So for now Principal, Accountant, and Teacher all get 403 on employee data, including just viewing the list — see the section 24 footnote. This is deliberately conservative to start, not a long-term design; loosen it (e.g. Principal view access) when actually needed, following the `CanViewFees`-style pattern from section 25 rather than widening `IsSchoolAdmin` itself.

### Build status
- **Stage 1** (schema, registration CRUD, photo + document upload, employment status, login creation, role-gated login): built, tested, and **deployed to real `smss-db`** (existing data confirmed untouched by the migration). Backend and frontend suites green. Teacher login confirmed end-to-end.
- **Not yet built**: Teacher-Subject Mapping as a standalone concept (subject-level teaching assignments are now effectively captured per-slot by Timetable, section 33, rather than a separate persistent mapping table), Employee Attendance/Salary-Payroll (future modules per section 11, not designed yet — distinct from the now-built Student Attendance, section 31; Employee Leave is now built, section 34), broader role access beyond School Admin (see Authorization above).
- **Small pending UI polish**: a warning in the employee form when choosing Resigned/Terminated/Retired, since the server already enforces the login-block correctly but the form doesn't yet hint at the consequence before saving. (Prompted for; status not yet confirmed back.)

## 30. Class-Teacher Assignment module — COMPLETE, DEPLOYED

The first step toward Attendance (section 31): a hard, server-enforced answer to "is this employee allowed to mark attendance for this section today," rather than trusting that any TEACHER-role login picked the right section.

### `tb_class_teacher_assignments`
- `assignment_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `school_id` VARCHAR(100) FK → tb_schools
- `academic_year_id` BIGINT FK → tb_academic_years (same target `tb_fee_structures` uses)
- `class_id` BIGINT, `section_id` BIGINT — composite FK to `tb_sections(section_id, class_id)` (a new unique constraint was added to `tb_sections` to support this), which guarantees a section and its stated class can never mismatch — confirmed rejected on a raw insert with the wrong class.
- `employee_id` VARCHAR(20) FK → tb_employees
- `status_id` BIGINT FK → lut_status — the project-wide hide-row pattern; reassigning a section **deactivates the old row and inserts a new one** in the same transaction rather than overwriting it, so "who was class teacher of 8-B last term" stays answerable.
- `assigned_by`, created/updated audit columns

**Real schema note:** `tb_sections` is its own table (`section_id`, `class_id`, `section_name`, `status_id`) and is **not** year-scoped — a section persists across academic years, which is exactly why the academic year lives on the assignment, not the section.

**One active assignment per section per year, enforced at the database level**: a partial unique index on `(school_id, academic_year_id, class_id, section_id) WHERE status_id = <Active>`, with the Active status id looked up at migration run time rather than hardcoded (confirmed `sid = 1` on both the test copy and the real `smss-db`, but checked, not assumed). Confirmed rejecting a second concurrent active row under 6 simultaneous assignment attempts (exactly one survives), both on the test copy and the real database.

**Eligible teacher** = employee is `status_id` Active, `employee_type = Teaching`, current employment status doesn't block login (reuses the same check as section 29's login gating), and holds an Active login with an Active role of code `TEACHER` (role-code match, same pattern as the Employee login allow-list — the Teacher role may sit alongside other roles on the same login).

**Decided:** a class teacher who later becomes ineligible (e.g. Terminated) keeps their historical assignment row — the admin can see and replace them — but the attendance `check` answers `false` for them, and the UI dropdown shows them as "no longer eligible." Mirrors the `blocks_login` design in spirit: the live answer reflects current reality, the row stays as history.

**Decided:** no restriction on one teacher covering multiple sections — realistic for small schools, not worth a constraint until an actual school needs it. (Would be a second partial unique index if ever required.)

### Endpoints (`/api/ClassTeacher`, School Admin only except `/check`)
- `POST assign` — assign/reassign in one transaction (old row → Inactive, new row → Active). Assigning the same teacher again is a no-op.
- `POST unassign` — marks the active row Inactive; 404 if there isn't one.
- `GET current` — the section's active class teacher, or `200` with `null` data if none.
- `GET` (list) — every active section with its teacher or none, plus a missing-count, in one query (used for the "sections missing a class teacher" admin banner).
- `GET history` — past and current assignments for a section.
- `GET eligible-teachers` — the dropdown's source list.
- `GET check?employeeId&sectionId[&academicYearId]` — "is this employee the active class teacher of this section right now" (year defaults to current). **School Admin may ask about anyone; a Teacher may only ask about themselves** (403 otherwise). Also exposed in-process as `IClassTeacherService.IsClassTeacherAsync` — consumed by Attendance (section 31) and Leave Management (section 34).

### Frontend
Lives on the existing School Setup → Section page — no new nav item. Academic-year picker (defaults to current year), a "Class Teacher" dropdown per active section row, a "Missing" flag + count banner for unassigned sections, "Not assigned" to unassign, snaps back on a failed save. A reusable `template` column type was added to the shared table component to host the dropdown in a cell.

### Deployment
Migration `Docs/smss-class-teacher-assignments.sql` run against real `smss-db` (backup `smss-db-before-class-teacher.dump` taken first — same convention as the Employee deployment). Smoke-tested against real data: assign/reassign/history/check all confirmed correct on real sections, the partial unique index confirmed to key off the real `lut_status` Active id (`sid = 1`), a raw-SQL second-active-row insert confirmed rejected on the real database, and existing schools/users/students/employees confirmed byte-identical before/after (only `tb_users.last_login_at` changed, from the smoke test's own sign-ins) — test data created during the smoke test was fully cleaned up afterward.

### Build status — COMPLETE
Backend and frontend suites green (backend: eligible-teacher filtering, assign/reassign/unassign/history, concurrency, validation, the attendance check for self/admin/ineligible-teacher callers, 403 for non-admins — all covered; frontend: 371 tests/39 files passing, including a real bug the new specs caught and fixed — the "no class teacher" banner wasn't recalculating because it read a form control directly instead of reacting to it). Deployed and confirmed on real `smss-db`. No open items.

## 31. Attendance module (daily, class-teacher-only) — COMPLETE, DEPLOYED

Scoped deliberately narrow per the roadmap decision: daily whole-day attendance only (no period-wise marking — that depends on Timetable, section 33), Present/Absent only (no Late/Half-Day yet — a lookup table, extendable without a schema change), marked once per section per day by that section's active class teacher (section 30).

### Roster source (discovered, not assumed)
No new table was needed. A student's current class/section comes from the existing `tb_student_enrollments`: `student_id`, `academic_year_id`, `status_id`, `roll_number`, `class_section_id` (holds `tb_sections.section_id`, no FK constraint on it), unique on `(student_id, academic_year_id)` — so a student has exactly one section per year, and a mid-year section move is an update to this one row, not a new row. The active roster of a section for a year is every enrollment in that section with Active status whose student is also Active.

### `lut_attendance_status` (global lookup)
- `attendance_status_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `status_name` VARCHAR(50) — Present (1), Absent (2)
- `status_code` VARCHAR(30)
- `is_active` BOOLEAN — this table is the reference point for the project-wide `is_active` naming standard (see section 27's `lut_fee_frequency` note and section 29).

### `tb_student_attendance`
- `attendance_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `school_id`, `academic_year_id`, `section_id`, `class_id` (denormalized from the section via a composite FK, same reasoning as `tb_student_fee_dues` denormalizing `student_id` — keeps class-wise reporting off an extra join), `student_id`
- `attendance_date` DATE
- `attendance_status_id` BIGINT FK → lut_attendance_status
- `marked_by` BIGINT FK → tb_users, `marked_at` TIMESTAMPTZ
- `corrected_by` BIGINT FK → tb_users nullable, `corrected_at` TIMESTAMPTZ nullable, `correction_reason` TEXT nullable (≤500 chars) — a database check constraint requires all three together, with a non-blank reason, whenever any is set
- Unique: `(student_id, attendance_date)` — one mark per student per day, enforced at the database level as the backstop behind the application logic

### Marking — `POST api/Attendance/submit`
A whole-section, one-day batch write: `{sectionId, date, marks: [{studentId, attendanceStatusId}]}`.
- The `date` must equal the server's India-date for this endpoint — no backdating through this path (that's what correction/backfill is for).
- Caller must be the section's active class teacher for that date's academic year (`IClassTeacherService.IsClassTeacherAsync` — a Terminated or reassigned-away teacher is correctly refused), or School Admin.
- The submitted list must cover **every** currently active student in the section's roster — a missing, unknown, duplicate, or since-left student id is rejected (400, naming the ids) and **nothing is written**. No silent defaulting.
- Resubmitting the same section+date today **overwrites** the existing marks (upsert) — this is the entire "same-day edit" mechanism; there's no separate edit endpoint.
- Section-day writes are locked during the transaction, with the unique constraint as a second backstop. Confirmed under load: 6 simultaneous submits for one section+day all return 200 and leave exactly one consistent set of rows (never a mix of two submissions); concurrent submits for different sections don't interfere; a student moved to another section mid-write correctly gets 409 with nothing written.

### Correction / backfill — `PUT api/Attendance/correct` (School Admin only)
One endpoint handles both: if the student already has a mark for that day, it's **changed**; if not, one is **created** (a backfill for a day the class teacher never marked at all). The response's `created` flag says which happened.
- Requires a non-blank reason, ≤500 characters, always.
- No time limit — correcting or backfilling 45 days back is the same as today, consistent with the fee-payment-void precedent (section 28): the audit trail is the control, not a lockout window.
- A backfilled row gets the student's section/class/year from their enrollment as of that date, and must fall on or after the student's enrollment date (added as a rule beyond what was originally specified — prevents "attendance" existing before a student had even joined; the error names the actual enrollment date).
- A student who has since left the school can still be backfilled, so their historical record stays completable.
- Repeating a backfill with the **same** status is rejected 400 ("already marked"); a **different** status on an already-marked day behaves as an ordinary correction.
- **A same-day admin correction or backfill blocks that section's class teacher from resubmitting for the rest of the day (409)** — deliberate: a full-section resubmit would silently overwrite the admin's entry, so it's refused instead. The teacher can still view the day (both marks show as corrected) and the admin finishes marking the rest of the section via further backfills if needed. For past days, this never arises since the teacher can no longer submit for them anyway.
- Confirmed under load: 6 simultaneous backfill attempts for the same student+day leave exactly one created row, the rest correctly rejected as "already marked," no server errors.

### Reads
- `GET section/{id}?date=` — the roster with each student's mark for that date, or unmarked; shows a student who has since left but still has a mark for that day. Readable by the section's class teacher or School Admin.
- `GET student/{id}?from=&to=` — a student's attendance history (max 366-day range), with a per-status summary (keyed by status code, so a future status needs no code change) and a present-percentage figure. Readable by the student's class teacher or School Admin.
- `GET missing?date=&academicYearId=` — active sections that have students but no attendance rows yet for that date (School Admin only) — note: this has no concept of holidays/non-working days yet (section 21/32 calendar isn't wired in), so it will list every calendar day including Sundays until this gets explicitly connected.
- `GET my-sections` — the sections the calling teacher can mark today (feeds the teacher's own marking screen).

### Frontend
`/attendance/mark` — a "Mark Attendance" nav item for Teacher and School Admin. Every student defaults to Present; tapping flips a student to Absent (fastest path for the common case, since most students are present most days); a single Submit sends the whole section. Reopening an already-marked day shows the saved state and the button reads "Update attendance." A "Mark all present" shortcut exists. Built so a future status (e.g. Late) slots into the same tap-cycle with no code change. No admin correction screen, no "who hasn't marked" screen, and no Student/Guardian attendance screens exist yet — the endpoints are ready for them, the UI isn't built (see sections 7, 13, 14, 15).

### Deployment
Migration `Docs/smss-attendance.sql` run against real `smss-db` (backup `smss-db-before-attendance.dump` taken first). Smoke-tested against real data: real roster/roll-order/active-student filtering confirmed correct against real enrollments; a temporary stand-in teacher was created, assigned, tested, and fully removed afterward (the real class teacher's password wasn't known/reset for this test); submit, resubmit, correction, and backfill all confirmed working against real student rows, with all test-created data cleaned up afterward; existing data confirmed byte-identical before/after.

### Build status — COMPLETE
Backend suite covers roster shape, submit success/resubmit/rejection cases, authorization, concurrency, database-level constraint enforcement, correction/backfill rules, and read access/error cases. Full backend suite passes, 0 failures. Frontend: 384 tests/40 files passing, ng build clean. Deployed and confirmed on real `smss-db`. No open items — everything explicitly out of scope (admin UI screens, Student/Guardian views, holiday awareness) is deferred future work, not a gap in what was asked for.

## 32. Academic Calendar module — COMPLETE, DEPLOYED

Section 21's School Calendar, built. Scoped deliberately simple per the roadmap decision: no audience targeting (every role sees every event), a fixed global category list (not school-customizable).

### `lut_event_category` (global lookup)
- `event_category_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `category_name` VARCHAR(50) — seeded: Holiday (#22c55e), Exam (#ef4444), PTM (#f59e0b), Staff Meeting (#3b82f6), School Event (#8b5cf6), Other (#6b7280)
- `category_code` VARCHAR(30)
- `color_hex` VARCHAR(7) — database check constraint requires `#rrggbb` format
- `is_active` BOOLEAN

### `tb_school_events`
- `event_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `school_id` VARCHAR(100) FK → tb_schools
- `academic_year_id` BIGINT FK → tb_academic_years — **every event must fall entirely within one academic year** (decided: keep this constraint; a holiday spanning two academic years would need two events — a rare enough boundary case that splitting is an acceptable workaround rather than relaxing the rule)
- `event_title` VARCHAR
- `start_date` DATE, `end_date` DATE (defaults to `start_date` if omitted; database rejects `end_date < start_date`)
- `is_all_day` BOOLEAN
- `start_time` / `end_time` TIME, nullable — a database check constraint requires both present together only when `is_all_day = false`, and on a single-day timed event requires `end_time > start_time`
- `event_category_id` BIGINT FK → lut_event_category
- `description` TEXT, nullable
- `created_by` BIGINT FK → tb_users
- `status_id` BIGINT FK → lut_status — soft delete, no hard delete

### Authorization
`CanManageEvents()` — School Admin and Principal (write access); every signed-in role in the school can read (no audience targeting in v1). This is the first module where Principal got a real write permission outside Fees' read-only case (section 25) — deliberately a small named helper, not an overload of `IsSchoolAdmin`.

### Endpoints (`/api/SchoolEvent`)
- `POST` / `PUT` / `DELETE` (soft, via `status_id`) — `CanManageEvents` only.
- `GET ?from=&to=[&academicYearId=]` — events overlapping a date range (max 400 days), for the calendar grid. If `academicYearId` is omitted, the server resolves it from `start_date`.
- `GET /upcoming?take=1-50` (default 5) — the next events from today, soonest first; an event already in progress is included and flagged `isOngoing`.
- `GET /categories` — the category list with colors, for the form and legend.

### Frontend
`/calendar` — nav item right after Dashboard, visible to every school role (School Admin, Principal, Teacher, Accountant, Student, Guardian — not Super Admin or a custom role). Month view (6-week grid, **Sunday-first** — distinct from Timetable's Monday-first week, section 33, both correct for their own context) with colored event chips, multi-day events shown on every day they cover, a "+N more" affordance on busy days, a color legend, an upcoming-events side panel, and an event details popup. School Admin/Principal additionally get Add/Edit/Delete, with an all-day toggle that shows/hides the time fields and a category picker showing each category's color. Week/day/list views were explicitly deferred — month view plus the upcoming list covers the real need for v1.

### Deployment
Migration `Docs/smss-school-events.sql` run against real `smss-db` (backup `smss-db-before-school-events.dump` taken first). Smoke-tested against real data: a real event created by School Admin, confirmed visible to Teacher/Student/Accountant/Guardian logins as read-only, confirmed write-rejected for those roles, existing data confirmed untouched.

### Build status — COMPLETE
Backend suite covers creation/validation (dates, times, category state, academic-year containment, length limits), update (including all-day↔timed switching), database-level constraint enforcement, write authorization (Principal can write, Teacher/Student/Accountant/Guardian get 403, cross-school 404), soft-delete behavior, and read behavior (uniform across roles, boundary-correct overlap queries, ordering, upcoming-list edge cases). Full backend suite passes, 0 failures. Frontend: 419 tests/43 files passing, ng build clean. Deployed and confirmed on real `smss-db`. No open items — audience targeting and week/day/list views are explicitly deferred, not gaps.

## 33. Teacher Timetable module — COMPLETE, DEPLOYED

Section 19's Timetable, built. Depends on the existing school-owned Subject masters (`tb_subjects`, `tb_class_subjects` — found already built from earlier Academic Setup work and reused as-is, not rebuilt) and reuses the Class-Teacher Assignment's (section 30) history/reassignment and composite-FK patterns directly.

### `tb_periods` (school-owned "bell schedule")
- `period_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `school_id` VARCHAR(100) FK → tb_schools
- `period_number` SMALLINT
- `start_time` / `end_time` TIME
- `status_id` BIGINT FK → lut_status
- Unique: `(school_id, period_number)` — enforced regardless of active status (consistent with how uniqueness is enforced project-wide, e.g. `tb_fee_structures`), so a period number can't be reused while an inactive row with that number still exists.
- Not academic-year-scoped — a school's bell schedule rarely changes year to year. Managed at `/school-setup/periods`, School Admin only. Guards: an Active period can't be deactivated while timetable entries reference it as Active, and two Active periods can't have overlapping time ranges.

### `tb_timetable_entries`
- `timetable_entry_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `school_id` VARCHAR(100) FK → tb_schools
- `academic_year_id` BIGINT FK → tb_academic_years
- `class_id`, `section_id` — composite FK to `tb_sections(section_id, class_id)`, same pattern as Class-Teacher Assignment (section 30)
- `day_of_week` SMALLINT — **1 (Monday) through 6 (Saturday); Sunday is rejected** at both the database and API level (the week is **Monday-first** here, distinct from the Calendar's Sunday-first week, section 32 — both correct for their own context)
- `period_id` BIGINT FK → tb_periods
- `subject_id` BIGINT FK → tb_subjects
- `employee_id` VARCHAR(20) FK → tb_employees (the teacher)
- `room_no` VARCHAR — free text, **not** checked for double-booking (some schools legitimately share rooms)
- `status_id` BIGINT FK → lut_status

**Reassignment reuses the Class-Teacher Assignment pattern exactly:** changing a slot's subject/teacher/room deactivates the old row and inserts a new one in the same transaction — no explicit "effective from" field, history lives in the `status_id` trail.

**Two partial unique indexes, both on Active rows, both confirmed holding under concurrent writes:**
1. One active entry per `(school_id, academic_year_id, class_id, section_id, day_of_week, period_id)` — a class/section can't have two subjects in the same slot.
2. One active entry per `(school_id, academic_year_id, employee_id, day_of_week, period_id)` — **a teacher can never be double-booked**, confirmed rejecting a conflicting assignment with 409 naming the conflicting class/section/subject, both at the application layer and as a database backstop. **Decided: keep this strict** — combined/merged-class teaching (one teacher, two sections, same slot) isn't supported for now; a real enough need for it later would be an explicit, separate exception, not a loosened constraint.

**Validation decided:** a subject must already be mapped to the section's class via the existing Class-Subject Mapping before it can be assigned to a timetable slot (keeps the timetable from scheduling a subject the class doesn't actually take) — **kept enforced**. A teacher is eligible (Active employee, `employee_type = Teaching`, employment status doesn't block login) **without needing an active login** — lighter than Class-Teacher Assignment's rule, decided as correct: being scheduled and being able to view your own schedule are separable concerns, and a school may want to build out a timetable before every teacher has a login yet.

### Endpoints (`/api/Timetable`, plus `/api/Period`)
- `/api/Period` — CRUD for the bell schedule, School Admin only.
- `POST /api/Timetable/assign` — the reassign-in-one-transaction write above, School Admin only.
- `POST /api/Timetable/unassign` — clears a slot (not originally scoped, added as a natural companion to assign).
- `GET /api/Timetable/class/{sectionId}?academicYearId=` — the full weekly grid for a section (every period × every day), School Admin only for now.
- `GET /api/Timetable/teacher/{employeeId}?academicYearId=` — a teacher's full weekly grid across all their sections. **School Admin may ask about anyone; a Teacher may only ask about themselves** (403 otherwise — same self-only pattern as Class-Teacher's `/check` and Attendance's reads).
- `GET /api/Timetable/my?academicYearId=` — convenience wrapper using the caller's own `employee_id`.
- `GET /api/Timetable/lookups` — not originally scoped, added to feed the assignment form's dropdowns (eligible subjects per class, eligible teachers).

### Frontend
- `/school-setup/periods` (School Admin) — manage the bell schedule.
- `/timetable` (School Admin) — Academic Year + Class + Section pickers, a weekly period-by-day grid; clicking a cell assigns, changes, or clears that slot.
- `/timetable/my` (Teacher) — read-only weekly grid of the teacher's own schedule, the direct build of the reference "Teacher Time Table" image.

Principal, Student, and Guardian read access is not yet built — same conservative-by-default pattern as Attendance (section 31): endpoints are ready to support it, screens aren't built yet.

### Deployment
Migration `Docs/smss-timetable.sql` run against real `smss-db` (backup `smss-db-before-timetable.dump` taken first; neither `tb_periods` nor `tb_timetable_entries` existed there before this). Smoke-tested against real data: real periods created, a real subject+teacher+room assigned to a real class/section/slot using the real Class-Subject Mapping data, the double-booking 409 confirmed against a real second section, teacher self/other-403 access confirmed, existing data confirmed byte-identical before/after. No real bell-schedule periods were kept from the smoke test (would have been fake data) — the School Admin needs to add the school's actual periods at `/school-setup/periods`.

**Process note for future smoke tests:** this deployment briefly swapped a real teacher's password hash to log in and test as them (restored and verified afterward, refresh tokens cleaned up) because the real password wasn't known. Decided: prefer creating a throwaway employee+login for this kind of test instead (as Class-Teacher Assignment and Attendance did), even though this one was reverted cleanly — lower risk if something fails mid-test. **This recurred once more on the Leave Management deployment (section 34), and again a third time before being escalated for good — see section 34's deployment notes.**

### Build status — COMPLETE
Backend suite covers period CRUD and guards, assignment success/validation (unmapped subject, Sunday, ineligible teacher), the two partial unique indexes under concurrent load (8 simultaneous assigns to one slot leave exactly one Active row; 6 simultaneous cross-section assigns of one teacher give a 200/409 mix, never two Active bookings), double-booking rejection naming the conflict, teacher self/other read access, and the full existing regression suite (attendance, class-teacher, employee, fee, first-login, events). All 0 failures. Frontend: 432 tests/46 files passing, ng build clean. Deployed and confirmed on real `smss-db`. No open items — Principal/Student/Guardian read screens are deferred future work, not a gap in what was asked for.

## 34. Leave Management module — COMPLETE, DEPLOYED

Section 18's Leave workflow, built. Two separate request tables rather than one polymorphic table — Employee and Student leave have different approval chains and different business rules, and the project has consistently kept Employee and Student as separate concepts rather than a shared "Person" abstraction. No quota/balance/accrual tracking in this version (decided deliberately: pure request/approve/reject for v1; a real quota system is its own scoping exercise if it's ever needed).

### `lut_leave_type` (global lookup)
- `leave_type_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `type_name` VARCHAR(50), `type_code` VARCHAR(30)
- `applies_to_employee` / `applies_to_student` BOOLEAN — same "applies to" discriminator pattern as the document-type master (section 29)
- `is_active` BOOLEAN
- Seeded: 8 rows — Casual Leave, Sick Leave, Earned Leave, Unpaid Leave, Other (employee-side) and Sick Leave, Personal Leave, Other (student-side). "Sick Leave" and "Other" intentionally appear twice, told apart by `type_code` and the applies-to flags — not a duplicate to clean up.

### `tb_employee_leave_requests` / `tb_student_leave_requests`
Same shape, with `employee_id` (FK `tb_employees`) vs `student_id` (FK `tb_students`) as the only structural difference:
- `leave_request_id` BIGINT GENERATED ALWAYS AS IDENTITY PK, `school_id`, applicant FK, `leave_type_id`
- `from_date` / `to_date` DATE — database check: `to_date >= from_date`; a request can span at most 366 days (matches Attendance's history-range limit); dates may be in the past (for recording leave after the fact)
- `reason` TEXT — required on **apply** as well as on reject, up to 500 characters (decided during the build, not originally scoped by me, but accepted — consistent with the project's existing required-reason conventions for Attendance corrections and fee-payment voids)
- `status` VARCHAR — Pending / Approved / Rejected / Cancelled
- `applied_by` (FK `tb_users`), `applied_at`
- `approved_by` (FK `tb_users`, nullable), `approved_at` (nullable), `decision_reason` (nullable, database-required non-blank whenever `status` becomes Rejected)

**Overlap prevention, two layers:** a database **exclusion constraint** (`ex_tb_elr_no_approved_overlap` / `ex_tb_slr_no_approved_overlap`, using PostgreSQL's `btree_gist` extension — the project's first use of a PostgreSQL extension, approved on review: standard, no external dependency, confirmed working on the real `smss-db` with superuser privilege) stops a second **Approved** leave from overlapping an existing Approved one for the same person, plus the same check at the application layer. Multiple **Pending** requests for the same person/dates can coexist — only Approved rows are checked for overlap, and only at the moment of approval. Confirmed under concurrent load: 6 simultaneous approval attempts on overlapping requests give exactly one 200 and five 409s, both for employee and student leave; a raw SQL insert of a second overlapping Approved row is rejected directly by the database.

**Cancel:** only while still Pending (409 otherwise, even for School Admin) — by the applicant, whoever recorded the request, or School Admin. Confirmed: racing 3 approvals against 3 cancels on the same request leaves exactly one winner.

### Authorization
- **Employee leave:** `CanApproveLeave()` — School Admin + Principal (same shape as `CanManageEvents`, section 25). **Nobody can decide their own request** — a Principal's leave is approved by School Admin, confirmed via 403 on self-decide. (A single-School-Admin school with no Principal could theoretically have no one to approve the School Admin's own leave — accepted as a rare edge case to revisit later, e.g. letting Super Admin break the tie, not a reason to weaken the no-self-decision rule now.) Any employee with a login can apply for themselves and view `/my`; School Admin can also apply on behalf of another employee.
- **Student leave:** approved by the student's current class teacher (`IClassTeacherService.IsClassTeacherAsync`, reused directly from section 30 — confirmed that reassigning a section's class teacher correctly shifts who can act on that section's pending requests) or School Admin. Entry (apply) is from the class teacher or School Admin's side only — no Student-facing self-apply screen exists yet (consistent with Attendance/Timetable's deferred Student screens, sections 31/33). Principal and Accountant get an empty/`❌` queue by design — only the class teacher and School Admin handle student leave, per section 18's original scoping.
- **Edge case handled:** the School Admin's own login is tied to `school_id`, not an `employee_id` — there's no employee record to attach a leave request to. `/leave/my` shows an explanatory notice for that login instead of a broken form; "Record Leave" on the admin Leave Requests page is how a School-Admin-as-person applies for actual staff.

### Endpoints
- `/api/EmployeeLeave` — `POST` (apply), `PUT /{id}/decide`, `POST /{id}/cancel`, `GET /my`, `GET ?status=&employeeId=` (the employee filter on this list is limited to people who already appear in requests, since Principal can't list all employees per section 29's conservative RBAC — an expected consequence of that earlier decision, not a new gap).
- `/api/StudentLeave` — same shape, scoped by `sectionId` instead of a free employee filter: `GET ?sectionId=&status=` is a class teacher's queue for their own section(s) (self-only, same pattern as Attendance's reads), School Admin sees all.
- `/api/LeaveType?appliesTo=` — feeds the leave-type dropdown for either side (not originally scoped, added as a natural companion).

### Leave stays separate from Attendance
**Decided, deliberately:** approving a leave request does **not** write to `tb_student_attendance` or touch `tb_class_teacher_assignments`. A class teacher marking the day's attendance sees no automatic change from an approved leave — they still mark Present/Absent manually (a UI hint showing "this student has approved leave today" could be added later as a pure frontend read, no schema change). Confirmed: the attendance row count was unchanged by the leave smoke test; class-teacher assignments only changed via explicit reassignment calls made as part of testing cross-section access.

### Frontend
- `/leave/my` — any employee with a login: apply, cancel a still-Pending request, view own history.
- `/leave/requests` — School Admin/Principal: opens on Pending, approve/reject employee leave, "Record Leave" to apply on behalf of staff, filterable list.
- `/leave/students` — class teacher's (or School Admin's) queue: record and decide student leave for their own section(s).

### Deployment
Migration `Docs/smss-leave.sql` run against real `smss-db` (backup `smss-db-before-leave.dump` taken first), including `CREATE EXTENSION IF NOT EXISTS btree_gist` — confirmed successful. Smoke-tested against real data: real employee and student leave applied/approved/rejected, the overlap 409 confirmed against a real approved leave, self-decision 403 confirmed for the real teacher, class-teacher-queue scoping confirmed, all test leave data removed afterward, existing data confirmed byte-identical before/after.

**Process note, repeated from Timetable (section 33):** this deployment again briefly swapped a real teacher's password hash to test as them (restored and verified) — a second occurrence, which is what finally made this a standing hard rule rather than a one-off reminder: every future real-db smoke test that needs to act as an existing user must create a throwaway account for that purpose and delete it afterward, exactly as Class-Teacher Assignment and the Attendance deploy did — never touch a real account's credentials, even temporarily, even when it has been reverted cleanly before. The rule has held cleanly through every deployment since (Homework, section 35; the Admission pipeline, section 36).

### Build status — COMPLETE
136 backend checks, 0 failures — overlap rejection (touching either end, fully inside, fully containing, back-to-back accepted), concurrency (overlapping approvals, approve-vs-cancel races), cancel-only-while-Pending, required-reason-on-reject (and on-apply), self-decision blocking, class-teacher-reassignment correctly shifting decide rights, and the confirmation that leave stays fully separate from Attendance and Class-Teacher Assignment data. Full regression suite (15 backend scripts including timetable and leave) passes, 0 failures. Frontend: 456 tests/49 files passing, ng build clean. Deployed and confirmed on real `smss-db`. No open items — quota tracking and a Student self-service apply screen are explicitly deferred, not gaps.

## 35. Homework module (publish + view only, v1) — COMPLETE, DEPLOYED

Section 17's Homework workflow, built with a deliberate scope cut: the first half of that workflow (Teacher publishes, Student views) only. Student submission, teacher review, feedback, and marks are explicitly **not** built — that would need a new submission table, file-upload-from-Student handling, and the first genuinely interactive Student screen in the app, and was scoped out up front rather than attempted alongside this simpler flow.

### `tb_homework`
- `homework_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `school_id` VARCHAR(100) FK → tb_schools
- `academic_year_id` BIGINT FK → tb_academic_years
- `class_id`, `section_id` — composite FK to `tb_sections(section_id, class_id)`, same pattern as Class-Teacher Assignment (section 30) and Timetable (section 33)
- `subject_id` BIGINT FK → tb_subjects
- `employee_id` VARCHAR(20) FK → tb_employees — the creating/owning teacher
- `title` VARCHAR(200)
- `description` TEXT, nullable, up to 2000 chars
- `assigned_date` DATE, defaults to today if omitted
- `due_date` DATE — database check: `due_date >= assigned_date`
- `attachment_path` VARCHAR, nullable
- `status_id` BIGINT FK → lut_status — soft delete, same hide-row pattern as every module since Class-Teacher Assignment, not a boolean

**Decided: section, subject, academic year, and owner are immutable after creation.** Update only changes title, description, and the two dates; fixing a wrong section/subject/owner means delete-and-recreate. Unlike Class-Teacher Assignment and Timetable, which preserve a deactivate-old/insert-new history because they're ongoing assignment slots, a homework row is a one-off item with no "who was it before" question worth answering — the simpler rule is the right one here.

### Teacher eligibility — reuses Timetable, not a new mapping table
`ITimetableService.IsTeachingSectionSubjectAsync(schoolId, employeeId, sectionId, subjectId, academicYearId)` — true only if that employee has an **Active** `tb_timetable_entries` row for exactly that section, subject, and academic year (any day/period counts). A cleared slot or a timetable entry from a different academic year correctly doesn't count. A Terminated-but-still-timetabled teacher isn't specially excluded here — they can't log in anyway, same reasoning Timetable (section 33) already uses for its own eligibility check.

**School Admin bypasses the timetable check**, but — **decided after the initial build report** — not the next rule:

**Both Teacher and School Admin must go through Class-Subject Mapping.** A subject must already be mapped to the section's class via the existing Class-Subject Mapping before homework can be created for it, for every creator including School Admin — kept consistent with Timetable's existing enforced rule (section 33) that a class can't get scheduled, or now assigned homework, in a subject it doesn't actually take. Failure returns 400 naming the subject and class (e.g. "Physics is not taught in Class 1. Add it to the class under Class-Subject Mapping first."). An unmapped subject, a subject mapped to no class, and an Inactive mapping are all refused — even for a teacher who has the slot in their own timetable.

**Decided: an admin-created homework row's owner must be an Active teaching employee**, but the admin's choice of owner isn't itself eligibility-checked (no requirement that the named owner actually teaches that section/subject) — the admin is accountable for who they name, same spirit as Timetable's admin-assign path.

### Ownership — stricter than eligibility
Only the **creating teacher**, or School Admin, may update, delete, or change the attachment on a homework row — **not** any other teacher who happens to also be eligible for that same section+subject. Two teachers can share a timetable slot across different weeks/terms in principle, and eligibility alone would let either create homework there, but only the one who actually created a given row can touch it afterward. Confirmed: a second eligible teacher gets 403 on update, delete, and attachment upload/remove for homework they didn't create; School Admin can act on anything.

### Attachment handling — mirrors Employee documents exactly (section 29)
Optional, single file per homework row. Private storage at `{schoolCode}/homework/{homeworkId}/attachment_<timestamp>.<ext>`, never served by direct URL. PDF and common image types only, 5 MB cap, file-signature check (not just extension) — confirmed rejecting a fake PDF, a renamed PNG, an `.exe`, an `.html` file, an empty file, and an oversized file. A second upload replaces the first and deletes the old file; removing the attachment deletes the file and the download then 404s. Download re-checks the caller's visibility into the homework row itself (creating teacher, School Admin, or a student currently enrolled in that section) — confirmed 403 for a student in another section, another teacher, or an ineligible teacher, 401 with no token, 404 cross-school. There's no original-filename column, so downloads are named `homework-{id}.<ext>` rather than the file's original name — accepted as a minor, deferred follow-up, not worth a schema change for v1.

### Endpoints (`/api/Homework`)
- `POST` — create. School Admin (any Active section/subject, subject-to-class mapping still enforced) or an eligible Teacher (per the checks above). 400 on bad dates/lengths/unmapped subject, 403 on an ineligible teacher.
- `PUT /{id}` — update (title/description/dates only). Creating teacher or School Admin only.
- `DELETE /{id}` — soft delete via `status_id`. Same authorization as update.
- `GET /{id}` — single item (not originally scoped, added for convenience).
- `GET /my?sectionId=&subjectId=&from=&to=` — Teacher: homework **they created**, not everything they're eligible for; `from`/`to` filter on `due_date`.
- `GET ?sectionId=&academicYearId=&from=&to=&subjectId=` — School Admin: full list across the school, filterable (the `subjectId` filter wasn't originally scoped, added as a natural addition).
- `GET /section/{sectionId}?academicYearId=&from=&to=` — School Admin only (not Teacher) — every homework item in a section regardless of who created it, for an admin overview distinct from a teacher's own `/my` list.
- `GET /student/my?from=&to=` — Student: homework assigned to their own current section (same roster-sourcing as Attendance, section 31, via `tb_student_enrollments`), across all subjects, read-only, soonest due first. An Inactive enrollment or Inactive student sees nothing; moving sections changes what's visible; previous-year homework is hidden. No submission fields anywhere in this payload.
- `POST /{id}/attachment`, `POST /{id}/attachment/remove` — not originally scoped as separate actions, added to make attachment replace/remove explicit rather than folding it into the main update.
- `GET /attachment/{id}` — authenticated download, visibility rule as above.

**Role helper:** `CanManageHomework()` — School Admin or Teacher, same shape as `CanManageEvents()` (section 25) — layered underneath with the eligibility and ownership checks described above. Principal, Accountant, and Guardian get nothing from this module.

### Frontend
- `/homework` — Teacher and School Admin: filtered by Academic Year, Section, Subject; add/edit/delete, attachment upload/download. A Teacher's section/subject choices are pulled from their own `/api/Timetable/my` data rather than duplicating that query.
- `/homework/my` — Student: a read-only card list sorted by due date, with an overdue/due-today/upcoming distinction computed client-side by comparing `due_date` to today (same style as Attendance's present-percentage computation) — no new status column needed.

### Deployment
Migration `Docs/smss-homework.sql` run against real `smss-db` (backup `smss-db-before-homework.dump` taken first). The real school had only one real teacher and no real timetable entries at deploy time, so the smoke test used three throwaway teachers (two eligible for different subjects, one ineligible) against the real Class 1-A and real Mathematics, plus two throwaway students (one in the real section, one in a temporary section) — none of it touching any real account's password, per the hard rule from section 34. Confirmed: eligible-teacher 201, ineligible-teacher 403 naming the subject and class, cross-subject-same-section 403, School-Admin bypass of the timetable check (but not the mapping check — an unmapped subject was refused for the admin too), ownership 403 for a different eligible teacher on update/delete/attachment, attachment upload/download/visibility exactly as designed, student reads scoped to their real section, soft delete removing a row from every list/read/download. Existing data fingerprinted across all 56 pre-existing tables (excluding refresh tokens and the new `tb_homework`, ignoring only `updated_at`/`last_login_at`) and confirmed byte-identical before/after; every throwaway account, enrollment, section, and file created for the test was deleted afterward.

### Build status — COMPLETE
94 backend checks on the test copy (up from 89 before the Class-Subject Mapping fix), 0 failures — eligibility, ownership, date/length validation and boundaries, attachment validation/visibility, student section-scoped reads, soft delete, and the Class-Subject Mapping enforcement added after my review (unmapped-for-admin, mapped-to-no-class, Inactive mapping, removed mapping — each refused). Frontend: 479 tests/52 files passing, ng build clean. Deployed and smoke-tested on real `smss-db`, 0 failures, existing data confirmed untouched. No open items for v1 — original-filename storage, pagination (once a school accumulates a year of homework), and Principal/Guardian read access are explicitly deferred follow-ups, not gaps.

## 36. Admission pipeline — COMPLETE, DEPLOYED (extended, not newly built)

**This is not a new module.** A real admission pipeline — `tb_admissions` (application number, review, approve/reject, a Confirm Admission step that creates the student and assigns a fee structure), live frontend at `/admissions`, 2 real ADMITTED applications — already existed in the codebase before this round. This doc previously, incorrectly, said Admissions (section 10) "is not yet built as a distinct module." That was wrong and has been corrected throughout this doc. The lesson for future sessions: verify the real repo/screens before designing a module from this doc's own "not yet built" claims, especially for anything that's been part of the project a while — this doc can go stale exactly like any other piece of documentation.

I drafted an initial build prompt for a brand-new parallel pipeline before this was caught (by Claude Code's own pre-build investigation, which surfaced the existing `tb_admissions` table and screens before anything got built). Chose to **extend** the existing pipeline rather than build a competing one or build-parallel-then-retire — matches the project's "don't create competing versions of the same concept" rule.

### What already existed (not re-documented in detail here — real schema, not redesigned)
`tb_admissions`: application number (format `ADM{year}NNNN`, restarting each calendar year — 2 real rows exist as `ADM20260001`/`ADM20260002`, both ADMITTED), applicant/guardian intake fields, class applied for, admission type, status (Submitted/Under Review/Approved/Rejected/CANCELLED — Withdrawn uses the pre-existing CANCELLED status, not a new one), review and decision fields, and a **Confirm Admission** step that creates the `tb_students`/`tb_student_enrollments` rows and assigns a fee structure in one action — this is the module's one and only conversion path (see "Decided" below).

### What this round added
- **New `ADMISSION_STAFF` role**, added to `lut_roles` (role id 10 on the real DB) and to the Employee login allow-list (section 29) — an admission-staff employee can now get their own login and run the pipeline, rather than School Admin being the only possible user.
- **Two-tier authorization, new:**
  - `CanManageApplications()` — School Admin, Principal, Admission Staff. Covers create, edit (only while Submitted/Under Review — refused after a decision), move to Under Review, document upload/remove, and all reads.
  - `CanDecideAdmission()` — School Admin, Principal **only**, not Admission Staff. Covers Approve, Reject, and **Confirm Admission** (now gated by this tier too — previously same-school-only, see the security fix below). Admission Staff can run the whole intake/review flow but never makes the final call or converts an Approved application into a student, mirroring how Leave Management (section 34) already keeps "apply" and "decide" as separate powers.
  - A decision requires Under Review first (skipping straight from Submitted is refused, naming the missing step); a decided application can't be edited, withdrawn, or decided again; Reject requires a non-blank reason; both the reject-reason and the "ADMITTED implies a linked student" rules are enforced at the database level, not just in code.
- **New `tb_admission_application_documents` table**, built fresh rather than reusing the older `tb_admission_documents` (which belongs to the pre-existing direct student-creation flow and was left untouched) — mirrors the Employee/Homework documents pattern: private storage, file-signature check, PDF/image up to 5 MB. Frozen (409 on upload) once an application reaches Rejected, Withdrawn (CANCELLED), or Admitted. The document-type master's "applies to" flag only supports STUDENT/EMPLOYEE/BOTH (no ADMISSION value), so admission documents use STUDENT/BOTH Active types — the real DB has one (Aadhaar Card, BOTH), which is sufficient; no change was made to the document-type master itself.
- **Application-number concurrency fix:** numbers were previously max-plus-one with retry-on-collision and no lock. Now uses the same per-school advisory-lock pattern already in use for fee receipt numbers (section 28), with the unique index kept as a backstop. Confirmed: 8 simultaneous submissions produce 8 distinct, gapless numbers.
- **A real, pre-existing security gap, found and closed:** both ways of creating a student record (the admission-confirm path and the separate "Add Existing Student" path) previously checked only same-school membership — **any** logged-in user of the school, including a Teacher or even a Student account, could call Approve, Reject, Confirm, or create-student directly; only the Angular route guard stopped them in the UI. Every Admission endpoint now sits behind one of the two tiers above.

### Decided — the single conversion path
An early draft of this round's build added a second, parallel way to turn an Approved application into a student: a pre-fill screen that opened the existing "Add Student" form with the application's data filled in, plus its own `prefill`/`mark-converted` endpoints. **Rejected and removed** once built: Confirm Admission already existed, was already tested, already in use for both real admitted students, and — unlike the new screen — already assigns a fee structure as part of conversion. Two ways to do the same thing, one of them strictly weaker, is exactly the "competing versions of the same concept" the project rules warn against. The prefill screen, its route, and its `mark-converted`/`prefill` endpoints were deleted before deploying; Confirm Admission is the only conversion path, now properly gated by `CanDecideAdmission()`.

### Student-endpoint hardening (scoped, not a full RBAC redesign)
Closing the security gap above meant touching `StudentController`'s list/get/photo/create-login endpoints, which had no role check at all beyond same-school. Decided to scope this narrowly rather than attempt the still-deferred project-wide RBAC pass (section 25):
- **Student and Guardian login types are now blocked entirely (403)** from the class list, any student's record (including their own), photo upload/remove, create-login, and direct student creation — closing the specific gap that mattered (a Student account reading or acting on another student's — or even their own — record through this surface).
- **Writes (create, photo upload/remove, create-login, direct Add Existing Student) now require School Admin specifically** — matching how every other module in the project gates its writes.
- **Reads for School Admin/Principal/Teacher/Accountant/Admission Staff are unchanged** — still as broad as before (Accountant's existing limited-view logic, section 28, is untouched). Deliberately did **not** attempt to scope Teacher down to "their own students" — that concept doesn't exist anywhere in the app yet, and designing it now would be new scope, not a gap-close.

### Endpoints (under the existing `api/Admission/{schoolId}/...` routes — not renamed)
Create, edit, review, decide (approve/reject), withdraw, list/filter, Confirm Admission, and the four new document endpoints (list, upload, download, remove) on `tb_admission_application_documents`. `mark-converted` and `prefill` do not exist — see "Decided" above.

### Deployment
Backup `smss-db-before-admission-pipeline.dump` (250 KB) taken first. Migration `Docs/smss-admission-pipeline.sql` ran clean — adds the Admission Staff role, four new columns on `tb_admissions`, the new application-documents table, and the new database checks; `reviewed_by`/`approved_by` on the two real pre-existing rows point at a user that no longer exists, so those FKs were added `NOT VALID` rather than forcing a data fix. Smoke-tested against real data: the two real applications (`ADM20260001`, `ADM20260002`) still list and read correctly, linked to `APS001ST001`/`APS001ST002` and their enrollments; Admission Staff confirmed able to manage but not decide; a Teacher, Accountant, and Student account each confirmed 403 on Confirm Admission (previously any of them could); a throwaway Admission Staff login successfully confirmed a throwaway application into a real Class 1-A student; the Student-endpoint hardening confirmed blocking a Student account from the class list, another student's record, its own record, and every write, with reads unchanged for every staff role; document upload/download/visibility and the frozen-after-decision rule all confirmed; 8 simultaneous application submissions gave 8 gapless numbers. Existing data fingerprinted across all 56 pre-existing tables and confirmed unchanged except the expected `lut_roles` row count (9→10) and the two real applications' untouched `updated_at` values. Every throwaway account, application, and uploaded file created during the test was deleted afterward — the hard rule from section 34 (no real account's credentials touched, even temporarily) held here too.

### Build status — COMPLETE
87 pipeline checks on the test copy, 0 failures — both authorization tiers (including 403 on every endpoint for Teacher/Accountant), status-transition rules (skip-review, double-decide, edit/withdraw-after-decision, missing rejection reason), application-number concurrency, document validation/download authorization, and the Confirm handoff under concurrency (6 simultaneous saves of one application producing exactly one student). Full backend regression (17 scripts) passes, 0 failures — including two pre-existing tests fixed along the way (one expected the old, narrower role list; one had a fragile text-based id sort that broke once the new role existed). Frontend: 511 tests across 55 files pass, `ng build` clean. Deployed and smoke-tested on real `smss-db`, 0 failures, existing data confirmed untouched beyond the expected role-count change. No open items — the Enquiry/lead stage and auto-conversion-without-review were deliberately out of scope from the start (see section 10), not gaps.

**Status check:** Class-Teacher Assignment, Attendance, Employee Management Stage 1, Academic Calendar, Teacher Timetable, Leave Management, Homework, and the Admission pipeline are all now complete and deployed (the last one extended, not newly built — see above). Remaining candidates from the original roadmap discussion: Internal Assessment/Class Tests, or Notifications.
