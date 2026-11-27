# Stage 4 build log (4a fee copy, 4b promotion) and differences from student-year-promotion-design.md

Status: built and committed locally on branch 24_nov, NOT pushed. Migrations on the real database (smss-db): 4b-1 done with backup; 4b-4 pending the user's approval at the time of writing. Where this log differs from student-year-promotion-design.md, this log wins.

Baseline after 4b-4: backend 21 failed (same old 21) / 2421 passed / 19 skipped / 2461 total; web 1679 passing.

## Decisions
- Fee structure copy is a part ("Fee structures") of the existing Year Copy screen, after classes/sections. All copied lines start on the target year's start date; one uniform percentage increase rounded to Rs 1; whole class copied or skipped with a visible reason; idempotent. School Admin applies; School Admin or Principal previews.
- "Assign fees for a year" repair action (School Admin): covers active enrolments without fees; optional-fee choices carry over; offers back-dating from the year's start (confirmation + reason, existing tb_fee_backdating_log) when the start has passed; max 500 per run.
- Concessions are per year and are never carried to a new year.
- Outcome lives in tb_student_enrollments.outcome / outcome_at (PROMOTED, REPEATED, PASSED_OUT, TRANSFERRED_OUT, LEFT). The old enrolment keeps class, section, roll and status; it is never set Inactive.
- tb_students.status is unchanged for passed-out/left students. Pending-documents and parent-details lists use "enrolled in the current-flagged year" (previous: active student).
- Promotion: School Admin and Principal. One class (all sections) or one section, up to 500. Preview then execute (confirm:true), everything recomputed on the server under the per-school advisory lock; each student on its own savepoint. Dues block = unpaid dues with due_date <= promotion date (default today, editable, stored on the batch); later dues shown as information. Applies to PROMOTE, REPEAT, PASS OUT and LEFT. Override: each row ticked individually, one run reason (min 10 chars) stored on batch and every override item, nothing carried forward. Teacher decisions only prefill; no decision = left out of the run.
- New-year fees are assigned after the commit (same code as the repair action), best-effort; if the target year's start has passed the run needs the back-dating confirmation; target classes with no fee structure need an acknowledgement tick.
- Single-student correction ("Correct a promotion" card on /promotion): School Admin and Principal, reason min 10 chars; refused if the new year holds attendance, payments or paid dues, marks, transport or a concession, or the year is closed; removes the new enrolment, fee items, dues and fee-structure assignment, clears the old outcome, audit row in tb_promotion_corrections, promotion item marked reversed (not deleted).
- Subscription billing counts Active enrolments of Active students in the current-flagged year; the bill falls when the current flag moves to the new year, so promote everyone before switching.

## Tables added
tb_promotion_batches, tb_promotion_items, tb_promotion_corrections (4b-4 migration smss-st-4b4-RUN-ME.sql, also adds tb_promotion_items.reversed_at).

## Open follow-ups
- Not checked: smss-mobile; login handling for passed-out and left students (Stage 8).
- Pending promotion list/tile pairing before and after the current-flag switch (being fixed).
- Do not run a real promotion on school data until the 4b-4 migration is on the real database and the user has tested on demo data.
- Next: Stage 5 late fee, then 6-7 parent accounts and messaging, then 8 status/leaving/transfer certificate.
