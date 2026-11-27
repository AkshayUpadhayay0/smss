# Stage 5 (late fee) and Stage 6 (parent accounts and messaging) build log

Status: built and committed locally on branch 24_nov, NOT pushed. Real database (smss-db) migrated for 5a-1, 6a (each with a pg_dump backup first). Where this log differs from the older design docs (admission-docs-latefee-registration-design.md section 4, guardian-portal-design.md), this log wins.

Baseline after 6c: backend 21 failed (same old 21) / 2647 passed / 19 skipped / 2687 total; web 1721 passing.

## Stage 5 Late fee
- Optional per school (tb_school_late_fee_rules, off by default) and per fee type (tb_fee_types.applies_late_fee, default off). Rule types FLAT, PER_DAY, PERCENT; grace days (calendar days beyond grace only); cap per due; optional tax on the late fee (school setting).
- Computed per late due by the server inside the payment transaction after the per-school receipt lock, using the school's India date; client sends only applyLateFee and a note. Charge per due = total as of today minus late fee already collected on that due by non-voided payments. Voiding a payment frees its late fee.
- Stored on tb_fee_payments (late_fee_amount, late_fee_tax_amount, decision, applied_by, note, rule snapshot) and per due in tb_fee_payment_late_fees (not an allocation; dues untouched). amount = fee_amount + late_fee_amount + tax_amount.
- Waiving needs a note (min 5 chars); receipt records who. Concession never discounts late fee. Unapplied late fees never count toward the promotion dues block. No backfill; old receipts unchanged (verified by hash).
- Reporting: separate "Late fee income" line; existing fee tiles unchanged and labelled "Fees only"; Accountant dashboard late fee and "Total received" tiles; collection report late fee and total columns; Late fee report (Charged and Waived tabs). Roles: School Admin configures; School Admin and Accountant apply or waive; Principal view-only.

## Stage 6 Parent accounts and messaging
- Guardian login id = school short code + GD + number, platform-unique (case-insensitive), allocated under a lock; only this id logs in (no login by WhatsApp or email). Parent sets the password through a one-time link (72 hours; reset links 1 hour; stored hashed; single use; newer link revokes older); no password is ever sent. Resend: first link plus three resends per 24 hours per guardian and purpose.
- Account creation after Confirm Admission, best-effort, for the primary guardian when consent is recorded and at least one channel exists; a sibling's guardian with an account gets the "child added" message. Bulk creation for existing guardians: School Admin, per school, preview, max 100 per run, only guardians with WhatsApp and email, school confirms parent consent (audited).
- Consent tick required on the admission form, Add Existing Student, and the import template; stored with date and recorder.
- Sender: IWhatsAppSender, log-only by default; Meta Cloud sender only when WhatsApp:Enabled, Sender=Cloud, token and phone number id set; platform number until the school's sender status is ACTIVE. Per-purpose channel switch (ParentAccounts:Channels:<PURPOSE>). Webhook /api/webhooks/whatsapp returns 404 unless configured, signature checked. Templates in Docs/whatsapp-templates.md (parent_account_ready, parent_child_added, parent_login_help (Utility; renamed from parent_login_code), parent_student_login_ready).
- Forgot id or password: identical answer whatever was typed, send queued after the response (timing-safe), per-IP and per-target rate limits. Reset or set-password ends sessions. Merged or inactive guardians cannot log in.
- Student logins: created by School Admin or the class teacher of the student's own section, no password, one-time link to the primary guardian; the guessable name-plus-date-of-birth password is gone.
- Access helper IStudentAccessService decides which student a request is for (student own record; guardian only linked, portal_access on, Active, same school; else 404). Read-only api/Family endpoints (children list with fee tile, profile, attendance, homework, timetable, exam schedule, report cards, fees, receipts, leave, photo, notices); Docs/family-endpoints.md. Parents see no other guardians' contacts, waived late fees or concession reasons.
- Student photos are already private (the older docs saying public are out of date).

## Open follow-ups
- Homework attachments are not exposed to parents (download rule is by section); add later.
- Parent leave apply and cancel are Student-only for now.
- Meta setup is the owner's task: templates submitted with the real site address, number registered, verified business, payment method, permanent token in user-secrets or environment variables; fill ParentAccounts:WebBaseUrl.
- Privacy Policy text must mention WhatsApp and email contact before real parent messages.
- Still to build: 7a web family screens, 7b mobile, Stage 8 (status, leaving, transfer certificate, mid-year section change, login handling for passed-out students).
- Nothing pushed; secrets rotation and repo visibility check still to be confirmed before any push.
