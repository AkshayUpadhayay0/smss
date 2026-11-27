# Guardian / Parent Portal — design record (updated 24 Nov 2026)

Status: DESIGN ONLY. Nothing built. Agreed in discussion with the owner on 24 Nov 2026. Items marked OPEN or PROPOSED are not confirmed by the owner.
Basis: a read-only Claude Code investigation of the code and Docs/*.sql plus the 14 Nov pg_dump. The live database was NOT queried (lut_roles contents unknown). The mobile app was searched and has no guardian handling.
Related: claude/student-year-promotion-design.md, claude/admission-docs-latefee-registration-design.md.

## 1. Problem
A family with several children has no parent login. A parent can only use a student login, which sees one child, so a parent of three children needs three logins. Student-login credentials are emailed to the student's email, else the primary guardian's, so a parent ends up holding a child's credentials.

## 2. What exists today (verified in code)
- tb_guardians: guardian_id (identity PK), school_id, first_name (holds the FULL name; last_name never filled), relationship, mobile_number (free text up to 20, validated as 10 digits only in the request DTO), email (optional), occupation, status_id. No unique key and no index on mobile, email, name or school_id.
- tb_student_guardians: student_guardian_id, student_id, guardian_id, relationship, is_primary, is_emergency_contact, can_pickup_student. Unique (student_id, guardian_id). No school_id. Nothing enforces one primary per student. Many-to-many is allowed by the schema, but the code never links one guardian to more than one student.
- Every admission, direct entry and Excel import ALWAYS inserts a new tb_guardians row; no lookup or reuse. Siblings become separate guardian rows for the same person (the demo seed does the same). Excel import takes up to three guardians (name and phone only, no email).
- No guardian login anywhere: no endpoint, service, web page or mobile screen. Web dashboard shows "coming soon"; the mobile app would show its unsupported screen. Constants exist (RoleCodes.Guardian, UserTypes.Guardian, IsGuardianLogin helper). A GUARDIAN row in lut_roles is NOT confirmed (no SQL inserts it); creating a guardian login without it would 500.
- Login resolves lower(org_user_id) across ALL schools and takes the first match, so any login id must be unique platform-wide.
- Every "my ..." endpoint derives the student from the token (tb_users.org_user_id = student_id). Nothing asks "is this student linked to the logged-in guardian". tb_users and tb_guardians have no link.
- Notices already resolve a Guardian tier. Employee and driver allow-lists exclude Guardian. No leak path was found; a guardian would mostly get 403.
- Student login is created on demand by POST api/Student/{schoolId}/{studentId}/create-login (School Admin only). Its temporary password is built from the first name and date of birth (guessable).
- Fee reminders (push design) go to the student's own user id only.
- Duplicate-guardian risk today: HIGH.

## 3. Decisions
1. The family screen is ALWAYS shown after a parent login, even with one child. One card per linked child.
2. Siblings are linked at admission by picking the sibling: the admin chooses the class, then the sibling's name. The sibling's guardians are shown with a tick box each (siblings may not share both parents); the admin ticks which apply and may add another guardian. A mobile-number match is only a hint ("a guardian with this number already exists, same person?"), never a silent merge. If every sibling has passed out, the hint is the fallback.
3. Parent details are MANDATORY when a student is added: the primary guardian needs name, relationship, mobile, WhatsApp number and email. A "same as mobile number" tick fills the WhatsApp field. Numbers are stored with the country code (+91), because Meta requires it. Previous: email optional, no WhatsApp field. Impact: admission application form, direct entry, Confirm Admission, Excel import template.
4. A Parent login is created AUTOMATICALLY when an admission is confirmed (see the two-step admission in the admission design doc), for the PRIMARY guardian only; other guardians get one on request. A sibling linked to an existing guardian does NOT create a second account; the guardian is told the child was added.
5. The parent receives a GENERATED LOGIN ID (school short code + GD + number, platform-unique, never equal to a student id) by WhatsApp and by email, plus a one-time link to set their own password (expires, for example 48 hours). PROPOSED, owner has not confirmed: the password is set through the link and is NOT sent in the message. Reason: WhatsApp reserves one-time passcodes for authentication templates, which allow only the preset format with a code button and no URLs; a temporary password in a utility template risks rejection.
6. Previous decision: no guardian code, parent types any linked student's id. New decision: generated guardian login id. Reason: a student id plus the student's password cannot tell a parent from a child, student passwords are guessable, student ids are sequential, and a family screen behind that login would leak siblings' data.
7. A student login is NOT created automatically. It is created on demand by the class teacher for students of their OWN section, and by the School Admin. PROPOSED: its credentials go to the parent's WhatsApp and email, not the child's.
8. A student login can never open the family screen. Parent login is its own login mode.
9. Siblings are derived (students sharing a guardian). No separate family table.
10. Portal access flag per guardian-to-student link (default on); a child with access off does not appear for that guardian (custody cases). PROPOSED.
11. Duplicate guardians of existing students are cleaned by a report of likely duplicates (same school, same normalised mobile, similar name) and an admin-confirmed merge: links move to the surviving guardian, the others are deactivated, never deleted. PROPOSED.
12. Fee reminders to guardians: one neutral message per guardian per day, no amount and no child name on the lock screen. PROPOSED.
13. WhatsApp: Meta WhatsApp Cloud API used directly (no middle provider). The SENDER is the school's own WhatsApp number (owner's choice). PROPOSED hybrid: each school has a sender status NOT_REGISTERED, PENDING or ACTIVE; until ACTIVE, messages go from the platform's own number with the school name in the text; once the school completes registration its own number takes over. Reason: a number on the Cloud API generally cannot also run in the normal WhatsApp app, every school's number must be registered and verified with Meta, and messages are billed to the account that owns the number.

## 4. Family screen and child dashboard (owner's mockup, with agreed changes)
- Child cards: photo, name, student id, class, and a small status chip (today's attendance, fee due, or passed out / pending promotion). Tap opens that child's dashboard with a header switcher to move between children.
- Fee tile: "Paid" only when every child is paid; otherwise the total due now with a per-child breakdown. Each child's fees are for that child's own enrolment year.
- Buttons: School Notifications with an unread count; Academic Calendar.
- Child dashboard (read-only to start): profile, attendance, homework, timetable, exam schedule, published report cards, fees and receipts, notices, leave.
- A child who has passed out or is pending promotion stays visible as read-only history (see claude/student-year-promotion-design.md).
- Student photos are public URLs today (open item in the project docs): move to private storage before guardians depend on them.

## 5. Security and access
- One server-side helper answers "which student is this request for": a Student login gets its own record; a Guardian login gets the requested child ONLY IF the child is linked to that guardian with portal access on. Existing services are reused behind it instead of copying every "my" endpoint. The guardian comes from the token, never from the request; studentId in a request is always checked against the link. Cross-school access returns 404.
- A Guardian login cannot hold the Student role. A guardian login id can never equal a student id.
- Passwords are set by the parent through the one-time link; no password is sent, logged or stored in the delivery log. Forced change rules still apply to any generated secret. Link tokens are single-use, expire, and are stored hashed.
- A forgot-password flow (WhatsApp authentication-template code or a reset link by email) and a School Admin reset are REQUIRED for parents before launch. Today no reset or forgot-password feature exists (queued in exam-report-card-design.md section 17).
- Rate limit login and reset attempts; same message whether or not an id exists.
- Sending runs AFTER the admission transaction commits (like FeeAutoAssignment today). A WhatsApp or email failure never blocks admission; it is logged and shown with a resend action.
- The attendance-by-student endpoint is safe today only because of a service-level check; add an explicit role check.

## 6. Data changes (proposed, implementer finalises names, constraints, indexes)
- tb_guardians: add whatsapp_number; normalise mobile and WhatsApp to one stored format (+91 default); email required for the primary guardian (service-level rule, existing rows are NULL); define first_name as the full name or fill last_name; index on (school_id, normalised mobile).
- tb_student_guardians: add portal_access boolean default true; partial unique index so one primary guardian per student.
- Link between tb_users and tb_guardians (one account per guardian). tb_users.org_user_id holds the guardian login id.
- One-time activation / reset token table (hashed token, user, purpose, expiry, used_at).
- Credential delivery log table (user, channel WHATSAPP or EMAIL, status, provider message id, attempts, error, timestamps; never a password or token).
- School WhatsApp sender table (school_id, WhatsApp number, phone number id, status NOT_REGISTERED / PENDING / ACTIVE, verified_at).
- Seed the GUARDIAN row in lut_roles (check the live table first).
- Guardian edit endpoint (none exists; a parent's number change must update contact and login).
- Duplicate report and merge audit.

## 7. WhatsApp and email integration
- Provider: Meta WhatsApp Cloud API. Code goes behind one interface (for example IWhatsAppSender) with a real Cloud API implementation and a LOG-ONLY implementation. The log-only sender is the default in development and tests, so everything can be built and tested before the key exists; switching to real sending is configuration only.
- Configuration (never in appsettings.json or any committed file; user-secrets locally and environment variables on the server): WhatsApp:Enabled, WhatsApp:Sender (Log or Cloud), WhatsApp:AccessToken (permanent System User token), WhatsApp:PhoneNumberId (platform default sender), WhatsApp:BusinessAccountId, WhatsApp:ApiVersion, WhatsApp:AppSecret, WhatsApp:WebhookVerifyToken, and one setting per template name. The project already had to rotate committed secrets once (6 to 7 Nov incident); .gitignore any secret file pattern.
- Templates the owner must create and get approved in Meta (all WhatsApp business-initiated messages need an approved template): parent_account_ready (Utility; variables child name, school name, login id; button with the set-password link), parent_child_added (Utility), parent_login_code (Authentication, preset format with the code button), later a fee-overdue reminder (Utility). Passcodes may only travel in authentication templates.
- The number must be registered with the Cloud API; opt-in consent from parents is required (consent statement on the admission form); a payment method is needed on the WhatsApp account.
- Email: reuse the existing NotificationService email sender for the same activation message.
- Delivery status and a resend action are visible to the School Admin; a failure never blocks admission.

## 8. Stages
- G0: verification and owner decisions (this document).
- G1 Data foundation: mandatory parent details (form, direct entry, confirm, Excel template), sibling pick, guardian reuse, mobile normalisation, portal_access, one-primary index, GUARDIAN role, duplicate report and merge, guardian edit endpoint, school WhatsApp sender table.
- G2 Parent account and delivery: auto-create the parent account at Confirm Admission, generated login id, one-time activation link, WhatsApp and email delivery with log and resend (log-only sender until the key is added), forgot-password, parent login mode, token claims. Existing students: after the cleanup, an admin action to create and send parent logins in bulk, plus a "complete parent details" list for missing WhatsApp or email.
- G3 Access and endpoints: the access helper, children list with status chips, read-only child endpoints, student-login creation by class teacher (own section) and School Admin.
- G4 Screens: web guardian dashboard and mobile guardian role (family screen, child dashboard).
- G5 Later: push notifications and fee reminders to guardians, leave applications by parents, online fee payment, parent document upload.

## 9. Rules for the implementer (Claude Code)
- Design first, stage by stage; report findings and wait for approval before building each stage.
- Verify the live database first (lut_roles, guardian data, mobile formats) and list rows that would violate a new rule before adding it.
- Idempotent RUN-ME migrations in Docs/ following the S1-S7 pattern; pg_dump backup first; do not push; do not commit unless asked.
- DTOs, the standard API envelope, async with CancellationToken, no secrets, passwords or tokens in logs.
- Tests for: sibling link, guardian reuse, access helper (guardian sees only linked children, 403 or 404 otherwise, portal_access off hides a child), student login cannot open the family screen, delivery failure does not block admission, activation link single-use and expiry, forgot-password, class teacher can create logins only for own section.

## 10. OPEN
1. Owner confirmation that the parent sets the password through a one-time link (no password in the message).
2. Owner confirmation of the hybrid sender (platform number until the school's own number is ACTIVE).
3. Whether older admission applications missing WhatsApp or email are completed at confirmation or imported with a "complete later" list.
