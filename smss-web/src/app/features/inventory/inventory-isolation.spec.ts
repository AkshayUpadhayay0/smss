import { ROLES, ROUTE_ROLES, canAccess } from '../../core/config/route-roles';
import { NAV_ITEMS, searchNav, visibleNav } from '../../layouts/main-layout/nav.config';
import { PRE_INVENTORY_GOLDEN } from './testing/pre-inventory-golden';

/** The role combinations the golden snapshot was captured for. */
const ROLE_SETS: Record<string, string[]> = {
  ...Object.fromEntries(Object.values(ROLES).filter((r) => r !== ROLES.STORE_KEEPER).map((r) => [r, [r]])),
  'Test Role': ['Test Role'],
  '(none)': [],
  'Principal+Teacher': [ROLES.PRINCIPAL, ROLES.TEACHER],
};

const INVENTORY_ROUTES = ['/inventory', '/inventory/items', '/inventory/add-stock', '/inventory/adjustments', '/inventory/issues', '/inventory/suppliers', '/inventory/guide'];
const isInventory = (route: string) => route === '/inventory' || route.startsWith('/inventory/');
/** Pages added after Inventory, on purpose, by other modules. The frozen golden predates them, so they are set aside here (never added to the golden). */
const ADDED_AFTER_INVENTORY = ['/school-setup/notifications', '/school-setup/academic-setup', '/student-documents', '/parent-details', '/duplicate-parents', '/promotion', '/schools/:schoolId/whatsapp'];
const shape = (items: ReturnType<typeof visibleNav>) => items.map((i) => ({ label: i.label, route: i.route ?? null, children: i.children?.filter((c) => !ADDED_AFTER_INVENTORY.includes(c.route ?? '')).map((c) => `${c.label}|${c.route}`) ?? null }));

describe('Inventory leaves every other role exactly as it was', () => {
  it('the golden snapshot covers every pre-existing role', () => {
    expect(Object.keys(PRE_INVENTORY_GOLDEN).sort()).toEqual(Object.keys(ROLE_SETS).sort());
    expect(Object.keys(PRE_INVENTORY_GOLDEN).length).toBeGreaterThanOrEqual(12);
  });

  for (const [name, roles] of Object.entries(ROLE_SETS)) {
    describe(name, () => {
      const golden = PRE_INVENTORY_GOLDEN[name];

      it('sees the same menu, apart from the new Inventory group', () => {
        expect(shape(visibleNav(roles, true).filter((i) => i.label !== 'Inventory'))).toEqual(golden.menu);
        expect(visibleNav(roles, false).filter((i) => i.label !== 'Inventory').map((i) => i.label)).toEqual(golden.menuNoEmployeeRecord);
      });

      it('can open exactly the same routes, apart from the /inventory ones', () => {
        const now = Object.keys(ROUTE_ROLES).filter((p) => !isInventory(p) && !ADDED_AFTER_INVENTORY.includes(p) && canAccess(p, roles)).sort();
        expect(now).toEqual(golden.routes);
      });

      it('gets Inventory only if it is School Admin, Principal (read-only) or Store Keeper', () => {
        const inventoryRoles = [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL];
        const gets = roles.some((r) => inventoryRoles.includes(r as never));
        expect(visibleNav(roles).some((i) => i.label === 'Inventory')).toBe(gets);
        expect(INVENTORY_ROUTES.filter((r) => canAccess(r, roles)).length > 0).toBe(gets);
      });
    });
  }

  it('the new Inventory pages are the only additions to ROUTE_ROLES', () => {
    const before = new Set(Object.values(PRE_INVENTORY_GOLDEN).flatMap((g) => g.routes));
    const added = Object.keys(ROUTE_ROLES).filter((p) => isInventory(p)).sort();
    expect(added).toEqual([...INVENTORY_ROUTES].sort());
    for (const p of added) expect(before.has(p)).toBe(false);
  });

  it('the top-bar search offers no Inventory page to a role that has no Inventory access', () => {
    for (const roles of [[ROLES.TEACHER], [ROLES.ACCOUNTANT], [ROLES.STUDENT], [ROLES.GUARDIAN], [ROLES.ADMISSION_STAFF], [ROLES.SUPER_ADMIN], [ROLES.DRIVER]]) {
      expect(searchNav('inventory', roles).length + searchNav('stock', roles).length + searchNav('suppliers', roles).length).toBe(0);
    }
  });
});

describe('Inventory access by role', () => {
  const labelsOf = (roles: string[]) => visibleNav(roles).find((i) => i.label === 'Inventory')?.children?.map((c) => c.label);

  it('School Admin and Store Keeper get all seven tabs and can open every page', () => {
    for (const role of [ROLES.SCHOOL_ADMIN, ROLES.STORE_KEEPER]) {
      expect(labelsOf([role])).toEqual(['Dashboard', 'Items / Categories', 'Add Stock', 'Adjustments', 'Issue / Return', 'Suppliers', 'Guide']);
      for (const r of INVENTORY_ROUTES) expect([r, canAccess(r, [role])]).toEqual([r, true]);
    }
  });

  it('the Principal gets Dashboard and Items only, with no write screen at all', () => {
    expect(labelsOf([ROLES.PRINCIPAL])).toEqual(['Dashboard', 'Items / Categories']);
    for (const r of ['/inventory', '/inventory/items']) expect(canAccess(r, [ROLES.PRINCIPAL])).toBe(true);
    for (const r of INVENTORY_ROUTES.slice(2)) expect([r, canAccess(r, [ROLES.PRINCIPAL])]).toEqual([r, false]);
  });

  it('nobody else can open any Inventory page', () => {
    for (const role of [ROLES.TEACHER, ROLES.ACCOUNTANT, ROLES.ADMISSION_STAFF, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.SUPER_ADMIN, ROLES.DRIVER, 'Test Role']) {
      for (const r of INVENTORY_ROUTES) expect([r, role, canAccess(r, [role])]).toEqual([r, role, false]);
    }
  });

  it('a Store Keeper reaches the dashboard, change-password, Inventory and the four shared employee pages (Calendar, My Notices, My Leave, My Profile), nothing else', () => {
    const open = Object.keys(ROUTE_ROLES).filter((p) => canAccess(p, [ROLES.STORE_KEEPER])).sort();
    expect(open).toEqual(['/change-password', '/dashboard', '/calendar', '/notices/my', '/leave/my', '/my-profile', ...INVENTORY_ROUTES].sort());
    expect(visibleNav([ROLES.STORE_KEEPER]).map((i) => i.label)).toEqual(['Dashboard', 'Inventory', 'Calendar', 'My Notices', 'My Leave']);
    // a Store Keeper login is an employee; without an employee record My Leave is hidden, like for every other role
    expect(visibleNav([ROLES.STORE_KEEPER], false).map((i) => i.label)).toEqual(['Dashboard', 'Inventory', 'Calendar', 'My Notices']);
  });

  it('adding the Store Keeper to Calendar, My Notices, My Leave and My Profile changed nobody else: each page is open to exactly the roles the golden snapshot says', () => {
    const sharedPages = ['/calendar', '/notices/my', '/leave/my', '/my-profile'];
    for (const route of sharedPages) {
      const before = Object.entries(PRE_INVENTORY_GOLDEN).filter(([, g]) => g.routes.includes(route)).map(([name]) => name).sort();
      const now = Object.entries(ROLE_SETS).filter(([, roles]) => canAccess(route, roles)).map(([name]) => name).sort();
      expect([route, now]).toEqual([route, before]);
      expect([route, canAccess(route, [ROLES.STORE_KEEPER])]).toEqual([route, true]);
    }
  });

  it('the Store Keeper still has no Fees, Students, Employees, Leave-approval, Homework, Notice-publishing or Attendance page', () => {
    for (const route of ['/fee-dashboard', '/students', '/employees', '/leave/requests', '/leave/students', '/homework', '/notices', '/attendance/mark', '/school-setup/classes', '/admissions', '/school-profile', '/timetable/my']) {
      expect([route, canAccess(route, [ROLES.STORE_KEEPER])]).toEqual([route, false]);
    }
  });

  it('every sidebar link of the Inventory group has a ROUTE_ROLES entry', () => {
    const group = NAV_ITEMS.find((i) => i.label === 'Inventory');
    expect(group?.children?.map((c) => c.route)).toEqual(INVENTORY_ROUTES);
    for (const c of group!.children!) expect(c.route in ROUTE_ROLES).toBe(true);
  });
});
