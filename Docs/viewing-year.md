# The viewing year (web, no migration)

One "viewing year" for the whole web app, chosen in the header (`core/services/year-context.service.ts`). It only decides which academic year reports, lists and dashboards LOOK at. It never changes the school's current year and never decides where data is entered.

- **Who:** School Admin, Principal, Accountant get the selector (every year the school has, labelled Current / Closed / Upcoming / Past). Teacher, Student, Guardian and the other roles do not: their pages work as before (the server's current year).
- **Default and storage:** the choice is stored in `sessionStorage` (`smss.viewingYear`) as the marker `current` (follows the school's current flag, so when an admin moves it everyone who did not pick explicitly follows) or as a year id (an explicit pick). Picking the year that is currently current stores the marker. A stored id that no longer exists, or an invalid value, falls back to current. It survives a page reload in the tab and starts again at `current` at every new sign-in (`AuthService.loginEpoch`).
- **Header chip:** whenever the viewed year is not the current one a highlighted chip says so, with "Back to current".
- **API:** every call still carries its own explicit `academicYearId` query parameter (the server validates it against the token's school, another school's year is a 404). Nothing is sent as a header.
- **Pages that follow it (own picker removed):** School Admin / Principal / Accountant dashboards, Fee Dashboard, Class-wise Fee Report, Section Fee Status, Student list, Report Schemes list, Attendance Report, Admissions list (the server is asked for that year's applications, with the explicit id), Section list (class teachers: read-only banner and disabled assignment in a non-current year). Helpers: `followViewingYear`, `syncYearControl` (`core/utils/follow-viewing-year.ts`). A page falls back to its old default when the years cannot be loaded.
- **Data-entry pages (act on the CURRENT year; `<app-viewing-year-banner />` says so when the header differs):** Mark Attendance, Homework, Assessments, Timetable, Admissions (list, detail, new application), Add Existing Student, Import Students.
- **Keep their own explicit year controls:** Promotion, Year copy, Academic Setup, Academic Years, scheme creation, fee structure builder.
- **Stay by date or student-wide:** cash and receipts (by date); payments (cross-year, see `Docs/earlier-years-dues.md`).
- **Not converted (they still have their own year filter):** the homework and assessment list year filters (Teachers use them too).
- **Roles without the selector** (a Teacher on the Attendance Report, Admission Staff on the Admissions list) keep the server's current year / every year, exactly as before.
- **Mobile:** nothing changed. The app has no year picker for any role that has a selector (the School Admin home reads one dashboard endpoint; the teacher marks screens pass the year the server gives them).
