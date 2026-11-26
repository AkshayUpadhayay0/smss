# Endpoint authorization audit (26 Nov 2026)

Two automated guards now stand behind this audit (`smss-api-tests`): `EndpointAuthorizationGuardTests` and `EndpointRoleMatrixTests` (golden file `golden/endpoint-role-matrix.txt`).

## 1. Every controller action and its authorization (553 actions, 89 controllers)
| Kind | Actions | How it is guarded |
|---|---|---|
| `[Authorize]` (any signed-in user; roles are then checked in code or in the service) | 468 | the guard test fails when an action has none |
| `[SuperAdminOnly]` filter (role matched by NAME in one place) | 70 | same |
| `[AllowAnonymous]` | 12 | on the allow-list below, each with a reason |
| no attribute at all | 2 (`Home.Get`, `SystemSetup.BootstrapSuperAdmin`) | on the allow-list below |

There is no `[Authorize(Roles = ...)]` attribute on any action any more (the old commented-out ones are in `MasterDataController`, which is Super-Admin only by its filter). Role rules live in code (`User.CanPromoteStudents()`, `Refuse()` helpers) or in the service (class-teacher, own-record and access-helper checks). That is why the second guard exists.

### The allow-list (the ONLY anonymous actions) and why
| Action | Reason |
|---|---|
| `AuthController.Login` | signing in: no token yet; per-login-id and per-address throttle |
| `AuthController.Refresh` | trades a single-use refresh token (the access token has expired) |
| `AuthController.Logout` | ends a session with the refresh token in the body; it can revoke only its own session |
| `ParentAuthController.CheckLink`, `SetPassword` (covers reset too), `Forgot` | the public parent pages: the hashed single-use link token is the credential; per-address and per-contact limits; `Forgot` answers the same for any contact |
| `PublicEnquiriesController.Submit` | the marketing site's Contact / Demo form: honeypot, validation, rate limit |
| `PublicPartnerSchoolsController.List` | only schools the Super Admin switched on, only name, logo, city and state; cached; rate limited |
| `WhatsAppWebhookController.Verify`, `Receive` | Meta calls them: verify token / `X-Hub-Signature-256`; 404 unless configured |
| `DriverDutyController.Location`, `End` | the driver phone's background task uses the duty TRACKING TOKEN, never the JWT |
| `HomeController.Get` | a one-line greeting, no data |
| `SystemSetupController.BootstrapSuperAdmin` | 404 unless `Bootstrap:Enabled`, needs `X-Setup-Key`, refuses once a Super Admin exists (set `Bootstrap__Enabled=false` in production) |

## 2. Who can reach what (the behavioural guard)
For each of the 468 signed-in, non-Super-Admin actions the test calls the action with fake services behind it, as a user holding ONLY one role (Student, Guardian, Teacher, Accountant, Admission Staff, Store Keeper, Driver, Principal, School Admin), and records whether the call got through to the service layer. The result is the golden file; a new endpoint that a role can reach, or a role that stops being gated, changes a line and fails the build until the file is rewritten on purpose (`SMSS_WRITE_GOLDEN=1`).

Reaches the service, per role (of 468): Student 99, Guardian 81, Teacher 118, Accountant 101, Admission Staff 99, Store Keeper 114, Driver 83, Principal 203, School Admin 385. Most of the Student / Guardian / Driver number is the shared read-only lookups (years, classes, sections, subjects, fee types and structures, masters, calendar, notices, the Family endpoints, which are guardian- or student-only inside the service).

**Writes that a Student, Guardian or Driver login reaches the service for, and where each is refused** (all read in the code):
| Action | The check |
|---|---|
| `AttendanceController.Submit` | `MayMarkAsync`: only the section's class teacher or a School Admin (403) |
| `StudentLeaveController.Apply`, `Cancel`, `Decide` | `MayHandleAsync`: only a School Admin or the student's class teacher (403); the request is looked up by the token's school |
| `StudentLeaveController.ApplyMy`, `CancelMy` | the login's own student record only |
| `EmployeeLeaveController.Apply`, `Cancel` | yourself (or a School Admin on your behalf); only the applicant or a School Admin may cancel |
| `PasswordResetController.Reset` | the service: Super Admin (a School Admin's login) or School Admin (any login of their OWN school); everyone else 403 |
| `AuthController.ChangePassword`, `DeviceController.Register`, `Unregister` | the caller's own account / device |

No real hole was found, so there is no fix commit.

## 3. Does every id belong to the token's school?
- **The school is never taken from the client.** 36 actions carry a `schoolId` in the URL (Student, Admission, Document Type, Onboarding, School Registration, Subscription, WhatsApp sender). Every school-level one compares it with the token's school and answers 403 (`RejectForeignSchool` / `Gate`); the ones that really take another school (School Registration, Subscription, Invoices, Super Admin schools) are `[SuperAdminOnly]`, and `WhatsAppSender.Save` is Super-Admin-only in code.
- **Repositories.** 226 repository methods take an id with no school parameter. Almost all are records inside a result, global lookups (masters, genders, blood groups) or platform tables (company finance, enquiries, subscription invoices: Super Admin only). The ones that look up by id alone were read at their call sites and each runs after a school-scoped load: assessment marks (`LoadForChangeAsync(schoolId, ...)`), payment allocations (`LockPaymentAsync(schoolId, ...)`), fee-structure lines (structure loaded by school), back-dating (`GetByIdAsync(schoolId, ...)` first), report cards and bulk cards (`AuthorisePublicationAsync`), notices (`GetRowAsync(schoolId, ...)`), the report-entry screens (exam subject, scheme, term and section are all resolved with the school id before the id-only reads).
- **Receipts / certificates / leavers / section changes:** school-scoped in the SQL and covered by cross-school 404 tests (6c, 8a, 8b, 8c).
- **Not proven by reading alone:** nothing found missing. Two small notes: `NoticeController.Get` answers 403 (not 404) for a notice of the same school that is not addressed to the caller (no cross-school leak); and the per-action cross-school tests exist for each feature stage, not for all 468 actions one by one.

## 4. How to keep it true
A new action without `[Authorize]` fails `Every_action_has_explicit_authorization_or_is_on_the_allow_list_with_a_reason`. A new action a Student, Guardian or other role can reach fails `The_roles_that_can_reach_each_signed_in_action_are_exactly_the_golden_matrix` until the golden line is reviewed. A GET named like a write fails `Every_non_anonymous_action_that_writes_is_a_POST_PUT_or_DELETE_never_a_GET`.
