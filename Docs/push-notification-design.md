# SMSS — Push Notifications (Firebase) — Fee Overdue Reminders

Status: DESIGNED 2026-11-19, not built. Scope decided with the user: mobile app only, Student login recipient, daily from due date until paid, dev build/EAS for the mobile app.

## 1. Requirement and decisions

A student with an overdue fee installment gets a push notification every day until the overdue amount is paid.

- **Channel:** smss-mobile (Student app) via Firebase Cloud Messaging. No web push in v1.
- **Recipient:** the Student login. No Guardian login exists yet. Tokens attach to `tb_users`, so a Guardian login works with no schema change once it exists (same forward-compatibility as Notices, section 38 of roles-workflow.md).
- **Trigger:** daily, one combined message per student (not one per installment), sent at a per-school time (default 09:00 India time). Starts after due date plus a per-school grace period (default 0 days). Stops automatically because "overdue" is derived from `tb_student_fee_dues` (section 28): once `amount_paid >= amount_due` on every due, nothing is sent. A voided payment makes the student eligible again, with no extra code.
- **Opt-in per school:** off by default. A School Admin enables it. A wrongly generated fee schedule should not message children before the school has reviewed it.
- **Neutral message text, no amount on the lock screen.** Example: "Fee payment pending — please clear your dues at the school office." Amounts belong inside the app, not on a lock screen a classmate can read. The data payload carries only `{type: "FEE_OVERDUE"}`: no student id, no amount.

**Previous decision:** no scheduler or background-job infrastructure (Notices section 38, Subscription & Billing section 39 both avoided it).
**New decision:** introduce one in-process .NET `BackgroundService`.
**Reason:** "every day" cannot be manual. This is the first real requirement for scheduling.
**Impact:** limited to this feature. Subscription invoicing stays manual. No Hangfire, Quartz, Redis or message broker (project rule 28).

## 2. Architecture

```
BackgroundService (tick every 5 min, IST)
   -> for each school with fee_reminder_enabled
        run when now_IST >= send_time AND last_run_date < today_IST
        pg_try_advisory_lock(per school)           (safe if the API ever runs 2 instances)
   -> FeeReminderService.GetOverdueStudents(school)   (reuses the Fee module's overdue definition)
   -> insert tb_push_notification_log rows            (unique per student+type+day = idempotent)
   -> FcmSender.SendEachAsync (batches of <= 500)     (FirebaseAdmin NuGet)
   -> update log row status; deactivate dead tokens
   -> set last_run_date = today_IST
```

- Catch-up behaviour: if the API is down at 09:00, the next tick after 09:00 the same day runs it. If it is down all day, that day is skipped. There is no backfill of missed days.
- Use the project's existing India-date helper (Attendance uses it). Do not use `DateTime.Today`.

## 3. Schema (new migration `Docs/smss-push-notifications.sql`, additive only)

### `tb_user_devices`
- `device_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `user_id` BIGINT FK → tb_users, `school_id` VARCHAR(100) FK → tb_schools (derived from the token on write)
- `fcm_token` VARCHAR(1024) NOT NULL, **UNIQUE**. A token belongs to one user at a time. On register, upsert and reassign to the current user, which handles a shared phone where a different student logs in.
- `platform` VARCHAR(10) CHECK IN ('ANDROID','IOS'), `device_label` VARCHAR(100) NULL, `app_version` VARCHAR(30) NULL
- `is_active` BOOLEAN default true (the project-standard name), `last_seen_at` TIMESTAMPTZ, `created_at`, `updated_at`
- Index `(user_id) WHERE is_active`

### `tb_school_notification_settings` (one row per school; no row = defaults)
- `school_id` VARCHAR(100) PK FK → tb_schools
- `fee_reminder_enabled` BOOLEAN default false
- `fee_reminder_send_time` TIME default '09:00' (India time)
- `fee_reminder_grace_days` SMALLINT default 0, CHECK 0..30
- `last_fee_reminder_run_date` DATE NULL
- `updated_by` BIGINT FK → tb_users, `created_at`, `updated_at`

### `tb_push_notification_log`
- `notification_id` BIGINT GENERATED ALWAYS AS IDENTITY PK
- `school_id`, `user_id`, `student_id` VARCHAR(20) NULL
- `notification_type` VARCHAR(30) (v1: `FEE_OVERDUE`), `notify_date` DATE (India date)
- `title` VARCHAR(100), `body` VARCHAR(300)
- `status` VARCHAR(20) CHECK IN ('PENDING','SENT','FAILED','NO_DEVICE'), `attempt_count` SMALLINT default 0
- `devices_targeted` / `devices_succeeded` SMALLINT, `error_summary` VARCHAR(500) NULL
- `created_at`, `sent_at` TIMESTAMPTZ NULL
- **UNIQUE `(student_id, notification_type, notify_date)`**: the backstop that makes the job idempotent after a restart or double run
- Index `(school_id, notify_date)`

No hard deletes. Dead tokens go `is_active = false`.

## 4. Who gets a reminder

A student qualifies when all are true:
1. The student row is Active and enrolled in the current academic year.
2. The student has an Active Student login (`tb_users`) that is not blocked.
3. At least one `tb_student_fee_dues` row has `due_date + grace_days < today_IST` and `amount_paid < amount_due`.

**Reuse the Fee module's existing overdue computation** (the grouped query behind the student list fee status and the Fee Dashboard) rather than writing a second definition. Two definitions of "overdue" would disagree, which is the kind of competing version this project avoids. Scope is the current academic year, like the dashboards. Arrears from a previous year are not reminded in v1.

A qualifying student with no active device gets a log row with status `NO_DEVICE`. The School Admin preview shows this count, so the school knows who needs another route.

## 5. Endpoints

| Endpoint | Who | Purpose |
|---|---|---|
| `POST /api/Device/register` `{fcmToken, platform, deviceLabel?, appVersion?}` | any authenticated school user | upsert token to the caller. `school_id` and `user_id` come from the token, never the body |
| `POST /api/Device/unregister` `{fcmToken}` | same | on logout; only deactivates a token owned by the caller |
| `GET/PUT /api/NotificationSettings` | School Admin (`IsSchoolAdmin`) | the three settings above |
| `GET /api/NotificationSettings/fee-reminders/preview` | School Admin | dry run: how many students would be reminded today, how many have a device. Sends nothing |
| `GET /api/NotificationLog?studentId=&from=&to=` | School Admin | audit: did this student get it |

Standard response envelope, DTOs, centralized error handling. FCM errors are logged internally and never returned to the client.

## 6. FCM sending

- NuGet `FirebaseAdmin` (current 3.x). Use `FirebaseMessaging.DefaultInstance.SendEachAsync(messages, ct)`, up to 500 per call. **Not** `SendAllAsync`: the old batch endpoint is deprecated and Google has disabled it, so it fails with errors such as 501.
- Credentials come from config: `Firebase:CredentialsPath` (env var `Firebase__CredentialsPath`) pointing to the service-account JSON. **Never in the repo.** The 6 to 7 Nov incident (committed secrets) is the reason. Add the filename pattern to `.gitignore`. On the VPS keep it outside the web root, for example `/etc/smss/firebase-service-account.json`, `chmod 600`, owned by the API's service user.
- `Firebase:DryRun` (bool). When true, `SendEachAsync(messages, dryRun: true)`: FCM validates but delivers nothing. **Real-db smoke tests must run with DryRun = true.** No real student gets a push from a test.
- Message: `notification{title, body}`, `data{type: FEE_OVERDUE}`, Android high priority.
- Per-token result handling:
  - `Unregistered` or invalid token → set `is_active = false`
  - transient (`Unavailable`, `Internal`, `QuotaExceeded`) → log row `FAILED`; retried on later ticks the same day, max 3 `attempt_count`
  - all tokens of a student failed permanently → `FAILED` with a short `error_summary`
- A failure for one school or student must never stop the loop for the others.

## 7. Mobile (smss-mobile)

- Needs a **development build via EAS**. Push does not work in Expo Go.
- `@react-native-firebase/app` + `@react-native-firebase/messaging` with their Expo config plugins. `messaging().getToken()` returns an FCM token on both Android and iOS. Claude Code should check the exact current plugin setup against the library docs before wiring it.
- Files: `google-services.json` (Android) and `GoogleService-Info.plist` (iOS) referenced from the app config. These are client config, not the server secret. Do not put the service-account JSON in the app.
- Flow: after a successful sign-in and after the first-login password change, request the notification permission (Android 13+ `POST_NOTIFICATIONS`; iOS prompt), get the token, call `/api/Device/register`. Re-register when the token refreshes (`onTokenRefresh`). Call `/api/Device/unregister` before logout. A permission denial must never block login or show an error.
- A foreground message shows an in-app banner (FCM does not draw one while the app is open).
- Tapping the notification opens the app. There is no Student "My Fees" screen yet, so there is nothing to deep-link to. Add the link when My Fees exists.
- iOS: Apple Developer account, an APNs auth key (.p8) uploaded in Firebase → Project settings → Cloud Messaging, and the Push Notifications capability on the app id.

## 8. Web (smss-web)

One small screen: **School Setup → Notification Settings** (School Admin): enable toggle, send time, grace days, a "Preview today" button (calls the preview endpoint), Save. Same shape as the ID Card Settings screen. No web push.

## 9. Stages

1. **Firebase setup (you, in the console), before the build:**
   1. Create a Firebase project.
   2. Add an Android app. The package name must match smss-mobile's `android.package`. Download `google-services.json`.
   3. Add an iOS app (bundle id) and download `GoogleService-Info.plist`. Upload the APNs key if you are doing iOS.
   4. Project settings → Service accounts → Generate new private key. Keep that JSON private and copy it to the VPS as in section 6. Never commit it or paste it into chat.
2. **Backend (smss-api):** migration, entities and DTOs, device endpoints, settings endpoints, `FcmSender`, `FeeReminderService`, hosted service, preview and log endpoints, tests.
3. **Mobile:** token registration and unregister, permission flow, foreground banner.
4. **Web:** Notification Settings screen.
5. **Deploy:** backup `smss-db-before-push-notifications.dump`, run the migration, put the credentials on the VPS and set the env var, restart the API, smoke-test with throwaway accounts and DryRun = true, then one real test with a throwaway student login on your own phone.

## 10. Tests (the project's usual bar)

- Eligibility: each of the three conditions flips the result. Grace days boundary (due today with grace 0 is not yet overdue, due yesterday is). Partial payment still overdue. Full payment stops it. Void makes it eligible again. Inactive student and blocked login are excluded.
- Idempotency: run the job twice the same day and get one log row per student. Two concurrent runs send once (advisory lock plus unique index).
- Catch-up: the tick after send time runs it. A tick before send time does not.
- Device rules: a token reassigned to a new user stops going to the old user. An unregister by a non-owner is a no-op. A dead token gets deactivated.
- Authorization: Device endpoints work for any school user and never trust a client `school_id`. Settings, preview and log are School Admin only (403 for others, other schools 404).
- Cross-tenant: school A's job never reads school B's students or tokens.
- Real smss-db smoke test: throwaway accounts only (standing rule), DryRun = true, fingerprint the existing tables before and after, delete everything created.

## 11. Known limits and deferred

- Arrears from earlier academic years are not reminded.
- No maximum-days cap. Daily until paid is what was asked for. A cap such as stop after 30 reminders is a cheap addition if schools complain about fatigue.
- No in-app notification history for the student, no Guardian recipient, no web push. The log table is already shaped to feed an in-app list later.
- Only one reminder type (`FEE_OVERDUE`). The log and sender are generic, so absence alerts, homework and notice pushes can reuse them.
- Reminders reach only students who have installed the app, signed in and allowed notifications. The preview's no-device count shows how many that leaves out.

## 12. Build prompt for Claude Code (backend stage)

> You are working in smss-api (ASP.NET Core, EF Core, PostgreSQL, layered controllers / service / db layer). Read `claude/push-notification-design.md` (sections 1 to 6 and 10) and follow it exactly. Before writing code, find and report: (a) the existing overdue computation behind the student-list fee status and the Fee Dashboard, and how you will reuse it, (b) the India-date helper used by Attendance, (c) how per-school advisory locks are taken for fee receipt numbers, (d) the existing IsSchoolAdmin check and the response envelope. Do not create a second definition of overdue. Build: migration `Docs/smss-push-notifications.sql` (additive only, idempotent, no change to existing tables), entities with explicit [Table]/[Column] attributes, DTOs, `DeviceController`, `NotificationSettingsController`, `NotificationLogController`, `IFcmSender`/`FcmSender` (FirebaseAdmin, `SendEachAsync`, `Firebase:CredentialsPath`, `Firebase:DryRun`), `FeeReminderService`, and a `BackgroundService` that ticks every 5 minutes. Add `.gitignore` entries for service-account JSON files. Never log tokens, credentials or full message bodies. Do not touch the Fee, Student or Auth modules except to call their existing services. Run the full regression plus the new checks from section 10 on the scratch DB copy, and report which tests ran. Do not run anything against the real smss-db and do not send a real push: stop after the scratch-DB results and wait for my go-ahead to deploy.
