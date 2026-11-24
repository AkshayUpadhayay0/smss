# Academic Year Configuration — design record (S1–S7)

Status: built and proven on scratch databases (commits up to 8598ed8 on branch 21Nov). Not pushed. Software is not live; demo schools only (APS, GHS, LAS).
Full design lives in the repo at Docs/academic-year-config-design.md (includes the timetable-versions addendum).

## Decisions
- Section = class + section name; capacity is per year, held on the year's section offering.
- Closing a year blocks configuration, attendance, enrolments, homework and timetable edits. Fee payments, voids, receipts and reads still work. School Admin can reopen. The current year can never be closed.
- A teacher may be class teacher of several sections in a year, with a confirm that names the other sections.
- No overlap among Active years; no ordering requirement (next year can be created early).
- Subject mapping is per class per year, with section-level ADD/REMOVE overrides. Effective subjects of a section = class mapping minus REMOVE plus ADD, defined once in one view and used by every reader.
- Schedules: named bell schedules (school-level masters). Resolution order: class+day, class, day, everything, then the school default. Break slots can never hold a lesson.
- Mon–Sat only; a 5-day school simply has no Saturday schedule.
- Callers without a year default to the current year (logged), to be removed later.
- Out of scope: promotion, roll-number regeneration, fee-structure copy.
- Previous decision: expand-then-contract migrations. New decision: dropped, because the software is not live; NOT NULL and final constraints applied directly. Reason: no real data to protect. Revisit once real schools have data.

## Timetable versions
- Many timetables per section per year; exactly one active per section (partial unique index). Activation deactivates the previous active in the same transaction under the per-school advisory lock. A timetable that was ever active cannot be deleted.
- Teacher double-booking: each entry carries a mirror of its timetable's active flag (composite FK, cascades). A partial unique index on (school, year, teacher, day, period) where the mirror is true makes double-booking among active timetables impossible in the database.
- Drafts never block anything and never show in any reader (section grid, teacher grid, homework checks, report-marks eligibility).
- Apply to other sections: creates new independent drafts, copies day, period, subject; teacher copied only where free, otherwise the slot is flagged needs-teacher. Activation with empty teacher slots is allowed with a warning count.

## Class teachers
History is contiguous (effective_from / effective_to, reason, assigned_by). One current per section per year. Reassigning closes the old row and opens the new one in one transaction; nothing is edited in place or deleted.

## Year transition (S7)
Copy from a source year to an Active, non-closed target. Parts: offerings, subjects and overrides, schedule assignments, class teachers, timetables (as drafts only). Skip-if-exists, never overwrite, never creates masters, never touches the source. Preview runs the real code and rolls back. Real run needs a confirm flag, takes the per-school lock, writes one tb_year_copy_log row. Re-run creates nothing.

## Migrations (idempotent RUN-ME files in Docs/, each verified, ALREADY MIGRATED on a second run)
Order matters: S1 smss-acad-s1-years, S2 smss-acad-s2-offerings, S3 smss-acad-s3-subjects, S4 smss-acad-s4-schedules, S5 smss-acad-s5-class-teachers, S6 smss-acad-s6-timetables, S7 smss-acad-s7-year-copy. Stop the API, back up with pg_dump, run in pgAdmin, send Messages, start the API from the latest commit.
Verified counts on the Nov dump: offerings APS 8/8, GHS 13/15, LAS 13/15; subject mappings APS 4, GHS 109, LAS 109; periods APS 8, GHS 6, LAS 6; timetable entries 26 / 540 / 541 on one active timetable per section; current class teachers 1 / 15 / 15.

## Known gaps and follow-ups
- Copy fee structures to a new year (most useful next task).
- Optional hardening: database teacher clock-time exclusion across schedules; remove the default-current-year fallback. (The unused old School dashboard and its endpoint were deleted in Stage 1c.)
- Not exercised: the FAILED copy-log path, a source year with several timetable versions per section, the mobile app on a device, the report card UI (API only).
- The S7 preview table scrolls sideways at 390px instead of stacking.
- Subscription billing counts enrolments in the current year, so setting a new current year changes the billed count (documented, unchanged).
