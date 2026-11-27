# Stage 2 build log (2a–2d) and decisions that override the older design docs

Status: built and committed locally on branch 24_nov, NOT pushed. Real database (smss-db) migrated for 2a–2d, each with a pg_dump backup taken first (E:\SmssBackups\smss-db-before-st-2a/2b/2c/2d-*.dump).

Test baseline after 2d: backend 21 failed (same 21 old failures) / 2188 passed / 19 skipped / 2228 total; web 1607 passing.

## Decisions that override earlier docs (admission-docs-latefee-registration-design.md, student-year-promotion-design.md, guardian-portal-design.md)
- Dues for promotion = due_date <= promotion date; not-yet-due shown as information. Dues join through tb_student_fee_items.academic_year_id (tb_student_fee_dues has no year column).
- Fee structure copy to the new year happens BEFORE promotion (stage 4a).
- Admission: 13 ADMITTED rows; there was never a DB check for Under Review. Two-step flow = Application added -> Confirmed; DB codes unchanged (APPLICATION_SUBMITTED, ADMITTED), only labels changed.
- Documents reuse tb_document_types.is_required (no new flag). "Days to submit" and OVERDUE are deferred; only PENDING, SUBMITTED, WAIVED.
- Dashboards (Principal, Accountant) default to the current-flagged year; Accountant cash tiles are all-years cash; old School dashboard retired.

## 2a Optional last name
tb_students.last_name and tb_admissions.applicant_last_name nullable + not-blank check; blank becomes null in the API. Name sweep committed separately (903cb99).

## 2b Guardian contact and school fields
tb_guardians.whatsapp_number, tb_admissions.guardian_whatsapp_number, tb_schools.whatsapp_number, tb_schools.latitude/longitude (Super Admin only, set as a pair). Primary guardian WhatsApp (or same-as-mobile tick) and email mandatory on application, Add Existing Student, Excel import (SAME keyword). New numbers stored as +91XXXXXXXXXX; existing 10-digit numbers untouched until the guardian stage. A school without WhatsApp only gets a dashboard banner.

## 2c Two-step admission
Start-review and approve removed. Roles allowed to add/edit/confirm/reject/withdraw: School Admin, Principal, Admission Staff; others 403. Confirm needs guardian WhatsApp + email, records confirmed_by/confirmed_at, sends no message and creates no login. 1 UNDER_REVIEW row migrated; 13 ADMITTED rows backfilled. Principal tile renamed "Applications waiting to be confirmed" (JSON field applicationsWaitingToBeConfirmed). Deploy the new API build before anyone confirms an application.

## 2d Student documents
tb_student_documents (one active row per student+type, void not delete, private storage, PDF/PNG/JPG/WEBP up to 5 MB, signature-checked). required_from set when is_required switches on; applies to students admitted on/after it. Waiver needs a reason. Roles: School Admin, Principal, Admission Staff manage; class teacher of the section reads statuses only; all others 403. Application documents carry over after confirm (failure never blocks). Deploy order: run smss-st-2d-RUN-ME.sql before the new API build.

## Open follow-ups
- Document Types screen does not yet show required_from.
- 5 added applications lack guardian WhatsApp/email; must be completed before confirm.
- "Complete parent details" list for existing guardians without email: built in the guardian stage.
- Nothing pushed; secrets rotation and repo visibility check still to be confirmed by the user before any push.
