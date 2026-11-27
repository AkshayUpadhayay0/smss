# Record leaving (Stage 8a, 26 Nov 2026)

Decision of 26 Nov 2026 (kept from 4b): **`tb_students.status_id` is not changed** when a student passes out or leaves. "Left" is derived from the outcome of the enrolment. A student who is set Inactive by other means is treated like a leaver at login.

## What it writes
`tb_student_enrollments` (script `Docs/smss-st-8a-RUN-ME.sql`, run after 4b-1; idempotent, nothing existing changes): `outcome` LEFT or TRANSFERRED_OUT, `outcome_at`, and the new `leaving_date`, `leaving_reason`, `outcome_by`, `leaving_override_reason`, `leaving_outstanding`. A new table `tb_student_leaving_log` holds one audit row per leaving and per correction. No new enrolment is created.

## Endpoints (`StudentLeavingController`, School Admin and Principal only; everyone else 403; other school's student 404)
| Endpoint | What |
|---|---|
| `GET api/StudentLeaving/check?studentId&leavingDate` | read-only: what would happen, the dues up to that date (`outstanding`, `dues`), blockers |
| `POST api/StudentLeaving/record` | `{studentId, type: LEFT\|TRANSFERRED_OUT, leavingDate?, reason, confirm: true, overrideDues?, overrideReason?}` |
| `GET api/StudentLeaving/correction/check?studentId` | can the leaving be corrected, and if not why |
| `POST api/StudentLeaving/correction` | `{studentId, reason (10+ chars), confirm: true}` |
| `GET api/StudentLeaving/leavers?academicYearId` | the students recorded as left (newest first, at most 500) |

Rules: the leaving date is not in the future and not before the enrolment date; the year must not be closed; a student who already has an outcome is a 409. **Dues:** unpaid dues of the enrolment's year due on or before the leaving date block it unless `overrideDues` is true with an `overrideReason` of at least 10 characters (same rule as promotion); the amount and the reason are stored on the enrolment and in the log. **Correction:** only for a leaving recorded here (an outcome written by a promotion run is corrected on the Promotion screen), only while no later enrolment exists, never in a closed year; once certificates exist (8b) also not while one is issued. A promotion correction is refused while the new-year enrolment already carries a leaving.

## Login after leaving (`LeavingRules.StudentLoginBlocked`, checked at sign-in AND at refresh)
Refused with "Your account is no longer active. Please contact your school." when the student is Inactive, or the latest enrolment ended LEFT / TRANSFERRED_OUT, or PASSED_OUT once its year is no longer current. Recording a leaving also revokes the student's refresh tokens in the same transaction (the 15-minute access token simply expires). A guardian is not affected: the child shows with the read-only LEFT chip and every parent endpoint is a GET.

## The ONE live-enrolment rule (`LiveEnrolment`)
Live = Active enrolment whose outcome is not LEFT or TRANSFERRED_OUT, and not PASSED_OUT once its year is no longer current. One definition in three forms (C#, an EF expression, SQL text) pinned together by a test. Used by: the attendance roster and the two "sections to mark" counts, the class-test roster, the marks-entry roster, the class student list, the students-per-section count, both dashboards' enrolled-student counts, the teacher dashboard's unpaid-dues count, the fee dashboard's class student list, "assign fees for a year", the pending-parent-details list and its count, the pending-documents list and count, ID-card rows, the fee-reminder push recipients, and the subscription billing counts (a leaver stops counting at the leaving date, which can never be in the future).

Deliberately NOT changed (history or money): old-year views and enrolment history, attendance and marks reports, published report cards, the roll-number uniqueness, the sibling search, receipts and the late-fee reports, and the **fee totals** (a leaver's unpaid dues remain owed and visible on the fee dashboard totals).

# Transfer certificates (Stage 8b, 26 Nov 2026)

Script `Docs/smss-st-8b-RUN-ME.sql` (after 8a; three new tables, nothing existing changes): `tb_transfer_certificate_sequences`, `tb_transfer_certificates`, `tb_transfer_certificate_copies`.

| Endpoint (`api/TransferCertificates`, School Admin and Principal only; other roles 403; another school 404) | What |
|---|---|
| `GET check?studentId` | can one be issued, the dues up to the leaving date, what stops it, any certificate already on this leaving |
| `POST issue` | `{studentId, conduct?, remarks?, confirm: true, overrideDues?, overrideReason?}` |
| `GET /` (`studentId`, `status`), `GET {id}` | list; one certificate with every print (who, when) |
| `GET {id}/pdf` | the original, drawn again from the stored snapshot (audited) |
| `POST {id}/duplicate` | a duplicate: same number, marked "DUPLICATE COPY No. N" (audited; a POST because it writes the audit row) |
| `POST {id}/void` | `{reason (10+ chars), confirm: true}` |

- **Who/for whom:** only a student whose LATEST enrolment has the outcome LEFT, TRANSFERRED_OUT or PASSED_OUT. One ISSUED certificate per leaving (a unique index); a void one does not count, so a reissue is a new number.
- **Number:** `<school code>/TC/<academic year of the enrolment>/<4 digits>`, taken under a per-school advisory lock with an atomic +1 on a counter that never goes down (per school and year label): a void number is never handed out again.
- **Immutable:** everything printed is stored as a JSON snapshot at issue time; every later copy is drawn from it only (profile, school and principal edits cannot change it).
- **Dues:** the same check and override as leaving (reason of 10+ characters stored on the row, never printed); the PDF prints only "Fee dues: Cleared" or "Not cleared".
- **Void:** the row and number stay; a void certificate prints nothing and cannot be duplicated. The 8a leaving correction is refused while an ISSUED certificate exists.
- **PDF:** QuestPDF, drawn on demand and sent only to a signed-in School Admin or Principal (`Cache-Control: no-store`); nothing is stored under a public address.
- **Web:** `/transfer-certificates` (sidebar: a top-level "Transfer Certificates" item right under Promotion; no existing menu group was changed).

## What a CBSE / ICSE transfer certificate asks for that the school profile or student record does not hold
School: affiliation number and board affiliation date, school (UDISE) code, the board's school code, recognition/registration number. Student: nationality, religion and caste category (SC / ST / OBC / General), father's and mother's names as separate fields (the certificate takes them from the guardian relationship when set), class in which first admitted, subjects studied, whether qualified for promotion to the higher class (and to which class), total working days and days present, fee concession availed, NCC / Scout / games and extracurricular record, date of application for the certificate, general conduct as a board-prescribed wording, Aadhaar or APAAR/PEN number (boards differ). A certificate with these needs those fields added to the school profile and the student record first; today the certificate prints only what exists.

# Mid-year section change (Stage 8c, 26 Nov 2026)

Script `Docs/smss-st-8c-RUN-ME.sql` (one new table, `tb_student_section_changes`; nothing existing changes). Decision: **section only, option 1**: the single enrolment is kept and its section and roll number are updated in place; every change leaves a history row. A class change is out of scope.

| Endpoint (`api/SectionChanges`, School Admin and Principal only; other roles 403; another school's student 404) | What |
|---|---|
| `GET check?studentId` | the sections the student can move to (same class, offered by the same year, active), each with strength, live count, full or not and the next free roll; and what stops it |
| `POST /` | `{studentId, toSectionId, effectiveDate?, reason (10+ chars), rollNumber?, confirm: true}` |
| `GET history?studentId` | every change of this student, newest first |

- **Rules:** the enrolment must be live (the shared rule) and the student Active; the year must not be closed; the target must be another section of the same class offered by the same year; capacity (`max_strength`) is counted on live enrolments, so a leaver frees a seat; the effective date is not in the future and not before the enrolment; the roll is typed or the next free number (above every numeric roll in the section, a leaver's included) and unique per year and section as before (`fn_roll_key`).
- **Concurrency:** one section change at a time per school (a transaction-scoped advisory lock), and the update is guarded by the section it moves from. Two changes into a section with one seat: one succeeds, the other is a 409. Two at once get two different rolls.
- **Untouched:** class, fees, fee items and dues, transport, attendance rows, report marks and report rows (they stay under their old section).
- **Follows the new section:** the roster and class lists, class-teacher access, homework, timetable, the ID card (all read the enrolment's section).
- **Refused, with a plain message (the one case that cannot be made safe):** the student has a **published** report card in that academic year. A published card belongs to the old section's publication; republishing the old section would drop it. Make the change before report cards are published.
- **Fixes this needed (found by the tests):** the report engine and the report entry screens read a student's activity grades, term inputs and year inputs by SECTION, which would have hidden everything entered under the old section; they now also include the students currently enrolled in that section. A student's own class-test results were limited to the CURRENT section; they now follow the student's own mark rows, so tests taken before the move stay. Ranks are among the section's students, so a moved student is ranked in the new section.
- **Attendance after a move (fixed 27 Nov):** the monthly attendance report lists a moved student in BOTH sections' reports for the days they were there (from the attendance rows' own section and the history), and the report card's AUTO term attendance (and the class teacher's attendance preview) counts the days marked in the student's other section(s) too, once per day. Published cards are frozen copies and are never recomputed.

# Web screens (Stage 8d, 26 Nov 2026)
`/student-leaving` (record a leaving with the dues check and override, correct a leaving, "Students who left" with a year filter), `/section-change` (check, choose a section, roll, date, reason; the history of that student) and the student profile's "Section changes" card. School Admin and Principal only. The sidebar gets three flat items under Promotion: Student Leaving, Change Section, Transfer Certificates (no existing group was changed). A blocked student sees the server's "Your account is no longer active. Please contact your school." on the web login page and on the app sign-in screen. The parent screens already show the LEFT / TRANSFERRED_OUT / PASSED_OUT chip and the read-only banner for such a child (web and mobile); only tests were added there.
