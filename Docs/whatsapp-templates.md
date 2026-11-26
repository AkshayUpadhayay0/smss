# WhatsApp message templates for Meta (parent accounts) — to submit in WhatsApp Manager

Status: for the owner to submit. Written 26 Nov 2026 with stage 6a. Nothing is sent until the templates are APPROVED, the settings in `Docs/configuration.md` ("WhatsApp and parent accounts") are filled in, and `WhatsApp:Enabled` is `true`.

## Rules these texts follow (Meta's template rules)
- Category **UTILITY** for all of them (they are about an account the parent already has a relationship with). Do **not** submit them as Marketing. Meta decides the final category; if it re-categorises one as Marketing, edit the text (remove anything promotional) and resubmit.
- Language **English (en)**. (A Hindi version of each can be added later under the same name; the code sends `WhatsApp:Language`.)
- A variable never starts or ends the body, and two variables are never next to each other.
- **No password is ever in a message.** Every message with a link uses the button: the button's URL has ONE variable at the end, and that variable is the one-time token. The token is single use and expires (72 hours for a set-password link, 1 hour for a reset link).
- The school's name is a variable in every template, so a message sent from the platform's number (until the school's own sender is ACTIVE) still says which school it is from.
- Variables are written `{{1}}`, `{{2}}` … in the order below. The code builds them in `ParentTemplates` (service layer); a test checks that this file and the code agree on the template names and the number of variables.
- Replace `https://YOUR-SITE` with the real website address (the same value as `ParentAccounts:WebBaseUrl`).

---

## 1. `parent_account_ready`
- **Category:** Utility  **Language:** English (en)
- **Sent when:** a parent account is created (at Confirm Admission, or by the School Admin's bulk action).
- **Header / footer:** none.
- **Body:**
  ```
  Hello {{1}}, {{2}} has been admitted to {{3}}. Your parent login ID is {{4}}. Use the button below to choose your own password. The link works once and expires in {{5}} hours.
  ```
- **Variables:** `{{1}}` parent's name · `{{2}}` child's name · `{{3}}` school name · `{{4}}` parent login ID · `{{5}}` hours the link works (72)
- **Button:** type **Call to action → Visit website → Dynamic**. Text `Set password`. URL `https://YOUR-SITE/parent/set-password?t={{1}}` (the one button variable is the one-time token).
- **Sample values for Meta's review:** `Asha Rao`, `Ravi Rao`, `Green Valley School`, `GVS001GD001`, `72`; button sample `Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV-qA_MtI`

## 2. `parent_child_added`
- **Category:** Utility  **Language:** English (en)
- **Sent when:** another child is linked to a parent who already has an account (a sibling). No second account is created; the child appears on the family screen by itself.
- **Body:**
  ```
  Hello {{1}}, {{2}} of {{3}} has been added to your parent login. Your login ID is {{4}}. Sign in to see all your children together.
  ```
- **Variables:** `{{1}}` parent's name · `{{2}}` child's name · `{{3}}` school name · `{{4}}` parent login ID
- **Button:** **Call to action → Visit website → Static**. Text `Open SMSS`. URL `https://YOUR-SITE/login` (no variable).
- **Sample values:** `Asha Rao`, `Meera Rao`, `Green Valley School`, `GVS001GD001`

## 3. `parent_login_help`  (login help: "Forgot id or password")
- **Category:** Utility  **Language:** English (en)
- **Sent when:** a parent asks for their login ID or a new password with their registered WhatsApp number or email. It carries the login ID and a one-hour reset link. The answer on screen is the same whether or not the number matched; a number that matches parents in several schools gets one message per school.
- **Why this is Utility and not Authentication:** it carries the login ID and a reset **link**, not a code, and an Authentication template may carry only a one-time code and a copy-code button (decision of 26 Nov 2026: renamed from `parent_login_code` to `parent_login_help`).
- **If Meta rejects or re-categorises a template**, no code change is needed: the channel used for each purpose is a setting (`ParentAccounts:Channels:<PURPOSE>`, see `Docs/configuration.md`), so for example the reset link can go by email only until the template is approved.
- **Body:**
  ```
  Hello {{1}}, we received a request to recover your parent login for {{2}}. Your login ID is {{3}}. Use the button below to choose a new password. The link works once and expires in {{4}} minutes. If you did not ask for this, please ignore this message.
  ```
- **Variables:** `{{1}}` parent's name · `{{2}}` school name · `{{3}}` parent login ID · `{{4}}` minutes the link works (60)
- **Button:** **Call to action → Visit website → Dynamic**. Text `Choose new password`. URL `https://YOUR-SITE/parent/reset-password?t={{1}}`.
- **Sample values:** `Asha Rao`, `Green Valley School`, `GVS001GD001`, `60`; button sample `Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV-qA_MtI`

## 4. `parent_student_login_ready`
- **Category:** Utility  **Language:** English (en)
- **Sent when:** the class teacher (own section) or the School Admin creates a student's login. The password is never sent; the link goes to the primary guardian's WhatsApp and email.
- **Body:**
  ```
  Hello {{1}}, a student login has been created for {{2}} at {{3}}. The student's login ID is {{4}}. Use the button below to set the student's password. The link works once and expires in {{5}} hours.
  ```
- **Variables:** `{{1}}` parent's name · `{{2}}` student's name · `{{3}}` school name · `{{4}}` the student's login ID · `{{5}}` hours the link works (72)
- **Button:** **Call to action → Visit website → Dynamic**. Text `Set password`. URL `https://YOUR-SITE/parent/set-password?t={{1}}`.
- **Sample values:** `Asha Rao`, `Ravi Rao`, `Green Valley School`, `GVS001ST014`, `72`; button sample as above.

## 5. Fee reminder (later — not part of 6a, do not submit yet)
- Name will be `parent_fee_reminder`, Utility. One neutral message per guardian per day, no amount and no child name in the part shown on the lock screen. The text and variables will be written when the fee-reminder stage is designed.

---

## Where each template is used
| Template | Purpose in the delivery log | Built in |
|---|---|---|
| `parent_account_ready` | `SET_PASSWORD` | 6b (account creation) |
| `parent_child_added` | `CHILD_ADDED` | 6b (sibling link) |
| `parent_login_help` | `RESET_PASSWORD` | 6b (forgot id or password) |
| `parent_student_login_ready` | `STUDENT_LOGIN` | 6b (student login by link) |

In stage 6a the sender, the templates' variable builders, the delivery log and the webhook exist and are tested; nothing calls them yet except the tests.

## After Meta approves them
1. Set `WhatsApp:TemplateParent…` only if Meta made you use a different name (the defaults are the names above).
2. Set the settings listed under "WhatsApp and parent accounts" in `Docs/configuration.md` as user-secrets / environment variables on the server.
3. Register the webhook in Meta's app dashboard: callback URL `https://YOUR-API/api/webhooks/whatsapp`, verify token = the value of `WhatsApp:WebhookVerifyToken`, subscribe to the `messages` field.
4. Switch `WhatsApp:Enabled` to `true` and `WhatsApp:Sender` to `Cloud`. Until then the log-only sender is used and the delivery log shows `LOGGED`.
