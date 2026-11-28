# Post-testing changes (28 Nov 2026): earlier-year dues, import fees, global viewing year

Status: committed locally on branch 24_nov, NOT pushed. No migrations. Baseline after Part B: backend 0 failed / 2745 passed / 19 skipped / 2764 total (last run before Part B, which changed no backend code); web 1810; mobile 236.

## Issues found by the owner while testing
1. Global academic year selector (built, Part B).
2 and 4. Previous years' outstanding fees must stay visible (built, Part A).
3. Class teacher list empty when assigning (not reproduced; endpoint is correct for every school that has teachers; likely cause is a teacher without a login or Teaching record; the screen now explains why the list is empty and offers a retry). Owner still to say which school and what the dropdown showed.
5. Excel import did not assign fees (built).

## Decisions
- Earlier-year dues: nothing is carried forward and no due rows are moved or created; a derived "Outstanding from earlier years" amount per year is shown beside the current year on the student Fees tab, payment dialog (earlier installments first and pre-selected, not forced), My fees, the parent tile and children list, the class fee report (separate "Earlier years" column) and in the overdue count of fee reminders (text unchanged, no amounts). A leaver who still owes stays on the class report flagged "Left". Documented in Docs/earlier-years-dues.md.
- Import: after the import commits each imported student gets the class fee structure through the same code as admission and the Assign fees repair; when the year has started the import dialog offers "Charge from the year's start" with a confirmation tick and a reason (logged in tb_fee_backdating_log); the preview names classes with no fee structure.
- Global viewing year (Docs/viewing-year.md): selector in the header for School Admin, Principal and Accountant listing every year (Current, Closed, Upcoming, Past); "current" is stored as a marker so everyone follows the flag when it moves; only an explicit pick is stored as an id; resets to current at each new sign-in, survives reloads in the tab; highlighted chip when not current. Dashboards, fee reports and lists follow it; data-entry pages always act on the current year and show a banner when the selection differs; promotion, year copy, academic setup, scheme creation and the fee structure builder keep their own year controls; payments stay cross-year; cash and receipts stay by date. The server still validates every year id against the token's school.

## Open
- Attendance report and admissions list filter not yet converted to the viewing year; homework and assessment list filters are teacher-facing and stay as they are.
- Owner tasks before any push: make the repository private, rotate JWT key, setup key, Gmail app password and database password, then the history rewrite and force push per Docs/push-plan.md.
