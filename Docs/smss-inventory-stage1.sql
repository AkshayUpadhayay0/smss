-- Inventory & Stock, Stage 1: categories, suppliers, items, the immutable stock ledger, stock-in, issue / return, units lookup, STORE_KEEPER role.
-- Design baseline: Docs/inventory-module-design.md (sections 1 to 5 and 8). Safe to run twice.
--
-- Adds ONLY new objects: lut_inv_unit, tb_inv_categories, tb_inv_suppliers, tb_inv_items, tb_inv_stock_movements, tb_inv_stock_ins,
-- tb_inv_issues, tb_inv_issue_lines, tb_inv_issue_returns, their functions / triggers, and ONE lut_roles row (STORE_KEEPER).
-- No existing table is altered; foreign keys point inward only (tb_schools, tb_users, tb_employees, tb_students, tb_employee_departments, lut_status).
-- Needs: tb_schools, tb_users, tb_employees, tb_students (with uq_tb_students_id_school), tb_employee_departments, lut_status, lut_roles.
-- Run BEFORE starting the API build that contains the inventory entities.
--
-- Conventions: BIGINT GENERATED ALWAYS AS IDENTITY, TIMESTAMPTZ, NUMERIC for money and quantities. status_id is INTEGER (lut_status.sid is integer).
-- Tenant guarantee: every child carries school_id and references its parent with a composite FK (id, school_id).
--
-- Reconciliation check (read-only; safe to run before any deploy and in smoke tests). Must return ZERO rows:
--   SELECT i.school_id, i.item_id, i.current_quantity, COALESCE(m.total, 0) AS ledger_total
--   FROM public.tb_inv_items i
--   LEFT JOIN (SELECT item_id, SUM(quantity_change) AS total FROM public.tb_inv_stock_movements GROUP BY item_id) m ON m.item_id = i.item_id
--   WHERE i.current_quantity <> COALESCE(m.total, 0);

BEGIN;

-- ============================================================================================ role
INSERT INTO public.lut_roles (role_name, role_code, description, status_id)
SELECT 'Store Keeper', 'STORE_KEEPER',
       'Runs the school store: items and categories, suppliers, stock in, stock adjustments, issuing items to staff, students or departments and taking returns. No access to fees, students or employees.',
       (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')
WHERE NOT EXISTS (SELECT 1 FROM public.lut_roles WHERE role_code = 'STORE_KEEPER')
  AND EXISTS (SELECT 1 FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status');

-- ============================================================================================ lut_inv_unit (global)
CREATE TABLE IF NOT EXISTS public.lut_inv_unit
(
    unit_id    bigint       GENERATED ALWAYS AS IDENTITY,
    unit_name  varchar(50)  NOT NULL,
    unit_code  varchar(20)  NOT NULL,
    is_active  boolean      NOT NULL DEFAULT true,
    CONSTRAINT lut_inv_unit_pkey PRIMARY KEY (unit_id),
    CONSTRAINT uq_lut_inv_unit_code UNIQUE (unit_code),
    CONSTRAINT chk_lut_inv_unit_name CHECK (length(btrim(unit_name)) > 0)
);

INSERT INTO public.lut_inv_unit (unit_name, unit_code) VALUES
    ('Pieces', 'pcs'), ('Kilogram', 'kg'), ('Gram', 'g'), ('Litre', 'litre'), ('Millilitre', 'ml'),
    ('Box', 'box'), ('Pack', 'pack'), ('Set', 'set'), ('Pair', 'pair'), ('Dozen', 'dozen'),
    ('Metre', 'metre'), ('Ream', 'ream'), ('Bottle', 'bottle')
ON CONFLICT (unit_code) DO NOTHING;

-- ============================================================================================ tb_inv_categories
CREATE TABLE IF NOT EXISTS public.tb_inv_categories
(
    category_id    bigint        GENERATED ALWAYS AS IDENTITY,
    school_id      varchar(100)  NOT NULL,
    category_name  varchar(100)  NOT NULL,
    category_code  varchar(30)   NOT NULL,
    description    text,
    status_id      integer       NOT NULL,
    created_at     timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at     timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_inv_categories_pkey PRIMARY KEY (category_id),
    CONSTRAINT uq_tb_inv_categories_id_school   UNIQUE (category_id, school_id),
    CONSTRAINT uq_tb_inv_categories_school_code UNIQUE (school_id, category_code),
    CONSTRAINT fk_tb_inv_categories_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_inv_categories_status FOREIGN KEY (status_id) REFERENCES public.lut_status (sid),
    CONSTRAINT chk_tb_inv_categories_name  CHECK (length(btrim(category_name)) > 0),
    CONSTRAINT chk_tb_inv_categories_code  CHECK (length(btrim(category_code)) > 0)
);

-- ============================================================================================ tb_inv_suppliers
CREATE TABLE IF NOT EXISTS public.tb_inv_suppliers
(
    supplier_id     bigint        GENERATED ALWAYS AS IDENTITY,
    school_id       varchar(100)  NOT NULL,
    supplier_name   varchar(150)  NOT NULL,
    contact_person  varchar(100),
    mobile          varchar(20),
    email           varchar(150),
    address         text,
    gstin           varchar(15),
    pan             varchar(10),     -- GSTIN / PAN format is validated by the application, deliberately not over-strict here
    status_id       integer       NOT NULL,
    created_at      timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_inv_suppliers_pkey PRIMARY KEY (supplier_id),
    CONSTRAINT uq_tb_inv_suppliers_id_school UNIQUE (supplier_id, school_id),
    CONSTRAINT fk_tb_inv_suppliers_school FOREIGN KEY (school_id) REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_inv_suppliers_status FOREIGN KEY (status_id) REFERENCES public.lut_status (sid),
    CONSTRAINT chk_tb_inv_suppliers_name  CHECK (length(btrim(supplier_name)) > 0)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_inv_suppliers_school_name ON public.tb_inv_suppliers (school_id, lower(supplier_name));

-- ============================================================================================ tb_inv_items
CREATE TABLE IF NOT EXISTS public.tb_inv_items
(
    item_id           bigint         GENERATED ALWAYS AS IDENTITY,
    school_id         varchar(100)   NOT NULL,
    item_code         varchar(50)    NOT NULL,                 -- ITM0001 per school, allocated under an advisory lock (max + 1)
    barcode           varchar(100),
    item_name         varchar(150)   NOT NULL,
    description       text,
    category_id       bigint         NOT NULL,
    unit_id           bigint         NOT NULL,
    item_type         varchar(12)    NOT NULL,
    purchase_price    numeric(12,2)  NOT NULL DEFAULT 0,       -- last stock-in cost (NOT a weighted average)
    selling_price     numeric(12,2),
    is_saleable       boolean        NOT NULL DEFAULT false,
    gst_percent       numeric(5,2)   NOT NULL DEFAULT 0,
    min_stock_level   numeric(12,3)  NOT NULL DEFAULT 0,
    current_quantity  numeric(12,3)  NOT NULL DEFAULT 0,       -- a cached total: written ONLY by the stock ledger service, in the same transaction as a tb_inv_stock_movements row
    expiry_date       date,
    photo_path        varchar(500),                            -- key in the PRIVATE file store, never a URL
    status_id         integer        NOT NULL,
    created_by        bigint         NOT NULL,
    created_at        timestamptz    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        timestamptz    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_inv_items_pkey PRIMARY KEY (item_id),
    CONSTRAINT uq_tb_inv_items_id_school   UNIQUE (item_id, school_id),
    CONSTRAINT uq_tb_inv_items_school_code UNIQUE (school_id, item_code),
    CONSTRAINT fk_tb_inv_items_school   FOREIGN KEY (school_id)                REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_inv_items_category FOREIGN KEY (category_id, school_id)   REFERENCES public.tb_inv_categories (category_id, school_id),
    CONSTRAINT fk_tb_inv_items_unit     FOREIGN KEY (unit_id)                  REFERENCES public.lut_inv_unit (unit_id),
    CONSTRAINT fk_tb_inv_items_status   FOREIGN KEY (status_id)                REFERENCES public.lut_status (sid),
    CONSTRAINT fk_tb_inv_items_created  FOREIGN KEY (created_by)               REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_inv_items_name       CHECK (length(btrim(item_name)) > 0),
    CONSTRAINT chk_tb_inv_items_type       CHECK (item_type IN ('CONSUMABLE', 'ASSET')),
    CONSTRAINT chk_tb_inv_items_qty        CHECK (current_quantity >= 0),            -- the database-level oversell guard
    CONSTRAINT chk_tb_inv_items_min        CHECK (min_stock_level >= 0),
    CONSTRAINT chk_tb_inv_items_purchase   CHECK (purchase_price >= 0),
    CONSTRAINT chk_tb_inv_items_selling    CHECK (selling_price IS NULL OR selling_price >= 0),
    CONSTRAINT chk_tb_inv_items_gst        CHECK (gst_percent >= 0 AND gst_percent <= 100),
    CONSTRAINT chk_tb_inv_items_saleable   CHECK (is_saleable = false OR selling_price IS NOT NULL)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_inv_items_school_barcode ON public.tb_inv_items (school_id, barcode) WHERE barcode IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_tb_inv_items_category  ON public.tb_inv_items (school_id, category_id);
CREATE INDEX IF NOT EXISTS idx_tb_inv_items_status    ON public.tb_inv_items (school_id, status_id);
CREATE INDEX IF NOT EXISTS idx_tb_inv_items_low_stock ON public.tb_inv_items (school_id) WHERE min_stock_level > 0 AND current_quantity <= min_stock_level;
CREATE INDEX IF NOT EXISTS idx_tb_inv_items_expiry    ON public.tb_inv_items (school_id, expiry_date) WHERE expiry_date IS NOT NULL;

-- ============================================================================================ tb_inv_stock_movements (immutable ledger)
CREATE TABLE IF NOT EXISTS public.tb_inv_stock_movements
(
    movement_id      bigint         GENERATED ALWAYS AS IDENTITY,
    school_id        varchar(100)   NOT NULL,
    item_id          bigint         NOT NULL,
    movement_type    varchar(20)    NOT NULL,
    quantity_change  numeric(12,3)  NOT NULL,
    quantity_after   numeric(12,3)  NOT NULL,
    reason_code      varchar(30),
    reference_type   varchar(30),
    reference_id     bigint,
    unit_cost        numeric(12,2),
    remarks          text,
    created_by       bigint         NOT NULL,
    created_at       timestamptz    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_inv_stock_movements_pkey PRIMARY KEY (movement_id),
    CONSTRAINT fk_tb_inv_movements_school  FOREIGN KEY (school_id)              REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_inv_movements_item    FOREIGN KEY (item_id, school_id)     REFERENCES public.tb_inv_items (item_id, school_id),
    CONSTRAINT fk_tb_inv_movements_user    FOREIGN KEY (created_by)             REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_inv_movements_type   CHECK (movement_type IN ('OPENING', 'STOCK_IN', 'ADJUSTMENT', 'ISSUE', 'RETURN', 'STOCK_IN_VOID', 'ISSUE_VOID')),
    CONSTRAINT chk_tb_inv_movements_change CHECK (quantity_change <> 0),
    CONSTRAINT chk_tb_inv_movements_after  CHECK (quantity_after >= 0),
    CONSTRAINT chk_tb_inv_movements_reason CHECK (reason_code IS NULL OR reason_code IN ('DAMAGE', 'LOSS', 'EXPIRED', 'CORRECTION', 'OPENING_BALANCE')),
    -- sign rules per movement type (an adjustment's sign depends on its reason)
    CONSTRAINT chk_tb_inv_movements_sign CHECK (
           (movement_type IN ('OPENING', 'STOCK_IN', 'RETURN', 'ISSUE_VOID') AND quantity_change > 0)
        OR (movement_type IN ('ISSUE', 'STOCK_IN_VOID')                      AND quantity_change < 0)
        OR (movement_type = 'ADJUSTMENT')),
    -- an OPENING row is the OPENING_BALANCE adjustment; every other ADJUSTMENT has one of the four reasons; no other type carries a reason
    CONSTRAINT chk_tb_inv_movements_reason_rule CHECK (
           (movement_type = 'OPENING'    AND reason_code = 'OPENING_BALANCE')
        OR (movement_type = 'ADJUSTMENT' AND reason_code IN ('DAMAGE', 'LOSS', 'EXPIRED', 'CORRECTION'))
        OR (movement_type NOT IN ('OPENING', 'ADJUSTMENT') AND reason_code IS NULL)),
    CONSTRAINT chk_tb_inv_movements_adj_sign CHECK (
           reason_code IS NULL OR reason_code NOT IN ('DAMAGE', 'LOSS', 'EXPIRED') OR quantity_change < 0),
    -- adjustments (and the opening balance) must say why, in words
    CONSTRAINT chk_tb_inv_movements_remarks CHECK (
           movement_type NOT IN ('OPENING', 'ADJUSTMENT')
        OR (remarks IS NOT NULL AND length(btrim(remarks)) > 0 AND length(remarks) <= 500)),
    CONSTRAINT chk_tb_inv_movements_unit_cost CHECK (unit_cost IS NULL OR unit_cost >= 0)
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_tb_inv_movements_one_opening ON public.tb_inv_stock_movements (item_id) WHERE movement_type = 'OPENING';
CREATE INDEX IF NOT EXISTS idx_tb_inv_movements_item   ON public.tb_inv_stock_movements (school_id, item_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_tb_inv_movements_recent ON public.tb_inv_stock_movements (school_id, created_at DESC);

-- The ledger is append-only: corrections are new rows, never edits. A backstop for "audited corrections", for UPDATE, DELETE and TRUNCATE.
CREATE OR REPLACE FUNCTION public.fn_tb_inv_stock_movements_immutable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'tb_inv_stock_movements is an append-only ledger: % is not allowed. Record a correcting movement instead.', TG_OP
        USING ERRCODE = 'restrict_violation';
END;
$$;

DROP TRIGGER IF EXISTS trg_tb_inv_stock_movements_no_change ON public.tb_inv_stock_movements;
CREATE TRIGGER trg_tb_inv_stock_movements_no_change
    BEFORE UPDATE OR DELETE ON public.tb_inv_stock_movements
    FOR EACH ROW EXECUTE FUNCTION public.fn_tb_inv_stock_movements_immutable();

DROP TRIGGER IF EXISTS trg_tb_inv_stock_movements_no_truncate ON public.tb_inv_stock_movements;
CREATE TRIGGER trg_tb_inv_stock_movements_no_truncate
    BEFORE TRUNCATE ON public.tb_inv_stock_movements
    FOR EACH STATEMENT EXECUTE FUNCTION public.fn_tb_inv_stock_movements_immutable();

-- ============================================================================================ tb_inv_stock_ins
CREATE TABLE IF NOT EXISTS public.tb_inv_stock_ins
(
    stock_in_id    bigint         GENERATED ALWAYS AS IDENTITY,
    school_id      varchar(100)   NOT NULL,
    item_id        bigint         NOT NULL,
    supplier_id    bigint,
    quantity       numeric(12,3)  NOT NULL,
    unit_cost      numeric(12,2)  NOT NULL,
    received_date  date           NOT NULL,                 -- "not in the future" is checked by the application against the school's (IST) today
    bill_number    varchar(50),
    expiry_date    date,
    remarks        text,
    received_by    bigint         NOT NULL,
    status         varchar(10)    NOT NULL DEFAULT 'Active',
    voided_by      bigint,
    voided_at      timestamptz,
    void_reason    text,
    created_at     timestamptz    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_inv_stock_ins_pkey PRIMARY KEY (stock_in_id),
    CONSTRAINT uq_tb_inv_stock_ins_id_school UNIQUE (stock_in_id, school_id),
    CONSTRAINT fk_tb_inv_stock_ins_school   FOREIGN KEY (school_id)              REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_inv_stock_ins_item     FOREIGN KEY (item_id, school_id)     REFERENCES public.tb_inv_items (item_id, school_id),
    CONSTRAINT fk_tb_inv_stock_ins_supplier FOREIGN KEY (supplier_id, school_id) REFERENCES public.tb_inv_suppliers (supplier_id, school_id),
    CONSTRAINT fk_tb_inv_stock_ins_received FOREIGN KEY (received_by)            REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_inv_stock_ins_voided   FOREIGN KEY (voided_by)              REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_inv_stock_ins_qty     CHECK (quantity > 0),
    CONSTRAINT chk_tb_inv_stock_ins_cost    CHECK (unit_cost >= 0),
    CONSTRAINT chk_tb_inv_stock_ins_expiry  CHECK (expiry_date IS NULL OR expiry_date > received_date),
    CONSTRAINT chk_tb_inv_stock_ins_status  CHECK (status IN ('Active', 'Voided')),
    CONSTRAINT chk_tb_inv_stock_ins_void    CHECK (
        status = 'Active'
        OR (voided_by IS NOT NULL AND voided_at IS NOT NULL AND void_reason IS NOT NULL AND length(btrim(void_reason)) > 0))
);
CREATE INDEX IF NOT EXISTS idx_tb_inv_stock_ins_item ON public.tb_inv_stock_ins (school_id, item_id, received_date DESC);
CREATE INDEX IF NOT EXISTS idx_tb_inv_stock_ins_date ON public.tb_inv_stock_ins (school_id, received_date DESC);

-- ============================================================================================ tb_inv_issues
CREATE TABLE IF NOT EXISTS public.tb_inv_issues
(
    issue_id        bigint        GENERATED ALWAYS AS IDENTITY,
    school_id       varchar(100)  NOT NULL,
    issue_number    varchar(50)   NOT NULL,                  -- ISS-2026-0001, allocated under a per-school advisory lock; the unique index is the backstop
    recipient_type  varchar(12)   NOT NULL,
    employee_id     varchar(20),
    student_id      varchar(20),
    department_id   bigint,
    issue_date      date          NOT NULL,
    purpose         text,
    issued_by       bigint        NOT NULL,
    status          varchar(10)   NOT NULL DEFAULT 'Active',
    voided_by       bigint,
    voided_at       timestamptz,
    void_reason     text,
    created_at      timestamptz   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_inv_issues_pkey PRIMARY KEY (issue_id),
    CONSTRAINT uq_tb_inv_issues_id_school UNIQUE (issue_id, school_id),
    CONSTRAINT uq_tb_inv_issues_school_number UNIQUE (school_id, issue_number),
    CONSTRAINT fk_tb_inv_issues_school     FOREIGN KEY (school_id)              REFERENCES public.tb_schools (school_id),
    CONSTRAINT fk_tb_inv_issues_employee   FOREIGN KEY (employee_id)            REFERENCES public.tb_employees (employee_id),
    CONSTRAINT fk_tb_inv_issues_student    FOREIGN KEY (student_id, school_id)  REFERENCES public.tb_students (student_id, school_id),
    CONSTRAINT fk_tb_inv_issues_department FOREIGN KEY (department_id)          REFERENCES public.tb_employee_departments (department_id),
    CONSTRAINT fk_tb_inv_issues_issued_by  FOREIGN KEY (issued_by)              REFERENCES public.tb_users (user_id),
    CONSTRAINT fk_tb_inv_issues_voided_by  FOREIGN KEY (voided_by)              REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_inv_issues_recipient_type CHECK (recipient_type IN ('STAFF', 'STUDENT', 'DEPARTMENT')),
    -- exactly the one recipient column that matches recipient_type is filled
    CONSTRAINT chk_tb_inv_issues_recipient CHECK (
           (recipient_type = 'STAFF'      AND employee_id IS NOT NULL AND student_id IS NULL     AND department_id IS NULL)
        OR (recipient_type = 'STUDENT'    AND student_id  IS NOT NULL AND employee_id IS NULL    AND department_id IS NULL)
        OR (recipient_type = 'DEPARTMENT' AND department_id IS NOT NULL AND employee_id IS NULL  AND student_id IS NULL)),
    CONSTRAINT chk_tb_inv_issues_status CHECK (status IN ('Active', 'Voided')),
    CONSTRAINT chk_tb_inv_issues_void   CHECK (
        status = 'Active'
        OR (voided_by IS NOT NULL AND voided_at IS NOT NULL AND void_reason IS NOT NULL AND length(btrim(void_reason)) > 0))
);
CREATE INDEX IF NOT EXISTS idx_tb_inv_issues_date     ON public.tb_inv_issues (school_id, issue_date DESC);
CREATE INDEX IF NOT EXISTS idx_tb_inv_issues_employee ON public.tb_inv_issues (school_id, employee_id)   WHERE employee_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_tb_inv_issues_student  ON public.tb_inv_issues (school_id, student_id)    WHERE student_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_tb_inv_issues_dept     ON public.tb_inv_issues (school_id, department_id)  WHERE department_id IS NOT NULL;

-- tb_employees and tb_employee_departments have no (id, school_id) unique key and no existing table may be altered, so the composite FK is not
-- possible for them. This trigger gives the same guarantee: a staff or department recipient must belong to the issue's own school.
CREATE OR REPLACE FUNCTION public.fn_tb_inv_issues_recipient_school() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.employee_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM public.tb_employees e WHERE e.employee_id = NEW.employee_id AND e.school_id = NEW.school_id) THEN
        RAISE EXCEPTION 'Employee % does not belong to school %', NEW.employee_id, NEW.school_id USING ERRCODE = 'foreign_key_violation';
    END IF;
    IF NEW.department_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM public.tb_employee_departments d WHERE d.department_id = NEW.department_id AND d.school_id = NEW.school_id) THEN
        RAISE EXCEPTION 'Department % does not belong to school %', NEW.department_id, NEW.school_id USING ERRCODE = 'foreign_key_violation';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_tb_inv_issues_recipient_school ON public.tb_inv_issues;
CREATE TRIGGER trg_tb_inv_issues_recipient_school
    BEFORE INSERT OR UPDATE OF school_id, employee_id, department_id ON public.tb_inv_issues
    FOR EACH ROW EXECUTE FUNCTION public.fn_tb_inv_issues_recipient_school();

-- ============================================================================================ tb_inv_issue_lines
CREATE TABLE IF NOT EXISTS public.tb_inv_issue_lines
(
    issue_line_id      bigint         GENERATED ALWAYS AS IDENTITY,
    issue_id           bigint         NOT NULL,
    school_id          varchar(100)   NOT NULL,
    item_id            bigint         NOT NULL,
    quantity           numeric(12,3)  NOT NULL,
    returned_quantity  numeric(12,3)  NOT NULL DEFAULT 0,
    CONSTRAINT tb_inv_issue_lines_pkey PRIMARY KEY (issue_line_id),
    CONSTRAINT uq_tb_inv_issue_lines_id_school UNIQUE (issue_line_id, school_id),
    CONSTRAINT uq_tb_inv_issue_lines_issue_item UNIQUE (issue_id, item_id),
    CONSTRAINT fk_tb_inv_issue_lines_issue FOREIGN KEY (issue_id, school_id) REFERENCES public.tb_inv_issues (issue_id, school_id),
    CONSTRAINT fk_tb_inv_issue_lines_item  FOREIGN KEY (item_id, school_id)  REFERENCES public.tb_inv_items (item_id, school_id),
    CONSTRAINT chk_tb_inv_issue_lines_qty      CHECK (quantity > 0),
    CONSTRAINT chk_tb_inv_issue_lines_returned CHECK (returned_quantity >= 0 AND returned_quantity <= quantity)
);
CREATE INDEX IF NOT EXISTS idx_tb_inv_issue_lines_item ON public.tb_inv_issue_lines (school_id, item_id);

-- ============================================================================================ tb_inv_issue_returns
CREATE TABLE IF NOT EXISTS public.tb_inv_issue_returns
(
    return_id      bigint         GENERATED ALWAYS AS IDENTITY,
    issue_line_id  bigint         NOT NULL,
    school_id      varchar(100)   NOT NULL,
    quantity       numeric(12,3)  NOT NULL,
    return_date    date           NOT NULL,
    condition      varchar(10)    NOT NULL,
    remarks        text,
    received_by    bigint         NOT NULL,
    created_at     timestamptz    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT tb_inv_issue_returns_pkey PRIMARY KEY (return_id),
    CONSTRAINT fk_tb_inv_issue_returns_line     FOREIGN KEY (issue_line_id, school_id) REFERENCES public.tb_inv_issue_lines (issue_line_id, school_id),
    CONSTRAINT fk_tb_inv_issue_returns_received FOREIGN KEY (received_by)              REFERENCES public.tb_users (user_id),
    CONSTRAINT chk_tb_inv_issue_returns_qty       CHECK (quantity > 0),
    CONSTRAINT chk_tb_inv_issue_returns_condition CHECK (condition IN ('GOOD', 'DAMAGED'))
);
CREATE INDEX IF NOT EXISTS idx_tb_inv_issue_returns_line ON public.tb_inv_issue_returns (school_id, issue_line_id);

COMMIT;
