# SMSS — Examination, Results & Report Card: design brief for Claude Code

Paste this whole file into Claude Code. **This is Phase A only: investigate and return a design. Write no code, no migration, no commits.** I will review your design, then release the build stages in section 9.

Why a gate: this is the most expensive module to redo (schema, calculation rules and PDF templates all interlock), and some schools will use SMSS *only* for report cards, so it has to be right the first time.

---

## 1. Ground rules (unchanged project standards)

- Multi-tenant: every table carries `school_id VARCHAR(100)`; school always comes from the token; cross-school gives 404; wrong role 403.
- PostgreSQL conventions of this project: `BIGINT GENERATED ALWAYS AS IDENTITY`, `TIMESTAMPTZ`, soft delete via `status_id`, `tb_` business tables, `lut_` global lookups, composite FKs on `(section_id, class_id)` as elsewhere. No UUIDs.
- Named role helpers in controllers (`CanManageExams`-style), no hard-coded role ids, thin controllers, DTOs, standard response envelope, centralized error handling.
- Derived values are computed, not stored, **except** the published snapshot (section 6).
- Reuse, do not rebuild: Subjects and Class-Subject Mapping, Academic Years, Sections and Enrollments, `IClassTeacherService`, `ITimetableService.IsTeachingSectionSubjectAsync`, the Attendance monthly report's working-day rules (section 31), QuestPDF and QRCoder, the ID Card template system (section 45: code-constant template keys plus per-school settings), `IFileStorageService`, Subscription (section 39).
- The Internal Assessment module (section 37) stays separate. Do not share its tables. Importing class-test marks into exams is a possible later feature, not v1.
- Student photos are still in public storage (known gap, own fix queued). Any template that shows a photo must read it server-side through `IFileStorageService.OpenUpload`, never via a new URL.
- QuestPDF Community licence applies. For Excel import/export use a permissively licensed library (ClosedXML is MIT). Confirm the licence of whatever you pick; do not use EPPlus.

## 2. What the five reference images tell us

The images are real report cards from one school, plus a stock template. **They contain real children's names and a real school's name, logo and artwork. Keep them out of the repo (local reference folder, gitignored), never commit them, never use those names in tests, seeds or templates, and do not copy artwork.** Draw original vector designs, as in the ID card work.

1. **UKG grid card (landscape):** grades only, no marks. Rows are exams (PT 1, PT 2, Half Yearly, PT 3, PT 4, Final); columns are subjects, each split into skills (Reading, Writing, Oral, Cycle-wise grade, Internal assessment). Per-term general remarks, Discipline and Cleanliness grades, a hand-written rank, attendance per term (72/93, 74/81), "Promoted to class", "New session begins on", three signatures. Grade key A/B/C with words.
2. **Cover and rules pages:** school name, recognition line, session, student profile block (name, class, section, roll, father, mother, address, contact), UDISE code, address, a rules/instructions list. Schools print these as a booklet.
3. **Class 1 two-page spread (Term I and Term II):** subjects (Hindi-I, Hindi-II, English-I and II, Maths-I and II, Science/EVS, S.St, GK, Sanskrit, Drawing, P.T., Computer) against Quarterly 1 (/10), Quarterly 2 (/10), Half Yearly (/80), **Marks obtained (/100)**, Grade, Remark. Term II repeats with Quarterly 3, Quarterly 4 and Annual, plus **Grand Total** (Term I + Term II). Totals, percentage, grade, Co-Scholastic grades, Discipline grades, attendance (219/239), term remarks, Passed/Failed, Promoted, Position, signatures.
   **Reference numbers to use as acceptance fixtures:** 9/10 + 9/10 + 78/80 gives 96/100 for the term; Term I 1023/1100 plus Term II 1037/1100 gives a grand total of 2060/2200. That card prints 93.63%; exact arithmetic is 93.636..., so this school **truncates**.
4. **Old simple card:** Max mark, 1st Paper, 2nd Paper, Total, Remark; Result (Promoted/Pass/Fail), Position, Division; text grades for Punctuality, Discipline, Conduct; attendance; Father, Mother, DOB; class teacher, parent, principal signatures.
5. **Stock "Class Report":** descriptive, not numeric. Learning areas (Writing, Reading, Speaking, ...) with Class Grade, Things to improve, Comments; a grading legend (A Superior to F Failure); Feedback & Remarks.

Conclusion: the **assessment structure differs by class group even inside one school** (image 1 vs image 3 are the same school), and cards range from marks tables to skill grids to descriptive reports. So structure and design must be separate layers.

## 3. Product goals

- A school running the whole ERP gets marks from the existing data (rosters, attendance, subjects) with no re-entry.
- A **report-card-only school** must be productive in an afternoon: quick-start presets, bulk student import (check whether one exists; if not, flag it as a gap), Excel marks template download/upload, manual attendance entry.
- Several designs per school to choose from, like the ID card (template gallery with live preview), selectable **per class group** because a school uses different cards for different classes.
- Report cards are near-legal documents: published ones are immutable snapshots with an audit trail.

## 4. End-to-end flow and roles

1. **School Admin: Setup.** Grade scales; report schemes per class group (terms, exams, subjects per exam with max marks and weightage, evaluation mode, pass rules, rank on or off, template); co-scholastic and discipline areas. Start from a preset or clone last year's scheme.
2. **School Admin: Exam schedule (optional in v1, dates only).** Exam date per exam, optionally per class. No seating or invigilation.
3. **Subject teacher: Marks entry** per exam, section and subject (eligibility reuses timetable rules plus class-subject mapping; class teacher and School Admin also allowed). Grid with Tab/Enter navigation, Absent toggle, validated against max, Excel upload. Draft saves allowed with partial rows; **Submit** requires every student to have marks, Absent, or Exempt.
4. **Class teacher: Section review.** Completeness dashboard (exam by subject, who has not submitted); enters co-scholastic and discipline grades, term remarks, rank text where grades-only, promotion decision and "new session begins" date; attendance is AUTO from the Attendance module for the term's date range, or MANUAL per student (working days and present days) when the school does not use SMSS attendance.
5. **School Admin: Lock** a term for a section or whole scheme (reopen needs a reason, audited).
6. **Compute results** (preview any time, draft only), then **Publish** (writes a versioned snapshot). A correction after publishing means re-publish as version N+1; the old version is kept.
7. **Print:** single card or section-wise bulk PDF, School Admin and the class teacher of that section. Principal read-only.
8. **Student (and later Guardian):** sees and downloads only their own **published** card via an authenticated endpoint.

## 5. Data model (concepts; you finalize names, columns, constraints, indexes)

- **Grade scales:** `tb_grade_scales` and `tb_grade_scale_bands`. Two kinds: PERCENT_BANDS (min and max percent, grade code, label, optional grade point, `is_fail`) and RATING (ordered labels, e.g. A Outstanding, B Very Good, C Fair, or the A to F legend of image 5). Validate that percent bands cover 0 to 100 with no gap or overlap. Presets are code constants that copy into the school's own rows on request. For CBSE-style A1 to E presets, **verify against the current official CBSE circular before shipping and mark them editable defaults**; I have not verified exact bands.
- **Report schemes:** `tb_report_schemes` (school, academic year, name, `evaluation_mode` MARKS or GRADES, grade scale, `year_combine` SUM_OF_TERMS or WEIGHTED, pass-mark percent per subject and overall, `show_rank`, `attendance_source` AUTO or MANUAL, `percentage_rounding` ROUND_HALF_UP or TRUNCATE, `template_key`) and `tb_report_scheme_classes` (a class belongs to at most one scheme per year).
- **Terms and exams:** `tb_report_terms` (scheme, name, sequence, start and end date for attendance) and `tb_report_exams` (term, name such as PT 1 or Half Yearly, sequence, default max marks, weight points toward the term total, optional date).
- **Exam subjects:** `tb_report_exam_subjects` (exam, class, subject, applicable, max marks override, entry mode MARKS or GRADE, `include_in_total`). This handles "Drawing has marks only in one exam" and extra subjects outside the total. Hindi-I and Hindi-II are simply two subjects in v1; a "subject group" is out of scope.
- **Skills (for grade-only skill grids):** `tb_report_subject_skills` (subject within scheme, name, sequence): Reading, Writing, Oral and so on.
- **Entries:** `tb_report_marks` (exam subject, skill nullable, student, `marks` NUMERIC(6,2) nullable, `grade_code` nullable, `entry_state` ENTERED, ABSENT or EXEMPT, with a check that the value columns fit the state). Unique per (exam subject, skill, student). Entry status per (exam subject, section): DRAFT, SUBMITTED, ACCEPTED, LOCKED, with who and when.
- **Co-scholastic and discipline:** `tb_report_activity_areas` (school-level, category, name, rating scale) and `tb_report_student_activity_grades` (term, student, area, grade).
- **Per student per term:** `tb_report_term_student` (term, student, remark text, conduct text, rank text override, manual attendance working and present days, promotion text and decision, new session date).
- **Results and publication:** computed results are stored only at publish: `tb_report_publications` (scheme, section, term or year, version, published_by, published_at, status) plus the stored per-student per-subject numbers and ranks for that version. Published versions never change.
- Audit rows for lock, unlock, publish and re-publish, following whatever audit pattern the project already uses.

## 6. Calculation rules (decided; implement exactly, and tell me if code or data contradicts them)

- Subject score in a term = sum over the term's exams of (obtained / exam max) x exam weight. Default weight = exam max, so a Class 1 term of 10 + 10 + 80 totals 100.
- **Absent** scores 0 and prints "Ab". **Exempt** removes the student from that subject (nothing counted). A subject marked `include_in_total = false` shows on the card but is excluded from totals, percentage and rank.
- Year result: SUM_OF_TERMS (default, matches image 3) or WEIGHTED by term weights.
- Percentage to two decimals using the scheme's rounding mode (the reference school truncates, so TRUNCATE must be selectable). Grades are looked up from the percentage through the scale; the lower band edge is inclusive.
- Result status per student: PASS, FAIL (list failed subjects) or INCOMPLETE (any missing entry; incomplete students cannot be ranked or published as PASS/FAIL). Subject pass mark default 33 percent, overall default 33 percent, both editable per scheme. Grace marks are out of scope.
- **Rank:** competition ranking (1, 2, 2, 4) within the section among complete students; optional class-wide; off by default for schemes with `show_rank = false`. In GRADES mode rank is a manual text field.
- **Attendance:** AUTO uses the Attendance module's working-day and percentage rules for the term dates (reuse, do not reimplement); MANUAL stores working and present days per student per term.
- Promotion ("Promoted to class X", "New session begins on") is a manual decision field for now; the future Student Promotion module will consume it.

## 7. Report card templates

Code-constant template keys with an original design each, rendering one shared normalized `ReportCardModel` (build the model once; the six templates only draw it). Each template declares which evaluation modes it supports, and the admin only sees compatible ones for a class group.

| Key | Inspired by | Modes | Layout |
|---|---|---|---|
| `classic-card` | image 4 | MARKS | compact portrait, papers/exams as columns, result block, conduct lines |
| `term-spread` | image 3 | MARKS | A4 landscape two-panel Term I and Term II, co-scholastic and discipline, grand total |
| `skill-grid` | image 1 | GRADES with skills | A4 landscape exam-by-skill grade matrix, term remarks, rank, promotion |
| `board-style` | CBSE-style scholastic plus co-scholastic | MARKS and GRADES | A4 portrait, classes 6 to 10 style |
| `playful-report` | image 5 | GRADES (descriptive) | learning areas, grade, things to improve, comments, legend |
| `minimal-bw` | none | MARKS and GRADES | single colour, ink-saving, photocopy-friendly |

- Optional extra pages, toggled in settings: **cover page** (image 2: school name, session, student profile block) and **rules page**. School header data comes from the existing school record; do not invent fields (check whether UDISE or recognition text exist; add to settings only if missing).
- Per-school settings table like `tb_school_id_card_settings`: two brand colours, header lines, principal signature (private storage), footer text, rules text, cover and rules toggles, show or hide for photo, rank, attendance, grade legend.
- Hindi/Devanagari in names and remarks must render (embedded font, tested as in the ID card work).
- Bulk PDF: section-wise, one file, explicit per-request cap (propose from a measured 40-student render), no background jobs.
- **Deferred, mention in your design but do not build:** printing only the data onto pre-printed booklet stationery (needs per-booklet alignment calibration); withholding report cards for unpaid fees; Excel import of class-test marks.

## 8. Screens (propose wireframe-level detail in your design)

Admin: Grade Scales; Report Schemes (wizard: scheme, classes, terms, exams, subject matrix, pass rules, template); Activity Areas; Exam Schedule; Results and Publish; Report Card Settings with template gallery and live preview. Teacher: Marks Entry. Class teacher: My Class Report (completeness, activity grades, remarks, attendance, promotion). Student: My Report Card. Principal: read-only Results. Match the existing app's look (dark sidebar, ID card settings screen as the gallery pattern); add a sidebar group rather than loose items.

## 9. Build stages (after I approve your design)

1. **Setup:** scales, schemes, presets and clone, terms, exams, exam subjects, skills, activity areas, schedule. No marks.
2. **Marks and class-teacher inputs:** entry grid, Excel template and upload, states and review, activity grades, remarks, attendance, promotion fields, lock.
3. **Results and publishing:** calculation, preview, snapshot and versions, result screens, Student My Report Card, plus **one** template (`term-spread`) so the whole chain works end to end.
4. **Templates and bulk:** the other five templates, settings and gallery, cover and rules pages, bulk PDFs.

Each stage is its own commit set, with the project's standing discipline: backup, throwaway accounts only, existing-data fingerprint, full regression with 0 new failures, scratch-copy smoke tests, frontend specs, not pushed.

## 10. Your Phase A report must contain

1. What you found in the existing code that affects this (subjects and mapping, academic years, attendance, assessments, ID card template and settings code, storage, subscription gating, student import) and anything in the codebase that contradicts this brief.
2. Proposed DDL (tables, keys, checks, indexes, soft-delete behaviour) and the migration plan.
3. Endpoint list with a role-by-endpoint authorization matrix.
4. The calculation spec as pseudocode, plus the image-3 fixtures worked through (96/100, 2060/2200, 93.636 truncated to 93.63).
5. The `ReportCardModel` shape and how the six templates will share rendering code.
6. A performance plan for bulk generation and for result computation (fixed query count regardless of class size).
7. Screens and the per-stage test plan.
8. Risks, anything you would change in this brief, and questions for me. Ask only what you cannot decide safely yourself.

Then stop and wait.

---

## 11. Phase A decisions (architect review, 5 Nov 2026) — these override earlier sections where they differ

**Answers to the questions**
- **Q1 Marks eligibility:** the union rule. A user may enter marks for (section, subject) if School Admin, or class teacher of that section, or teaching that section and subject in the timetable; Class-Subject Mapping is required in every case. No explicit "who teaches what for exams" table in v1. Report-card-only schools use class-teacher or School Admin entry, including Excel upload. Revisit only if a real school needs subject-teacher entry without a timetable.
- **Q2 Principal:** read-only, plus read and download of published cards. Only School Admin configures, locks and publishes. *Previous decision:* the role matrix (section 5 of the project doc) lets Principal "Manage" examinations. *New decision:* Principal is read-only for Examination. *Reason:* consistent with Attendance and the rest of the project's Principal pattern. *Impact:* update the role matrix; a Principal approval step can be added later if a school needs sign-off.
- **Q3 Rank pool:** PASS students only. FAIL and INCOMPLETE students get no rank. No setting for this in v1.
- **Q4 Attendance denominator:** marked working days per student, with the warning for working days nobody marked (shown on the class teacher's review and on the Publish screen as a warning the admin acknowledges, not a blocker).
- **Q5 Audit:** module-local append-only `tb_report_audit` approved. It is not a generic audit framework and must not grow into one.
- **Q6 Student import:** yes, as its own stage between Stage 1 and Stage 2 (see stages below).
- **Q7 Gating:** none in v1.

**Accepted design changes:** bands store lower edge only (the UI shows the derived upper edge); `include_in_total` at scheme-subject level; promotion per year; `show_rank` only on the scheme; module-local audit table; `card_model` jsonb snapshot per student; the immutability trigger on `tb_report_publication_students` (the project's first trigger; keep it tiny and test it directly; test cleanup scripts must disable it explicitly inside their own transaction, never in app code).

**Required changes before Stage 1 DDL**
1. `status_id` must have the same type as `lut_status.status_id` (the project uses BIGINT elsewhere); do not introduce `int`.
2. The snapshot stores **no binary data**. Photos, logo and signature are resolved at render time from storage paths. Check that replacing a principal signature creates a new file name rather than overwriting, so older published versions do not silently change; if it overwrites, tell me.
3. Lock has one source of truth: the term-section lock and per-exam-subject entry status must not be able to contradict each other (locking sets both in one transaction, and unlock rules are defined).
4. Publishing requires the term-section to be LOCKED. Students who are INCOMPLETE are excluded from that publication, listed by name in the confirmation, and included in the next version once complete.
5. In a MARKS scheme, an exam subject with `entry_mode = GRADE` is display-only (grade shown, never summed, never ranked, never fails). State this in the calculation spec.
6. Setup speed is the product. Stage 1 must ship presets matching the three real card shapes (term-wise PT/Half-Yearly/Annual out of 100; two-paper simple; pre-primary skill grid) so an admin can complete a working scheme in three steps and under ten minutes.

**Revised stages**
1. Setup (as designed, including presets, clone, and the scheme wizard).
2. **Student Excel import** (new, own commit set): dry-run validation preview, row-level errors, duplicate admission-number handling, creates the same records and logins as the existing existing-student onboarding path (reuse that service, hash passwords, same credential handling), row cap, no fee assignment. Exit criterion: confirm the ClosedXML dependency tree (pin SixLabors.Fonts to 1.x or fall back to DocumentFormat.OpenXml) before writing any Excel code.
3. Marks entry and class-teacher inputs, including the shared attendance day-classification refactor (own commit first; existing attendance tests plus the 90 real section-month comparison must stay identical).
4. Results, publishing, Student My Report Card, `term-spread` template, plus the PdfBootstrap extraction (own commit first). **Run the synthetic 40-student render spike at the start of this stage**, not later.
5. Remaining five templates, settings and gallery, cover and rules pages, bulk PDFs.

**Noted, not built:** an employee Excel import for report-card-only schools (class and subject import exists; employee onboarding is still one by one). Per-student billing for report-card-only schools is a commercial question for later.

---

## 12. Stage 1 status (6 Nov 2026) and decisions

**Built, local, not pushed:** `c2db733` (backend plus `Docs/smss-report-card-stage1.sql`), `668a3cd` (exam schedule returns each scheme's classes), `b179633` (Examinations sidebar group, grade scales, schemes with the three-step wizard and editor, activity areas, exam schedule). 11 new tables on the real DB, existing-data fingerprint 0 changed. Verified on scratch restores: constraints 61/61, API smoke 113/113 (full role matrix, cross-school 404), frontend 869 tests, legacy suites no new failures. **Not yet run:** the real-DB API smoke with throwaway accounts (needs API restart); do it before Stage 2 starts.

**Confirmed decisions**
- `status_id` is `integer`, matching `lut_status.sid` and every other `status_id` in the project. This corrects section 11 item 1, which wrongly said BIGINT.
- Principal is read-only for Examination, as built.
- Locking: `tb_report_term_section.status` is the only lock authority; entry status is DRAFT, SUBMITTED, ACCEPTED only; a lock is one transaction requiring everything ACCEPTED and writes an audit row; only School Admin can unlock, with a reason. Unlock alone does not allow editing: changing an ACCEPTED entry needs an audited reopen with a reason; re-publishing needs a fresh lock.
- Config children (terms, exams, subjects, matrix cells, skills) are hard-deleted; scales, schemes and areas are soft-deleted. **Stage 2 exit criterion:** the placeholder "no entries" guard becomes real in Stage 2's first migration (non-cascading marks FK plus service guard), tested: deleting an exam, subject, skill or class with marks returns 409.
- Term dates default to an even split of the academic year. They drive AUTO attendance, so the wizard must make that visible.
- **Template availability in Stage 4:** until a template is built, a scheme that selects it can be saved, but preview, print and publish for it are blocked with a clear message ("this design is not available yet, choose Term Spread"). No silent fallback. Stage 5 lifts this.
- **Signature:** the report card settings never delete a signature file that a publication references. Each upload gets a new file name, the publication's config snapshot stores the file key, and old published cards keep their original signature. Removal of unreferenced old files is a later chore. Done in Stage 5; the ID card's own signature behaviour is unchanged.

**Order from here:** 2 student Excel import (ClosedXML licence check first), 3 marks entry and class-teacher inputs, 4 results, publishing and `term-spread`, 5 other templates, settings and bulk. The student photo privacy fix still comes before Stage 5.

---

## 13. Student Excel import status (6 Nov 2026)

**Built, local, not pushed:** `d8e7517` (ClosedXML 0.105.1, SixLabors.Fonts pinned to [1.0.0,2.0.0), Apache-2.0; all other Excel packages MIT; licences in `Docs/third-party-licences.md`), `adcc9c0` (shared onboarding builder, no behaviour change), `3a5de20` (backend import), `71a7be2` (frontend). Verified: Stage 1 real-DB smoke 113/113; import smoke 121/121 on scratch (every validation rule, atomicity, hostile files, concurrent imports); real DB 19/19 with throwaway students removed; frontend 893 tests; no new legacy failures. Row cap 500 (measured: 200 rows in 0.37 s, 500 in 0.89 s, 16 to 18 DB commands).

**Behaviour:** School Admin only; no logins and no passwords (existing onboarding creates none; Create Login is a separate per-student action); no fee assignment (the existing flow assigns one student at a time); admission number optional (generated as today); admission date is the start of the current academic year; guardians need name plus phone, first complete one is primary (father, mother, guardian); single-word names are an error; all-or-nothing commit.

**Accepted gaps, queued (none block marks entry):**
1. Bulk fee-structure assignment for imported students (they have no fees until assigned; the class teacher fee view will show "no fee" for them).
2. Bulk Create Login with a one-time credentials sheet (needs a BCrypt timing measurement; do not lower the work factor).
3. Optional "admission date" column in the import template (report cards print "date of joining"); add before the remaining templates are built.
4. Manual browser test of Import Students after the API restart (real multipart upload from a browser was not exercised).

---

## 14. Marks Entry stage status (6 Nov 2026)

**Built, local, not pushed:** `54a01bb` (attendance day rules in a shared helper; monthly report byte-identical across all 360 section-months), `fe23043` (migration `Docs/smss-report-card-stage2.sql` plus backend), `910ab0c` (frontend). Verified: 65/65 constraint cases, 265 backend smoke checks, fixed DB command counts (grid 9, completeness 12, attendance 9, context 8, term inputs 9, activity grades 11, same for 12 and 52 students), frontend 933 tests, no new legacy failures, real DB 32/32 with throwaway accounts removed and fingerprint 0 changed.

**Behaviour as built:** per-grid version token on the entry-status row (stale save gets 409); Excel upload is a dry run that fills the DRAFT grid, never submits; skills apply to grade entry only; Absent and Exempt are toggles on marks grids and drop-down options on skill grids; promotion becomes final when the last term is locked; `tb_report_audit` is append-only through a small trigger (test clean-up disables it only inside its own transaction); Stage 1 guards are item-precise (only items with entries are protected; max, weight, mode changes blocked once entries exist; term dates blocked while a term is locked); five UNIQUE keys added on existing tables as anchors for composite FKs, including `tb_students (student_id, school_id)`; no `status_id` in the new tables.

**Decisions**
- Principal may open marks grids read-only; a subject teacher who is not the class teacher reads only their own subject's grid and gets 403 on the class report. Confirmed.
- School Admin may submit and accept their own entries; no second-person check (many small schools have one administrator; the audit table records the actor). Confirmed.
- **Late students must not block locking.** Previous behaviour: a student added after a grid was accepted blocked both accept and lock. New decision: accept still requires every roster student to have an explicit state (marks, Absent or Exempt), but lock proceeds when the only gap is a late student, after a confirmation that lists them by name; the audit row stores their ids; Stage 3 treats them as INCOMPLETE and excludes them from publication. Reason: one new admission must not block a whole section's publication.
- The five UNIQUE keys are accepted on condition that each is a superset of an existing primary or unique key, so none can fail on data or change behaviour. The next report lists all five (table, columns, why).

---

## 15. Results and Publishing stage status (6 Nov 2026)

**Built, local, not pushed, nothing published:** late students no longer block lock (Step 1); `ca3c5ba` shared PdfBootstrap (all six ID card templates render pixel-identically); `01c61fd` calculation engine with fixtures F1 to F12 as a 30-test xUnit project (the repo's first test project, approved for the calculator only); `345c0b1` migration for publications and cards; `9de7957` backend plus the Term Spread design (one commit, accepted because the card model and the template registry reference each other); `13e9ebe` and `0760a1d` frontend; `5d38317` demo seed script.

**Verified:** engine matches an independent SQL recomputation for every student in every seeded section on term and year scopes (276 results on the real data); v1 unchanged after v2; a published card re-renders identically after live marks and the school name change; Hindi renders; immutability triggers hold; fixed DB command counts (preview 29, first publish 31, republish one more, same for 12 and 52 students); real-DB smoke 19/19, fingerprint 0 changed. The five Stage 2 UNIQUE keys (all supersets of existing primary keys): `tb_report_exam_subjects (exam_subject_id, school_id)` and `(exam_subject_id, scheme_subject_id)`, `tb_report_subject_skills (skill_id, scheme_subject_id)`, `tb_report_activity_areas (area_id, school_id)`, `tb_students (student_id, school_id)`.

**Demo data on the real DB (demo schools only):** 6 schemes, 4,416 marks, 16 locked term-sections, nothing published. Publish from the screen to judge the design.

**Decisions:** Term Spread is MARKS-only; preview, draft and publish share one builder; Principal gets published PDFs but never draft PDFs; a class teacher must name their section to list versions; publish is School Admin only; header and colours stay hard-coded until the settings stage; the signature row is a plain line until a signature is configured.

**Lapse to close:** the 40-student render spike numbers were not kept. The spike must be re-run at the start of the bulk stage, and the numbers written into a committed doc (not left in a session).

**Next, in order:** (1) owner reviews the Term Spread card from the demo data; (2) housekeeping and photo privacy batch (Bootstrap setting, `.gitignore`, `/missing` holiday fix, student photos to private storage, optional admission-date import column); (3) the remaining five templates, settings and gallery, signature file keys, cover and rules pages, bulk PDFs.

---

## 16. Housekeeping and photo privacy batch (6 Nov 2026)

**Built, local, not pushed:** `686dbc7` (Bootstrap false in committed config), `d63d0fc` (git hygiene), `b54f269` (`/missing` ignores Sundays, Holiday events and out-of-year days; monthly report byte-identical), `e0e9c5b` plus `1abf74f` (student photos to private storage), `f8b930c` (optional admission date column in the import). Verified: photo smoke 33/33 scratch and 14/14 real (read-only), old public URLs 404, 2 real photos migrated (0 missing, 0 failed), frontend 965 tests, unit tests 34/34, no new legacy failures.

**Photo design:** `profile_photo_url` holds the private storage key; DTOs return `/api/Student/{id}/photo?v=<fingerprint>`; image content type, `Cache-Control: private, no-cache`, ETag with 304; anything under `/uploads/<school>/students/` is 404 on the public path; Angular secure-image service and component (authenticated fetch to object URLs, bounded cache, cleared on sign-out). Student sees only own photo; Teacher only students in their own sections; other staff any student in their school; Guardian 403; cross-school 404; no token 401.

**Decisions**
- Teacher photo scope stays strict (own sections), even though the profile endpoint still lets any teacher open any student's record. That broader profile read is the known queued gap (section 8, deferred per section 25) and should be closed next to this.
- No per-user cache for Teacher photo checks until it is slow.
- School logos stay public (shown only in the login response, no anonymous endpoint returns them). Documented, not changed.
- Employee photos move to private storage the same way (none exist on the real DB, so the migration is trivial).
- Term Spread gets "date of joining" (the data is now on the record) with the remaining template work.

**Security findings, open**
- The committed `appsettings.json` holds secrets (setup key, SMTP app password, database password, and possibly the JWT signing key). This breaks the project rule against hard-coded secrets. Fix: move all secrets to user-secrets and environment variables, fail fast with a clear message if missing outside development, document in `Docs/configuration.md`, and **rotate** every one that was committed (rotation matters more than history cleanup).
- Seven uploaded files (two real student photos, five logos) and `Docs/demo-credentials.txt` remain in older local commits. Rewriting history is only cheap before the first push; first check which commits are already on any remote.

---

## 17. Security incident and cleanup (6 to 7 Nov 2026)

**Finding:** the GitHub repo was public and held, in history, the DB password, JWT key, SMTP app password and setup key (all identical to values in use), seven uploaded files (two student photos, five logos), `Docs/demo-credentials.txt`, and committed database dumps (custom-format binary; password hashes plus each school's data; 14 distinct dump contents, 1 to 4 users each, test schools only unless the owner says otherwise). Refresh tokens are stored hashed and every refresh-token row in the dumps is expired, so no leaked token is valid.

**Done, local, nothing pushed:** secrets moved to user-secrets with a fail-fast startup check (`d085d8a`); employee photos private (`0565786`); Teacher-only logins can no longer read any student record (`163fd41`); dumps untracked and ignored, gitleaks pre-commit hook and scan command (`cf7c67d`, `5a8ff65`, `8ea0650`); runbook `Docs/security-runbook.md`; rewritten history prepared and verified in `E:\SecurityBackup\smss-rewritten.git` (original backup in `smss-original-mirror.git`).

**Decisions**
- Rotate every committed secret and treat the old values as burned; make the repo private; do not commit anything until the push is done (new commits are not in the prepared mirror).
- Preferred route for the history: delete and re-create the repo private and push the rewritten mirror to it (guarantees the old commit ids disappear from GitHub, since a force-push leaves unreachable commits reachable by id until GitHub purges them). Use force-push plus a GitHub Support purge request only if the repo has collaborators, issues or settings worth keeping.
- The demo admin password `Admin@Demo26` stays in the committed seed script (invented demo schools); demo data must never be loaded into a hosted database.
- There is no reset-password or forgot-password feature, and no Super Admin reset for a school login; SQL is the only way. This is a product gap to build properly (see queue), not a script.

**Queue:** password reset (Super Admin resets a school login; School Admin resets employee and student logins; temporary password plus `is_first_login`; later a forgot-password flow); untrack `smss-api/.vs/` and `*.csproj.user`; bulk fee assignment; bulk Create Login; the remaining five report card templates, settings gallery, signature file keys, cover and rules pages, bulk PDFs (start with the spike re-run, numbers written into a committed doc).
