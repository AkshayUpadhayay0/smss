# Parent (guardian) read-only endpoints — Stage 6c (26 Nov 2026)

Base route `api/Family`, `[Authorize]`, every action a `GET`. **Nothing takes a guardian id, a school id or a role from the request**: the guardian is the one behind the token (`guardian_id` claim, re-checked against `tb_guardians.user_id`), the school is the token's, and a child id in the route is always checked by ONE helper, `IStudentAccessService.ResolveAsync`, called inside `IFamilyService` before any data is read.

## The access helper ("which student is this request for")
| Caller | Result |
|---|---|
| **Student login** | its OWN student record (login id = student id); another student id = **404** (never 403). The children list and the notices wrapper are **403** for a student login. |
| **Guardian login** | the requested child ONLY IF: the guardian is Active and not merged, belongs to the token's school, the token's `guardian_id` matches the guardian behind the user, the child is a student of the same school, and the link in `tb_student_guardians` has **`portal_access` = true**. Anything else (unlinked, switched off, another school, unknown id, merged / inactive guardian) = the same **404 "Student not found."** |
| any other login, or both roles | **403** |
| no school in the token | **400** |

## Endpoints
| Endpoint | Helper | Reuses (behind the helper) |
|---|---|---|
| `GET /api/Family/children` | the list itself checks the guardian the same way and lists only `portal_access` links | `IStudentRepository` (enrolments, due rows), `IAttendanceService.GetForStudentAsync` (today's chip) |
| `GET /api/Family/children/{id}/profile` | `ResolveAsync` | `IStudentService.GetProfileForStudentAsync`, **minus** the guardians list and the student's own mobile / email |
| `.../attendance?from&to` (≤ 92 days) | `ResolveAsync` | `IAttendanceService.GetForStudentAsync` (Present / Absent only) |
| `.../homework?from&to` | `ResolveAsync` | `IHomeworkService.GetForStudentAsync` |
| `.../timetable` | `ResolveAsync` | `ITimetableService.GetStudentGridAsync` |
| `.../exam-schedule` | `ResolveAsync` | `IReportScheduleService.GetAsync`, filtered to the schemes and dates of the child's own class |
| `.../report-cards`, `.../report-cards/{publicationId}`, `.../report-cards/{publicationId}/pdf` | `ResolveAsync` | `IReportResultService.CardsForStudentAsync / CardForStudentAsync / CardPdfForStudentAsync` — **published cards only** |
| `.../fees` | `ResolveAsync` | `IStudentMyFeesService.GetFeesForStudentAsync` — the own view: APPLIED late fee lines only, no concession reason, no voided payment |
| `.../receipts/{paymentId}` | `ResolveAsync` | `IFeePaymentService.GetReceiptForStudentAsync` — only an Active payment of THIS child (another child's = 404), own view |
| `.../leave?status&from&to` | `ResolveAsync` | `IStudentLeaveService.GetForStudentAsync` (read only) |
| `.../photo` | `ResolveAsync` | `IStudentPhotoService.GetAsync` (private storage) |
| `GET /api/Family/notices` | guardian only (403 for a student) | `INoticeService.GetMyAsync` with the guardian's own login (Guardian tier; school-wide, no child id) |

Each `*ForStudent*` method is the old "my" method split in two: the "my" method still resolves the student from the login and then calls it, so the existing student endpoints behave exactly as before.

## Existing "my" endpoints NOT exposed to parents
- `GET api/Student/me/fees`, `/me`, `/me/id-card*`, `GET api/Attendance/student/my`, `api/Homework/student/my`, `api/Timetable/student/my`, `api/StudentReportCard*`, `api/StudentLeave/my`: **not called by parents**; the parent uses the `Family` copy above, which goes through the helper. They stay Student-only.
- **Homework attachment download** (`GET api/Homework/attachment/{id}`): its visibility rule is by the student's section, not by a child id, so it cannot be reused behind the helper as it stands. Parents see the homework text and due date but not the attachment (a later stage can add `children/{id}/homework/{homeworkId}/attachment`).
- **Class-test results** (`api/Assessment/student/my`), the **student dashboard**, **ID card** and **my bus** (transport): not asked for in 6c; the dashboard and the ID card are built around the student's login. Not exposed.
- Leave apply / cancel (writes), and every other write: parents are read-only for now.

## Data minimisation (asserted by tests)
A guardian never receives another guardian's contact details, the student's own mobile / email, Aadhaar, internal remarks, a concession reason, a waived late fee, a voided payment, or another child's data. The response shapes of the new DTOs are fixed by a test (a field cannot be added without the test noticing).

## Student photos
Student photos are **already private**: files live in the private store and are served only by an authenticated endpoint (`GET api/Student/{id}/photo` for students and staff, and now `GET api/Family/children/{id}/photo` for a parent through the helper). The public `/uploads` URL space serves school logos only (`UploadsAllowListMiddleware`). On the real database both students that have a photo already hold a private key, none a public `/uploads/…` path. No migration, nothing to move.
