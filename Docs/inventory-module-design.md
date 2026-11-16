# SMSS — Inventory & Stock Module (design baseline)

Status: DESIGN APPROVED (scope decisions confirmed 2026-11-16), nothing built yet. Source of truth for the Inventory module, same role `claude/exam-report-card-design.md` plays for Examination. Do not redesign without the "Previous decision / New decision / Reason / Impact" format.

## 1. Scope and decisions (confirmed)

| Decision | Choice | Why | Impact |
|---|---|---|---|
| Phasing | 3 stages, each shippable on its own | Module is large; each stage deploys and tests independently | Stage 1 has no dependency on PO/POS tables |
| Roles | New `STORE_KEEPER` role | Store work is not a finance or admin duty | One `lut_roles` row, one line in the Employee login allow-list (same as `ADMISSION_STAFF`, section 36) |
| Sales and money | Standalone sales, no link to Fees | No ledger or expense module exists yet; a sale cannot post anywhere | Zero effect on `tb_student_fee_dues`. Ledger integration is a later, separate decision |
| Stock model | Item-level running quantity + immutable movement ledger | Matches the reference screens (one optional expiry per item); audit comes from the ledger | Batch/FEFO tracking is out of scope. If ever needed it is a new design, not a patch |

**Previous decision / New decision (Suppliers):** when scoping I placed Suppliers in Stage 2. **New:** the Suppliers master moves to Stage 1. Reason: Add Stock (Stage 1) records the supplier it came from, and a free-text supplier would have to be migrated later. Impact: Stage 1 gains one small master table; Stage 2 is then PO, GRN and supplier payments only.

**Not in scope, any stage:** batch tracking, barcode printing, multi-store/warehouse, stock transfers, partial sale refunds, GST returns, payment-gateway, accounting ledger posting, background jobs (alerts are computed on read, no scheduler, consistent with sections 20/39).

## 2. Isolation rule (the "no impact on other modules" guarantee)

1. Only new tables, all prefixed `tb_inv_*` / `lut_inv_*`. No existing table is altered.
2. Foreign keys point **inward only**: inventory tables reference `tb_schools`, `tb_users`, `tb_employees`, `tb_students`, `tb_employee_departments`. **No existing table references an inventory table.** Dropping the module would leave every other module untouched.
3. New code lives in its own folders: API `Controllers/Inventory`, `Services/Inventory`, `Entities/Inventory`, `Dtos/Inventory`; Angular lazy-loaded `features/inventory` with its own routes. No existing service is modified or called for writes. Existing modules are only **read** (employee/student/department lookups for issue recipients).
4. Complete list of touch-points in existing code (nothing else may change):
   - `lut_roles`: insert `STORE_KEEPER`.
   - Employee login allow-list: add `STORE_KEEPER` (one line).
   - `DbContext`: add the new `DbSet`s. DI registration for the new services.
   - Angular: one sidebar group "Inventory" and the lazy route registration.
   - Existing tests that assert the exact role list must be updated (the Admission round hit this once: a test expected the old, narrower list).

## 3. Conventions inherited from the project

- `BIGINT GENERATED ALWAYS AS IDENTITY` PKs, `VARCHAR(100) school_id`, `TIMESTAMPTZ`, `NUMERIC` money. No UUIDs, no `gen_random_uuid()`.
- `status_id` → `lut_status` for soft hide/delete (check the exact column type of `lut_status.sid` when writing the migration; section 42 recorded it as INTEGER). No hard deletes anywhere.
- `school_id` always from the token, never from the client. Cross-school access returns 404.
- **Tenant guarantee in the database:** child tables carry `school_id` and reference the parent with a composite FK `(parent_id, school_id)` (parents get `UNIQUE (id, school_id)`), so an item can never point at another school's category, and a movement can never point at another school's item.
- Business numbers (`ISS-2026-0001`, later `PO-`, `GRN-`, `INV-`, `SPY-`) use the existing per-school advisory-lock allocator (fee receipts, section 28), unique index as backstop.
- Derived status is never stored (Fee Due pattern). Locked prices at transaction time (Fee/Invoice pattern). Soft-void with reason, never delete (Fee payment pattern).
- Private file storage for any uploaded file (item photo): never a public `/uploads` URL. Student photos are currently public (section 45, open item); inventory must not repeat that.
- Standard API envelope `{status, statusCode, message, data}`; DTOs only; async + `CancellationToken`; centralized exception handling.

## 4. Authorization (role-name helpers, per section 25 pattern)

| Helper | Roles | Gates |
|---|---|---|
| `CanManageInventory()` | School Admin, Store Keeper | Categories, items, suppliers, add stock, adjustments, issue/return, (Stage 3) POS |
| `CanViewInventory()` | School Admin, Store Keeper, Principal | Dashboard and read-only lists; response carries `canManage` so the UI hides actions (same as `CanViewFees`) |
| `CanApprovePurchase()` (Stage 2) | School Admin, Principal | Approve a PO |
| Supplier payments (Stage 2) | provisional: School Admin + Accountant | confirm at Stage 2 start |

Everyone else: 403 (Teacher, Accountant, Admission Staff, Student, Guardian), 401 anonymous. Server-side checks, not UI-only. Store Keeper must be added to `FirstLoginEnforcementMiddleware`-covered accounts automatically (it reads `tb_users`, no change needed). Roles/permission strings from the reference screens (`inventory.items.manage` etc.) are **not** introduced; they map to the helpers above until the project-wide RBAC pass happens.

## 5. Stage 1 — Items, Stock, Issue/Return, Suppliers, Dashboard

### Lookups
- `lut_inv_unit` (global): pcs, kg, g, litre, ml, box, pack, set, pair, dozen, metre, ream, bottle. Columns `unit_id`, `unit_name`, `unit_code`, `is_active`.
- Adjustment reasons and movement types are **code constants** (CHECK-constrained text), not tables: `DAMAGE`, `LOSS`, `EXPIRED`, `CORRECTION`, `OPENING_BALANCE`.

### `tb_inv_categories`
`category_id` PK, `school_id`, `category_name` VARCHAR(100), `category_code` VARCHAR(30), `description` TEXT null, `status_id`, `created_at`, `updated_at`. Unique `(school_id, category_code)`, `(category_id, school_id)`.

### `tb_inv_suppliers`
`supplier_id` PK, `school_id`, `supplier_name` VARCHAR(150), `contact_person`, `mobile`, `email`, `address` TEXT, `gstin` VARCHAR(15), `pan` VARCHAR(10), `status_id`, timestamps. Unique `(school_id, lower(supplier_name))`, `(supplier_id, school_id)`. GSTIN/PAN format validated in the service (not over-strict in the DB). Also used as the "vendor" in Stage 3 vendor-on-behalf billing, so no separate vendor table.

### `tb_inv_items`
- `item_id` PK, `school_id`
- `item_code` VARCHAR(50): auto `ITM0001` per school (locked max-plus-one), unique `(school_id, item_code)`
- `barcode` VARCHAR(100) null: partial unique `(school_id, barcode) WHERE barcode IS NOT NULL`
- `item_name` VARCHAR(150), `description` TEXT null
- `category_id` (composite FK), `unit_id` → `lut_inv_unit`
- `item_type` VARCHAR(12) CHECK in (`CONSUMABLE`,`ASSET`)
- `purchase_price` NUMERIC(12,2) default 0 (updated to the last stock-in cost; **last-purchase-cost, not weighted average**)
- `selling_price` NUMERIC(12,2) null, `is_saleable` BOOLEAN default false, `gst_percent` NUMERIC(5,2) default 0 (used from Stage 3; kept here so the item master is never altered later)
- `min_stock_level` NUMERIC(12,3) default 0
- `current_quantity` NUMERIC(12,3) default 0, **CHECK `current_quantity >= 0`** (database-level oversell guard)
- `expiry_date` DATE null, `photo_path` VARCHAR null (private storage)
- `status_id`, `created_by`, timestamps
- Check: `is_saleable` requires `selling_price` not null. Index `(school_id, category_id)`, `(school_id, status_id)`, and a partial index for low stock reads.
- Quantity is **never edited through the item update endpoint**. It only changes via the movement ledger (below). Same idea as `amount_paid` on fee dues: a cached total, only written transactionally.

### `tb_inv_stock_movements` (immutable audit ledger, the single source of truth for every quantity change)
`movement_id` PK, `school_id`, `item_id` (composite FK), `movement_type` CHECK in (`OPENING`,`STOCK_IN`,`ADJUSTMENT`,`ISSUE`,`RETURN`; Stage 2 adds `GRN`; Stage 3 adds `SALE`,`SALE_VOID`), `quantity_change` NUMERIC(12,3) signed, CHECK `<> 0`, `quantity_after` NUMERIC(12,3) CHECK `>= 0`, `reason_code` VARCHAR(30) null (adjustments), `reference_type` VARCHAR(30) null, `reference_id` BIGINT null (stock-in id, issue line id, later GRN/sale line), `unit_cost` NUMERIC(12,2) null, `remarks` TEXT null (required for adjustments), `created_by` → `tb_users`, `created_at`.
- **A trigger rejects UPDATE and DELETE** on this table (DB-level backstop for "audited corrections"). Corrections are new rows, never edits.
- Partial unique index: at most one `OPENING` movement per item.
- Index `(school_id, item_id, created_at DESC)` and `(school_id, created_at DESC)` for dashboard recent activity.

### Atomic stock change (the one place quantity is written)
Inside a single transaction: `UPDATE tb_inv_items SET current_quantity = current_quantity + @delta, updated_at = now() WHERE item_id=@id AND school_id=@sid AND current_quantity + @delta >= 0 RETURNING current_quantity;` Zero rows returned means insufficient stock → 409 naming the item and available quantity, nothing written. Then insert the ledger row using the returned value as `quantity_after`. Concurrent issues of the same item cannot both succeed past zero. All writers (stock-in, adjustment, issue, return, and later GRN/sale) go through one `IStockLedgerService` so the rule exists once.

### `tb_inv_stock_ins` (the "stock-in log", no PO needed)
`stock_in_id` PK, `school_id`, `item_id` (composite FK), `supplier_id` null (composite FK), `quantity` NUMERIC(12,3) CHECK `> 0`, `unit_cost` NUMERIC(12,2), `received_date` DATE (not in the future), `bill_number` VARCHAR(50) null, `expiry_date` DATE null (if given, updates the item's expiry; must be after received_date), `remarks`, `received_by` → `tb_users`, `status` Active/Voided + `voided_by/at/reason` (soft void reverses stock through the ledger; refused if it would take quantity below zero), `created_at`. Effect: ledger `STOCK_IN` row, item `purchase_price = unit_cost`.

### Adjustments
No header table: an adjustment **is** a ledger row (`ADJUSTMENT`, signed quantity, `reason_code`, required non-blank remarks ≤500). `OPENING_BALANCE` is a positive adjustment allowed only while the item has no `OPENING` row. `EXPIRED`/`DAMAGE`/`LOSS` must be negative; `CORRECTION` may be either sign. Adjusting cannot be voided (a counter-adjustment is the correction), consistent with an append-only ledger.

### Issue / Return
`tb_inv_issues` (header): `issue_id` PK, `school_id`, `issue_number` VARCHAR(50) (`ISS-2026-0001`, locked per school), `recipient_type` CHECK in (`STAFF`,`STUDENT`,`DEPARTMENT`), `employee_id` VARCHAR(20) null → `tb_employees`, `student_id` VARCHAR(20) null → `tb_students`, `department_id` BIGINT null → `tb_employee_departments`, **CHECK exactly the one column matching `recipient_type` is non-null**, `issue_date` DATE, `purpose` TEXT null, `issued_by` → `tb_users`, `status` Active/Voided + void fields. Unique `(school_id, issue_number)`.
`tb_inv_issue_lines`: `issue_line_id` PK, `issue_id` (cascade), `school_id`, `item_id` (composite FK), `quantity` CHECK `> 0`, `returned_quantity` default 0, CHECK `returned_quantity <= quantity`. Unique `(issue_id, item_id)`.
`tb_inv_issue_returns`: `return_id` PK, `issue_line_id`, `school_id`, `quantity` CHECK `> 0`, `return_date`, `condition` CHECK in (`GOOD`,`DAMAGED`), `remarks`, `received_by`. **`GOOD` returns add stock back (ledger `RETURN`); `DAMAGED` returns are recorded but do not add stock** (provisional default, confirm in review).
Issue and return status are **derived** (Outstanding / Partially Returned / Returned) from line quantities. Voiding an issue is allowed only when nothing has been returned; it restores stock via the ledger. Recipient must be Active at issue time (employee Active, student Active, department Active).
Recipient pickers read the existing employee, student and department tables through narrow search endpoints in the Inventory controller (school-scoped, capped result size, no new access to those modules' own endpoints).

### Dashboard (aggregate endpoint, same pattern as sections 28/40/41/44)
`GET /api/InventoryDashboard/summary`: total active items, total stock value (sum of `current_quantity × purchase_price`), low-stock count and top list (`min_stock_level > 0 AND current_quantity <= min_stock_level`), expiring within 30 days and already-expired counts and lists (30 is a named constant), issued-out-and-not-returned count, latest 10 movements, operations summary (stock-ins and issues in the last 30 days). Failed sub-queries show "Unavailable", never a false 0.

### Stage 1 endpoints (`/api/Inventory...`, school from token)
- `Category`: list / create / update / toggle status
- `Supplier`: list / create / update / toggle status
- `Item`: list (paged, filter by category, search by name/code/barcode, low-stock, expiring) / get / create / update (no quantity field) / toggle status / photo upload, remove, authenticated download
- `StockIn`: list / create / void
- `StockAdjustment`: list (ledger filter) / create
- `StockMovement`: list per item (paged) for the history drawer
- `Issue`: list / get / create / return / void
- `InventoryLookup`: units, recipient search (staff, students, departments), adjustment reasons
- `InventoryDashboard/summary`

### Stage 1 Angular (`features/inventory`, lazy, own routes under `/inventory/*`)
Tabs as in the reference: Dashboard, Items / Categories, Add Stock, Adjustments, Issue / Return, Suppliers, plus an optional static Guide page. Reuse the shared table, card, form and dialog components. Confirmation dialog on void; empty states; loading states; mobile-friendly forms; photo shown through an authenticated blob URL.

## 6. Stage 2 — Procurement (outline, details finalised when Stage 2 starts)

- `tb_inv_purchase_orders`: `po_number` (`PO-2026-0001`, locked), `supplier_id`, `order_date`, `expected_date`, stored status `DRAFT` / `APPROVED` / `CANCELLED`, `approved_by/at`, totals (`subtotal`, `gst_total`, `total`) computed server-side and frozen on approval. **Displayed "Sent / Partially Received / Received" is derived** from received quantities, not stored.
- `tb_inv_po_lines`: `item_id`, `ordered_quantity`, `unit_price`, `gst_percent`, line totals.
- `tb_inv_goods_receipts` + `tb_inv_goods_receipt_lines` (GRN): only against an APPROVED PO; received quantity per line cannot exceed ordered minus already received (over-receipt refused); each line writes a `GRN` ledger row and refreshes the item's `purchase_price`. Soft void reverses stock.
- `tb_inv_supplier_payments` + `tb_inv_supplier_payment_allocations`: school-scoped payment split across POs, a direct copy of the fee/subscription allocation pattern; PO balance derived = total − active allocations; soft void.
- Open question for Stage 2: may the PO creator approve their own PO? Leave Management forbids self-decisions; a single-admin school would then have no approver. Decide at Stage 2 start (proposal: Store Keeper can never self-approve; School Admin may, and it is recorded).

## 7. Stage 3 — Point of Sale (outline)

- `tb_inv_sales`: `sale_number` (`INV-2026-0001`, locked), `buyer_type` (`STUDENT` / `STAFF` / `WALK_IN`), buyer FK or `buyer_name`, `billing_mode` (`SCHOOL` / `ON_BEHALF_OF_VENDOR`), `vendor_id` → `tb_inv_suppliers` (required for on-behalf), `payment_mode`, subtotal / discount / GST / total (locked at sale), `sold_by`, soft-void fields.
- `tb_inv_sale_lines`: item, quantity, **unit price and GST locked at sale time**, per-line discount, line total. Only `is_saleable` items; stock decreases through the ledger (`SALE`); void restores it (`SALE_VOID`). Refund is a **full void with reason** (partial returns deferred; no cash tracking, same as section 28).
- On-behalf billing prints the invoice under the vendor's name and GSTIN; the **Vendor Collections** tab is a read-only total per vendor of on-behalf sales. Recording a settlement payment to the vendor is deferred.
- Invoice PDF through the existing QuestPDF dependency (section 45); no new library.
- Decide at Stage 3 start: item price GST-inclusive or exclusive, and whether a sale to a student should show on the student profile (read-only list, still no fee link).

## 8. Edge cases and risks to test in Stage 1

- 8 simultaneous issues of the last 5 units: exactly the quantity available is issued, rest get 409, quantity never negative, ledger sums to the item quantity.
- Ledger reconciliation check: `SUM(quantity_change)` per item equals `current_quantity` (a test and a safe SQL check to run before any deploy).
- Cross-school: item, supplier, category ids from another school return 404; raw insert with mismatched `school_id` is rejected by the composite FK.
- Recipient integrity: wrong combination of recipient columns rejected by CHECK; inactive employee/student refused.
- Stock-in void that would drive quantity below zero is refused.
- Photo: signature-checked, 2 MB, private download, 404 cross-school.
- Item with `is_saleable = true` and no selling price rejected.
- Duplicate item name in same category warned in UI but allowed (codes are the identity).
- Existing-data fingerprint across all pre-existing tables identical before/after deploy; only `lut_roles` row count changes (+1).

## 9. Delivery process (same as every module)

Backup (`smss-db-before-inventory-stage1.dump`), scratch copy first, migration `Docs/smss-inventory-stage1.sql` idempotent, scratch smoke, then real `smss-db`, throwaway accounts only (never touch a real user's credentials), cleanup, fingerprint. One commit per stage. Confirm `Bootstrap:Enabled` is `false` in committed `appsettings.json` before any push (open item, section 46).

## 10. Claude Code prompt — Stage 1

```
Build Stage 1 of the SMSS Inventory module exactly as specified in the project doc
claude/inventory-module-design.md (sections 1 to 5 and 8). Read it first.

Before writing code, investigate and report briefly:
1. How existing modules implement: the API response envelope, DTO validation, the
   per-school advisory-lock number allocator (fee receipt numbers), private file
   storage (Homework attachment / Employee documents), the lut_status Active lookup
   and the exact column type of lut_status.sid, the Employee login role allow-list,
   role-name helpers such as CanViewFees/CanManageFees, and the Angular shared
   table/card/dialog components and lazy route pattern.
2. Whether any existing test asserts the exact role list in lut_roles.
Follow those existing patterns. Do not invent new ones.

Hard rules:
- Isolation: new tables, entities, services, controllers and Angular feature only.
  The ONLY edits to existing code are: lut_roles seed row STORE_KEEPER, one line in the
  Employee login allow-list, DbSet/DI registration, the sidebar group and lazy route
  registration, and any existing test that asserts the old role list. List every
  existing file you touched in your final report. Do not modify any other module.
- All quantity changes go through one IStockLedgerService using the atomic UPDATE ...
  WHERE current_quantity + delta >= 0 RETURNING pattern plus an immutable
  tb_inv_stock_movements row, in one transaction. The item update endpoint must not
  accept a quantity.
- Tenant isolation: school_id from the token only; composite FKs (id, school_id);
  cross-school ids return 404.
- PostgreSQL only, BIGINT GENERATED ALWAYS AS IDENTITY, TIMESTAMPTZ, no UUIDs.
- Item photos in PRIVATE storage with an authenticated download endpoint.
- Roles: CanManageInventory (School Admin, Store Keeper), CanViewInventory (+Principal,
  read-only with canManage flag). Everyone else 403, enforced server-side.
- Soft delete/void only, never hard delete. Statuses derived, not stored, where the doc says so.

Deliver in this order, pausing for my review after step 2:
1. Migration Docs/smss-inventory-stage1.sql (idempotent, with the ledger UPDATE/DELETE
   trigger, CHECKs, partial uniques) and entities.
2. Report the schema as built and any deviation from the doc, with reasons.
3. Services, controllers, DTOs, dashboard endpoint.
4. Backend smoke tests on a scratch DB copy, including: concurrent issue of the last
   units, ledger-sum equals current_quantity, cross-school 404, CHECK rejections via raw
   SQL, void rules, every role's 403/200, and the full existing regression suite.
5. Angular feature (Dashboard, Items/Categories, Add Stock, Adjustments, Issue/Return,
   Suppliers, optional Guide), frontend tests, ng build clean.
6. Do NOT deploy to the real smss-db or commit until I confirm. When I do: backup first,
   throwaway accounts only, fingerprint all pre-existing tables before/after.
```
