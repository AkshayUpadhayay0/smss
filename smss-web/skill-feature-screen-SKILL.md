---
name: smss-feature-screen
description: Build a new business feature screen in smss-web (list, add/edit form, detail), such as schools, students, employees, fees. Use for any non-master module that talks to the SMSS API.
---

# Build a feature screen (SMSS)

Use for business modules (school registration, later students, employees, fees, attendance). For simple lookup masters use `smss-master-data-screen` instead.

## Before you start

1. State briefly: which screens, which API endpoints, which DB tables are involved, and any risk. For large or architecture-affecting work, wait for agreement before coding.
2. Read an existing finished feature (school registration) and follow its structure, naming, shared components and theme.
3. Confirm the backend endpoints and DTO shapes. If something is missing, describe the contract instead of faking data.

## Structure

```
features/<feature>/
  models/        request and response interfaces
  services/      <feature>.service.ts (HTTP + unwrap ApiResponse)
  components/    list, form (add/edit), detail, dialogs
  <feature>.routes.ts   lazy-loaded
```

- Standalone components, lazy routes, strong typing, thin components.
- Use shared components (table, confirm dialog, cards, form controls) before creating new ones.

## Required behavior

- **List**: server-side pagination and filter for growing data, loading state, empty state, error state, status chip, row actions.
- **Form**: reactive form, inline validation, disabled submit while invalid or saving, clear error messages from API `message`, unsaved-changes guard if the form is long.
- **Status**: activate/deactivate via POST toggle with a confirmation dialog. No hard delete.
- **File upload** (for example school logo): optional, validate type and size on the client, preview, but the server is the authority. Path convention is handled by the API.
- **Responsive**: usable on phones; forms collapse to one column.

## Multi-tenant and security

- Never send or trust `school_id` as an authorization mechanism. Once auth is in, the tenant comes from the token.
- Do not let the UI expose any way to browse another school's data by editing an id.
- Show sensitive results (such as a generated temporary password) once, never store them in the client, never log them.
- Hide actions by role for convenience only; the API enforces access.
- Assume login will be added: no code that depends on endpoints being anonymous.

## Rules

- API contract: `{ status, statusCode, message, data }`. Handle `status: false`.
- No hard-coded URLs, ids, role ids, or secrets.
- No new npm packages without a stated reason.
- Make the smallest correct change; don't refactor unrelated code.

## Verify

- `ng build` passes
- Manually test the happy path, validation errors, API error, empty list, pagination, status toggle, narrow width
- Summarize what changed and what to test
