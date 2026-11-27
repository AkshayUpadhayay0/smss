# Admission, student documents, late fee and school registration — design record (24 Nov 2026)

Status: DESIGN ONLY. Nothing built. Agreed in discussion with the owner on 24 Nov 2026.
Basis: existing project docs (claude/roles-workflow.md sections 28, 36), the Claude Code student-flow and masters investigations, and the owner's answers. The live database was NOT queried.
Related: claude/student-year-promotion-design.md, claude/guardian-portal-design.md.

## 1. Student and admission form
- Only FIRST NAME is mandatory. Middle and last name are optional. Today tb_students.last_name is NOT NULL (and the admission record has the same field), so it must become nullable. Report cards, ID cards, lists, sorting, published snapshots and every place that joins the name must handle an empty last name without stray spaces. The Excel import currently rejects single-word names; that rule goes.
- Guardian section: primary guardian needs name, relationship, mobile, WHATSAPP number (mandatory) and email (mandatory), with a "same as mobile number" tick for WhatsApp. Numbers stored with country code (+91 default). The student's own mobile and email stay optional. Details in claude/guardian-portal-design.md.
- Sibling pick: "Has a sibling in this school?" Choose class, then name; the sibling's guardians appear with a tick each; the admin ticks which apply or adds another. Mobile match is a hint only.
- Applies to the admission application form, Add Existing Student (direct entry), Confirm Admission and the Excel import template.

## 2. Admission in TWO steps
- Previous flow: Submitted -> Under Review -> Approved or Rejected -> Confirm Admission (creates the student). New flow: Application ADDED -> Application CONFIRMED (creates the student, enrolment, guardian link, fee assignment, parent account and message in one action; message failure never blocks it). Reject and Withdraw remain as exits, not steps. Reject still needs a reason.
- Previous decision (roles-workflow section 36): CanManageApplications (School Admin, Principal, Admission Staff) could add and edit; only CanDecideAdmission (School Admin, Principal) could approve, reject and confirm. New decision: School Admin, Principal AND Admission Staff can add, confirm and reject. Reason: owner's choice. Impact: Admission Staff can now create students; every confirmation must record the actor; Teacher, Accountant, Student and Guardian stay blocked (the earlier security fix holds).
- Remove the Under Review and Approved states from the flow. Existing applications in those states are migrated to the pending (added) state in one migration; reviewer and approver columns keep their values. The database checks "decision requires Under Review" go; "ADMITTED implies a linked student" and "reject needs a reason" stay. The two real ADMITTED applications must be untouched.
- Keep: per-school gapless application numbers under the advisory lock; Confirm concurrency safety (6 simultaneous confirms of one application must make exactly one student); Confirm Admission remains the only conversion path.

## 3. Required documents
- Document types gain a "required for admission" flag in School Setup (the existing Document Types screen). The same required list applies to ALL students (owner's choice).
- Documents belong to the STUDENT, not the year. There is no student document table today (only tb_admission_application_documents per application, the older tb_admission_documents, and employee documents), so add one, with the same private storage and validation as the application documents (PDF or image up to 5 MB, file-signature check, never a public URL).
- Documents uploaded on the application carry over to the student when the application is confirmed.
- Per student and per required type the status is SUBMITTED, PENDING or WAIVED (waiver needs a reason and records who).
- Documents NEVER block admission. A missing required document shows "Document not submitted" on the student profile, and the School Admin dashboard gets a "pending documents" count that links to a list of students and what they lack.
- Optional school setting "days to submit" (default off). When set, a document past its deadline shows as overdue.
- The requirement applies to students admitted after the setting is switched on. Existing students are not flagged all at once (the import would flood the dashboard); staff can waive per student or per type.
- Parent upload comes later through the Guardian Portal.

## 4. Late fee at the payment screen
- At collection time the cashier gets an option to add a late fee, like the existing tax and concession lines. It appears on the receipt as its own line with a snapshot of how it was calculated (rule, days late, amount). Not applying it is the waiver; the receipt records who decided. Voiding a payment voids its late fee line.
- The school configures the rule in Fee Setup (opt-in, off by default): type FLAT amount, PER DAY amount, or PERCENT of the overdue amount; grace days; a cap. Each fee type has an "applies late fee" flag (for example tuition yes, admission fee no). The rule in force when the payment is taken is saved with the receipt, so a later rule change never rewrites old receipts.
- Late fee is NOT a due. The original due and its status computation (Paid, Partially Paid, Overdue, Pending) stay untouched, and an unapplied late fee does NOT count as outstanding for the promotion dues block.
- The implementer must first read how tax and concession lines are built into fee payments and receipts today and mirror that exact mechanism; do not invent a second one. Propose how late-fee income appears in the fee dashboards and reports so figures stay consistent.

## 5. School registration
- Add a mandatory WHATSAPP number on tb_schools for new registrations and for edits, with a "same as mobile number" tick; stored with country code. Existing schools have none, so the column stays nullable in the database, the rule is enforced in the form and service, and existing schools are prompted to add it.
- Add optional LATITUDE and LONGITUDE columns on tb_schools, filled by the Super Admin only. Suggested NUMERIC(9,6) each, range checks (-90..90 and -180..180), both filled together or both empty. No special map extension is needed. School users see the location read-only.
- The school WhatsApp number is the SENDER of parent messages once the school's sender is ACTIVE (hybrid with the platform number as fallback; see claude/guardian-portal-design.md decision 13 and the sender table). Capturing the number at registration does not by itself make it a sender: registration and verification with Meta is a separate onboarding step per school.

## 6. Overall build order (all three design documents)
0. Verification (read-only) and a staged plan; wait for approval.
1. Year continuity hardening (promotion design Stage 1): enrolment foreign keys, unique roll numbers, next_class_id, master guards, selectable past years, year-scoped dashboard, receipts and profile history, per-year concessions, receipt fee-type name snapshot.
2. Student and admission changes (this document sections 1 to 3 and 5): optional last name, mandatory guardian WhatsApp and email, same-as-mobile tick, two-step admission, student documents with dashboard and profile indicators, school WhatsApp and latitude and longitude.
3. Guardian foundation (guardian design G1): sibling pick, guardian reuse, duplicate report and merge, mobile normalisation, portal access flag, GUARDIAN role.
4. Promotion (promotion design Stage 2): preview, dues block with School Admin or Principal override, promote, repeat, pass out, pending-promotion list.
5. Late fee (this document section 4).
6. Parent account and delivery (guardian design G2): auto account, generated login id, set-password link, WhatsApp and email delivery behind the interface with the log-only sender, forgot-password, student login by class teacher or School Admin.
7. Parent access and screens (guardian design G3 and G4): access helper, children endpoints, web and mobile family screen and child dashboard.
8. Status, leaving and transfer certificate, mid-year section change (promotion design Stage 3); fee structure copy to a new year.
Claude Code may propose a different order if it finds a dependency; it must say so and wait for approval.

## 7. Rules for the implementer (Claude Code)
- Design first, stage by stage; report findings and wait for approval before each stage.
- Verify the live database first and list rows that would violate a new rule before adding it.
- Idempotent RUN-ME migrations in Docs/ (S1-S7 pattern); pg_dump backup first; do not push; do not commit unless asked.
- Never trust school_id from the client; DTOs; the standard API envelope; async with CancellationToken; no secrets in logs.
- Tests for each rule; real-database smoke checks with throwaway accounts only; fingerprint existing tables unchanged.

## 8. OPEN
- Whether the document deadline setting is wanted (proposed optional, default off).
- How late-fee income should be reported (implementer proposes).
- Hybrid sender and the set-password link need the owner's confirmation (see the guardian design document).
