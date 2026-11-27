# Student Year Continuity and Promotion — design record (24 Nov 2026)

Status: DESIGN ONLY. Nothing built. Agreed in discussion with the owner on 24 Nov 2026 after testing the year switch on the Little Angels demo school (LAS).
Basis: two read-only Claude Code investigations (student flow; school masters) read from code and Docs/*.sql plus the 14 Nov pg_dump. The live database was NOT queried. Mobile app NOT checked. Dashboard queries NOT traced.

## 1. The problem that was found
Switching the current year from 2026-27 to 2027-28 (no students added to the new year) left these dashboard tiles unchanged: Total students 177, Paid 10, Not paid 167, Fee collected Rs 3,55,450, Outstanding Rs 5,59,976. Only "Income this session" changed (to Rs 0). The tiles show "November 2026" under a 2027-28 banner. Years are mixed on one screen, and there is no way to move an existing student into a new year.

## 2. What is already right (do not redesign)
- tb_students is identity only (student_id, admission_number, DOB, guardian link, photo). No class, section or year. These never change across years.
- tb_student_enrollments is one row per student per year (class_section_id, roll_number, academic_year_id, status_id). uq_tb_enrollments_student_year allows exactly one per student per year.
- Fee structure, fee items, transport and attendance rows carry academic_year_id. Published report cards are immutable snapshots (jsonb). The student login (tb_users.org_user_id = student_id) survives year changes.
- Year layer for masters: tb_academic_years (one current, no overlap, close/reopen), tb_academic_year_classes and tb_academic_year_sections (per-year offering with capacity), per-year class-subject mapping, class teachers, schedules, timetables, year copy (S7, configuration only, no students).
- Tenant isolation: school id comes from the token, services validate foreign ids.
- Single tb_users table for Super Admin and school users stays (decided). Safeguards: SUPER_ADMIN has null school_id and every other type has one (DB check); school endpoints never accept a Super Admin without an explicit logged act-on-behalf path; Super Admin created only by seed or Super Admin action; Super Admin actions audited.

## 3. Decisions (24 Nov 2026)
1. Identity never changes: student_id and admission_number are generated once at admission and are never touched by promotion.
2. History is never overwritten. Promotion, repeat and leaving ADD rows. An existing enrolment is never updated to a new class, section or year.
3. Previous-year dues do NOT carry forward. A student with an outstanding balance in the source year is NOT promoted until the dues are cleared. Previous decision (earlier discussion): carry-forward was an open option. New decision: no carry-forward, block instead. Reason: owner's rule. Impact: old dues stay in the old year; fee payments still work on closed years so dues can be cleared; blocked students need a visible Pending Promotion list.
4. The dues block can be OVERRIDDEN by School Admin or Principal with a recorded reason (who, when, reason, outstanding amount at that moment). Teachers and others cannot override.
5. Concessions apply only to the year they were given. Previous: tb_student_fee_concessions unique (student_id, concession_id), no year. New: add academic_year_id; unique (student_id, concession_id, academic_year_id). Impact: migration plus change in fee structure/billing code that reads concessions.
6. Repeat (detained) = a NEW enrolment in the same class in the new year. Fits the one-enrolment-per-student-per-year rule.
7. Masters: typo-fix renames of classes and sections are allowed. Moving a section to a different class is blocked once the section has been used (enrolments, attendance, fees, report setup, timetable or homework reference it).
8. Add next_class_id to tb_classes (explicit next class; NULL = last class, students there pass out). Reason: sequence_order is not unique and is freely editable, so "next = order + 1" is only a convention.
9. The year selector (what I am viewing) is separate from "set current year" (a school-wide admin action that also changes the billed enrolment count).

## 4. next_class_id
- Column on tb_classes: next_class_id, nullable, self-reference to the same school's class (composite (school_id, next_class_id) -> tb_classes (school_id, class_id), which already has a unique key).
- Rules: a class cannot point to itself; no two classes in a school may share the same next class (partial unique on school_id, next_class_id where not null); no loops (checked in the service).
- Import Masters sets it for the catalog chain Nursery -> LKG -> UKG -> Class 1 ... -> Class 12 (Class 12 NULL). Classes a school adds itself get it set on the Class screen.
- It is only the DEFAULT. At promotion the admin may choose another target class for a student (for example skipping a class).

## 5. Stage 1 — Hardening (foundation, before promotion)
Not live and demo data only, so constraints are applied directly (earlier decision: expand-then-contract dropped). Check the live database and the demo data against each rule BEFORE adding it, and report violations.
1. tb_student_enrollments: add foreign keys. (school_id, academic_year_id) -> tb_academic_years (school_id, academic_year_id); (school_id, class_section_id) -> tb_sections (school_id, section_id); (academic_year_id, class_section_id) -> tb_academic_year_sections (academic_year_id, section_id) so a student cannot be enrolled in a section the year does not offer. All three target columns already have unique keys.
2. Roll number: partial unique index on (academic_year_id, class_section_id, roll_number) where roll_number is not null. Enrolment creation and import report a clear message on conflict.
3. next_class_id as in section 4.
4. Master guards: refuse deactivating a class, section or subject that is in use in an open year (same style as the existing withdrawal guard in Academic Setup). History screens must still show inactive masters. Block section move to another class once used. Section move today returns a generic 500 through composite FKs; it should return a clear 409.
5. Enrolment creation (admission, direct entry, import) must check the section is offered in that year.
6. Past years selectable: the student list dropdown and other year selectors list ALL years (closed ones marked read-only), not only Active ones.
7. Year-scoped screens: dashboard tiles (Total students = enrolled in the selected year, fee tiles = dues of that year's fee items, "this month" inside the year's date range with an empty state when no fee structure exists), receipts use the enrolment of the PAYMENT's year (today they use the current-year enrolment), student profile returns all enrolments as a list with a tab per year. Remove or log the default-to-current-year fallbacks one by one.
8. Receipts snapshot the fee type name at payment time (tax and concession names are already snapshotted).
9. Per-year concessions (decision 5).
10. Minor: Import Masters offers classes only to the current year and always creates section "A"; a new class or section is auto-offered only in the current year, not in an already-created future year. Note and decide later.

## 6. Stage 2 — Promotion
Order for a new school year: create the year -> run year copy (offerings) -> promote students.
Preview (runs the real code and rolls back) then execute (needs a confirm flag), per source section and target year.
Preconditions: target year Active and not closed; target class and section offered in the target year; source enrolment present and Active; no enrolment already exists for the student in the target year (skip-if-exists, never overwrite; a second run creates nothing).
Per student the admin sees a decision, pre-filled from the class teacher's tb_report_year_student promotion_decision where it exists (PROMOTED -> promote; DETAINED / NOT_PROMOTED -> repeat), and can change it:
- PROMOTE: new enrolment in the class given by next_class_id (admin may pick another). Target section suggested by the same section name, admin may change. Capacity checked against the target year's per-section max_strength.
- REPEAT: new enrolment in the same class in the new year.
- PASS OUT / LEAVE: no new enrolment (see Stage 3 statuses). Last class (next_class_id NULL) can only pass out or repeat.
Dues check per student: sum over the source year's dues of (amount_due - amount_paid). If greater than zero the student is BLOCKED and appears on the Pending Promotion list with the amount. A School Admin or Principal may override with a reason (stored). After overriding, promotion proceeds normally; the old dues stay in the old year.
Roll numbers: assigned in the new section (next free number, or admin-entered), never copied; the unique rule from Stage 1 applies.
Execution: one transaction, per-school advisory lock (the project's existing pattern), writes a batch audit row plus one row per student (decision, target, blocked/overridden, reason, actor, time). The old enrolment gets its outcome status; no column of the old enrolment other than status is changed.
Fees: after promotion the existing FeeAutoAssignment (finds the active fee structure for class + year) assigns the new year's fees, as it does at admission. Fee structures for the new year must exist first; copying them from the previous year is still the top follow-up in the academic-year doc.
Pending Promotion list: students with no enrolment in the new year because of a block or no decision yet; visible to School Admin and Principal so nobody disappears from all rosters.
Subscription billing counts current-year Active enrolments; promotion into the new year changes the billed count (already documented).

## 7. Stage 3 — Status, leaving, history
- Yearly outcome lives on the ENROLMENT, not on tb_students. Proposed values (finalise in Stage 3 design): ACTIVE, PROMOTED, REPEATED, PASSED_OUT, TRANSFERRED_OUT, LEFT. tb_students.status_id stays the overall lifecycle only. Today nothing ever changes either status, and setting a student Inactive removes them from every roster, including past years, so history readers must not filter on Active.
- Transfer certificate / leaving workflow (only a document-type master exists today). Decide whether a student with dues can receive a transfer certificate.
- Student edit endpoint (none exists today).
- Mid-year class or section change: uq_tb_enrollments_student_year forbids a second enrolment in the same year, so a mid-year change needs a section-change history table, not a second enrolment. Design before building.
- Student profile page: tab per year (class, section, roll, attendance, marks, fees, published report card), always showing the full history regardless of the selector.

## 8. Build order
1. Stage 1 hardening (including year-scoped dashboard, receipts, profile history, per-year concessions).
2. Stage 2 promotion with the dues block and override.
3. Stage 3 status, leaving and transfer certificate, mid-year section change.
4. Fee structure copy to a new year.

## 9. Rules for the implementer (Claude Code)
- Design first, stage by stage. Report what you found in the code and database, then wait for approval before building each stage.
- Verify the live database first (the investigations used the 14 Nov dump, which predates S1-S7). List any rows that would violate a new constraint before adding it.
- Idempotent RUN-ME migration files in Docs/, each verified with an ALREADY MIGRATED second run, following the S1-S7 pattern. Back up with pg_dump first. Do not push; do not commit anything not asked for.
- Never trust school_id from the client. Promotion, preview, override and pending list are School Admin / Principal only. API response structure stays the project's standard envelope.
- Tests for the promotion rules (dues block, override, repeat, last class, capacity, idempotent re-run, closed target year). Real-database smoke check with throwaway accounts; fingerprint 0 changed on existing data.
- Check the mobile app (smss-mobile) for student and year flows; it was not covered by the investigations.

## 10. Still open
- Whether a transfer certificate is blocked by outstanding dues.
- Final enrolment outcome values.
- Whether Import Masters should accept real section names instead of always creating "A".
- Whether the dashboard "this month" should follow the calendar or the selected year's range when today is outside the year.
