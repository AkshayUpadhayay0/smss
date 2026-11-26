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
