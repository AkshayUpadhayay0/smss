---
name: smss-master-data-screen
description: Add or modify a master-data (lookup) admin screen in smss-web, such as board type, school type, school level, status or role. Use when the user asks for a new lut_* master list with add, edit and activate/deactivate.
---

# Add a master-data screen (SMSS)

Master screens share one generic, config-driven list plus a form dialog. A new master should be a **configuration entry plus a few small files**, not a new copy of the screen.

## Before you start

1. Read the existing master screens (board type is the reference) and find the generic master list, the form dialog, the config file, and `MasterDataService`. Reuse them. Do not duplicate.
2. Confirm the backend endpoints exist and are working for this master:
   - `GET /api/MasterData/<name>`
   - `POST /api/MasterData/<name>`
   - `PUT /api/MasterData/<name>/{id}`
   - `POST /api/MasterData/<name>/{id}/toggle-status` (POST, never PATCH)
   If any are missing, stop and describe the contract needed. Do not mock them.
3. Check the table's shape. They differ:
   - `lut_board_type`, `lut_school_type`, `lut_school_level`: `isactive` BOOLEAN with code, name, description
   - `lut_roles`: `status_id` (FK to `lut_status.sid`), role_code, role_name, description
   - `lut_status`: sid, name, type
   Map the model and form fields to the real columns. Do not assume they are all the same.

## Steps

1. **Model** — add request and response interfaces in the master-data feature's `models/`. No `any`.
2. **Service** — add typed get/create/update/toggle methods to `MasterDataService` (or the existing master service). Unwrap `ApiResponse.data` here.
3. **Config** — add one entry to the master config: title, route, columns, form fields (label, key, validators, max length), and the service calls.
4. **Route** — add a lazy route under `/master-data/<name>`.
5. **Sidebar** — add the item under *Masters*.
6. **Behavior** — the generic screen must give:
   - loading, empty and error states
   - search/filter and pagination if the list can grow
   - add and edit in the dialog with inline validation and double-submit protection
   - Active/Inactive toggle with a confirmation dialog
   - snackbar with the API `message` on success and failure
   - the list refreshes immediately after save or toggle (no click needed; use signals, `async` pipe or `markForCheck`)

## Rules

- "Delete" is a status toggle. No hard delete.
- Never hard-code Active/Inactive ids. They come from `lut_status` by name (Active / Inactive, type `general status`) and the backend resolves them.
- Code/name uniqueness errors come back from the API as `message`; show them, don't guess.
- For **Role**: system roles (the seven seeded role codes) are protected from rename or deactivation, and deactivating a role with active users must be blocked with a clear message. Confirm the backend enforces it; the UI only reflects it.
- For **Status**: be careful. Other tables depend on these rows. Prefer limiting edits to name/description and warn before changing anything that rows reference.

## Verify

- `ng build` passes
- Manually test: list loads, add, edit, duplicate-name error, deactivate, reactivate, empty state, narrow-width layout
- Report what you changed and what the user should test
