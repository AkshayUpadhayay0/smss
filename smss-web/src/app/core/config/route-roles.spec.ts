import { Route } from '@angular/router';
import { routes } from '../../app.routes';
import { NAV_ITEMS } from '../../layouts/main-layout/nav.config';
import { AUTHENTICATED, ROLES, ROUTE_ROLES, accessFor, canAccess } from './route-roles';

describe('route-roles', () => {
  describe('accessFor', () => {
    it('matches exact routes and :param routes, ignoring query, hash and trailing slash', () => {
      expect(accessFor('/schools')).toEqual([ROLES.SUPER_ADMIN]);
      expect(accessFor('/schools/')).toEqual([ROLES.SUPER_ADMIN]);
      expect(accessFor('/schools?x=1#top')).toEqual([ROLES.SUPER_ADMIN]);
      expect(accessFor('/schools/sch2026001/edit')).toEqual([ROLES.SUPER_ADMIN]);
      expect(accessFor('/schools/sch2026001/view?tab=1')).toEqual([ROLES.SUPER_ADMIN]);
      expect(accessFor('/dashboard')).toBe(AUTHENTICATED);
    });

    it('does not let a :param swallow extra or missing segments', () => {
      expect(accessFor('/schools/a/b/edit')).toBeNull();
      expect(accessFor('/schools/edit')).toBeNull();
      expect(accessFor('/schools/sch1')).toBeNull();
    });

    it('returns null for anything not listed', () => {
      for (const url of ['/', '/nope', '/masters', '/masters/unknown', '/login']) expect(accessFor(url)).toBeNull();
    });
  });

  describe('subscription billing (Super Admin only)', () => {
    it('/subscriptions and /invoices: the Super Admin may open them; no school role may', () => {
      for (const path of ['/subscriptions', '/invoices']) {
        expect(canAccess(path, [ROLES.SUPER_ADMIN])).toBe(true);
        for (const role of [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER, ROLES.ACCOUNTANT, ROLES.ADMISSION_STAFF, ROLES.STUDENT, ROLES.GUARDIAN, 'Test Role']) expect(canAccess(path, [role])).toBe(false);
        expect(canAccess(path + '?status=TRIAL', [ROLES.SUPER_ADMIN])).toBe(true); // query parameters do not change the route
      }
    });
  });

  describe('company finance (Super Admin only)', () => {
    it('/company/overview, /company/employees and /company/expenses: the Super Admin may open them; no school role may', () => {
      for (const path of ['/company/overview', '/company/employees', '/company/expenses']) {
        expect(canAccess(path, [ROLES.SUPER_ADMIN])).toBe(true);
        for (const role of [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER, ROLES.ACCOUNTANT, ROLES.ADMISSION_STAFF, ROLES.STUDENT, ROLES.GUARDIAN, 'Test Role']) expect(canAccess(path, [role])).toBe(false);
      }
      expect(canAccess('/company/expenses?from=2026-04-01&to=2026-10-31', [ROLES.SUPER_ADMIN])).toBe(true);
      expect(canAccess('/company/payroll', [ROLES.SUPER_ADMIN])).toBe(false); // anything not listed stays closed
    });
  });

  describe('student record for the Accountant', () => {
    it('may open /students/:id (Fees tab only, enforced by the page) but not the student list, onboarding or admissions', () => {
      expect(canAccess('/students/DPS001ST001', [ROLES.ACCOUNTANT])).toBe(true);
      for (const url of ['/students', '/students/onboard-existing', '/admissions', '/school-profile', '/fee-setup/fee-types']) expect(canAccess(url, [ROLES.ACCOUNTANT])).toBe(false);
      for (const role of [ROLES.PRINCIPAL, ROLES.TEACHER, ROLES.STUDENT]) expect(canAccess('/students/DPS001ST001', [role])).toBe(false);
    });
  });

  describe('calendar', () => {
    it('/calendar: every role of a school may open it; a Super Admin (no school) and a custom role may not', () => {
      for (const role of [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER, ROLES.ACCOUNTANT, ROLES.STUDENT, ROLES.GUARDIAN]) expect(canAccess('/calendar', [role])).toBe(true);
      for (const role of [ROLES.SUPER_ADMIN, 'Test Role']) expect(canAccess('/calendar', [role])).toBe(false);
    });
  });

  describe('attendance', () => {
    it('/timetable and /school-setup/periods are School Admin only; /timetable/my is the teacher own page', () => {
      for (const path of ['/timetable', '/school-setup/periods']) {
        expect(canAccess(path, [ROLES.SCHOOL_ADMIN])).toBe(true);
        for (const role of [ROLES.TEACHER, ROLES.PRINCIPAL, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.ACCOUNTANT, ROLES.SUPER_ADMIN]) expect(canAccess(path, [role])).toBe(false);
      }
      expect(canAccess('/timetable/my', [ROLES.TEACHER])).toBe(true);
      for (const role of [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.ACCOUNTANT, ROLES.SUPER_ADMIN]) expect(canAccess('/timetable/my', [role])).toBe(false);
    });

    it('admissions: School Admin, Principal and Admission Staff work applications; nobody else does; the direct Add Existing Student form is School Admin only', () => {
      for (const path of ['/admissions', '/admissions/new', '/admissions/:admissionId', '/admissions/:admissionId/edit']) {
        for (const role of [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.ADMISSION_STAFF]) expect(canAccess(path, [role])).toBe(true);
        for (const role of [ROLES.TEACHER, ROLES.ACCOUNTANT, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.SUPER_ADMIN]) expect(canAccess(path, [role])).toBe(false);
      }
      expect(canAccess('/students/onboard-existing', [ROLES.SCHOOL_ADMIN])).toBe(true);
      for (const role of [ROLES.PRINCIPAL, ROLES.ADMISSION_STAFF]) expect(canAccess('/students/onboard-existing', [role])).toBe(false);
      for (const url of ['/students', '/students/:studentId', '/employees', '/fee-dashboard', '/school-setup/classes']) expect(canAccess(url, [ROLES.ADMISSION_STAFF])).toBe(false);
    });

    it('/assessments: Teacher and School Admin; /assessments/my: Student only. Principal, Accountant, Admission Staff, Guardian and Super Admin get neither', () => {
      for (const role of [ROLES.TEACHER, ROLES.SCHOOL_ADMIN]) expect(canAccess('/assessments', [role])).toBe(true);
      for (const role of [ROLES.PRINCIPAL, ROLES.ACCOUNTANT, ROLES.ADMISSION_STAFF, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.SUPER_ADMIN]) expect(canAccess('/assessments', [role])).toBe(false);
      expect(canAccess('/assessments/my', [ROLES.STUDENT])).toBe(true);
      for (const role of [ROLES.TEACHER, ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.ACCOUNTANT, ROLES.ADMISSION_STAFF, ROLES.GUARDIAN, ROLES.SUPER_ADMIN]) expect(canAccess('/assessments/my', [role])).toBe(false);
    });

    it('/notices: School Admin and Principal only; /notices/my: every school role, but not the Super Admin', () => {
      for (const role of [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL]) expect(canAccess('/notices', [role])).toBe(true);
      for (const role of [ROLES.TEACHER, ROLES.ACCOUNTANT, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.ADMISSION_STAFF, ROLES.SUPER_ADMIN]) expect(canAccess('/notices', [role])).toBe(false);
      for (const role of [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.TEACHER, ROLES.ACCOUNTANT, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.ADMISSION_STAFF]) expect(canAccess('/notices/my', [role])).toBe(true);
      expect(canAccess('/notices/my', [ROLES.SUPER_ADMIN])).toBe(false);
    });

    it('/homework: Teacher and School Admin; /homework/my: Student only. Principal, Accountant, Guardian and Super Admin get neither', () => {
      for (const role of [ROLES.TEACHER, ROLES.SCHOOL_ADMIN]) expect(canAccess('/homework', [role])).toBe(true);
      for (const role of [ROLES.PRINCIPAL, ROLES.ACCOUNTANT, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.SUPER_ADMIN]) expect(canAccess('/homework', [role])).toBe(false);
      expect(canAccess('/homework/my', [ROLES.STUDENT])).toBe(true);
      for (const role of [ROLES.TEACHER, ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL, ROLES.ACCOUNTANT, ROLES.GUARDIAN, ROLES.SUPER_ADMIN]) expect(canAccess('/homework/my', [role])).toBe(false);
    });

    it('/leave/my: staff with a login (Teacher, Principal, Accountant, School Admin) - not Student, Guardian or Super Admin', () => {
      for (const role of [ROLES.TEACHER, ROLES.PRINCIPAL, ROLES.ACCOUNTANT, ROLES.SCHOOL_ADMIN]) expect(canAccess('/leave/my', [role])).toBe(true);
      for (const role of [ROLES.STUDENT, ROLES.GUARDIAN, ROLES.SUPER_ADMIN]) expect(canAccess('/leave/my', [role])).toBe(false);
    });

    it('/leave/requests: School Admin and Principal decide staff leave; /leave/students: Teacher and School Admin handle student leave', () => {
      for (const role of [ROLES.SCHOOL_ADMIN, ROLES.PRINCIPAL]) expect(canAccess('/leave/requests', [role])).toBe(true);
      for (const role of [ROLES.TEACHER, ROLES.ACCOUNTANT, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.SUPER_ADMIN]) expect(canAccess('/leave/requests', [role])).toBe(false);
      for (const role of [ROLES.TEACHER, ROLES.SCHOOL_ADMIN]) expect(canAccess('/leave/students', [role])).toBe(true);
      for (const role of [ROLES.PRINCIPAL, ROLES.ACCOUNTANT, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.SUPER_ADMIN]) expect(canAccess('/leave/students', [role])).toBe(false);
    });

    it('/attendance/mark: Teacher and School Admin may open it; Student, Guardian, Accountant, Principal and Super Admin may not', () => {
      for (const role of [ROLES.TEACHER, ROLES.SCHOOL_ADMIN]) expect(canAccess('/attendance/mark', [role])).toBe(true);
      for (const role of [ROLES.STUDENT, ROLES.GUARDIAN, ROLES.ACCOUNTANT, ROLES.PRINCIPAL, ROLES.SUPER_ADMIN]) expect(canAccess('/attendance/mark', [role])).toBe(false);
    });
  });

  describe('employees', () => {
    it.each(['/employees', '/employees/new', '/employees/DPS001EM001', '/employees/DPS001EM001/edit'])('%s is School Admin only', (url) => {
      expect(canAccess(url, [ROLES.SCHOOL_ADMIN])).toBe(true);
      for (const role of [ROLES.ACCOUNTANT, ROLES.PRINCIPAL, ROLES.TEACHER, ROLES.STUDENT, ROLES.GUARDIAN, ROLES.SUPER_ADMIN]) expect(canAccess(url, [role])).toBe(false);
    });
  });

  describe('fee dashboard and reports', () => {
    it.each(['/fee-dashboard', '/fee-reports/class'])('%s: School Admin, Accountant and Principal (view-only) may open it; Teacher, Student and Guardian may not', (url) => {
      for (const role of [ROLES.SCHOOL_ADMIN, ROLES.ACCOUNTANT, ROLES.PRINCIPAL]) expect(canAccess(url, [role])).toBe(true);
      for (const role of [ROLES.TEACHER, ROLES.STUDENT, ROLES.GUARDIAN]) expect(canAccess(url, [role])).toBe(false);
    });
  });

  describe('canAccess', () => {
    const SUPER = [ROLES.SUPER_ADMIN];
    const SCHOOL = [ROLES.SCHOOL_ADMIN];

    it('Super Admin reaches everything that is listed except school-side pages', () => {
      const schoolSide = ['/school-profile', '/students', '/students/:studentId', '/students/onboard-existing', '/admissions', '/admissions/new', '/admissions/:admissionId', '/school-setup/import-masters', '/school-setup/academic-year', '/school-setup/classes', '/school-setup/sections', '/school-setup/subjects', '/school-setup/document-types', '/school-setup/class-subjects', '/school-setup/employee-designations', '/school-setup/employee-departments', '/school-setup/admission-types', '/fee-setup/fee-types', '/fee-setup/fee-structures', '/fee-setup/fee-concessions', '/fee-setup/fee-structures/new', '/fee-setup/fee-structures/:feeStructureId/edit', '/fee-dashboard', '/fee-reports/class', '/employees', '/employees/new', '/employees/:employeeId', '/employees/:employeeId/edit', '/attendance/mark', '/calendar', '/school-setup/periods', '/timetable', '/timetable/my', '/leave/my', '/leave/requests', '/leave/students', '/homework', '/homework/my', '/notices', '/notices/my', '/admissions/:admissionId/edit', '/assessments', '/assessments/my'];
      for (const url of Object.keys(ROUTE_ROLES).map((p) => p.replace(':schoolId', 'sch1'))) {
        expect([url, canAccess(url, SUPER)]).toEqual([url, !schoolSide.includes(url)]);
      }
    });

    it('School Admin only reaches the open-to-everyone routes and their own School Profile', () => {
      expect(canAccess('/dashboard', SCHOOL)).toBe(true);
      expect(canAccess('/school-profile', SCHOOL)).toBe(true);
      expect(canAccess('/school-setup/academic-year', SCHOOL)).toBe(true);
      expect(canAccess('/school-setup/classes', SCHOOL)).toBe(true);
      expect(canAccess('/school-setup/sections', SCHOOL)).toBe(true);
      expect(canAccess('/school-setup/subjects', SCHOOL)).toBe(true);
      expect(canAccess('/change-password', SCHOOL)).toBe(true);
      for (const url of ['/schools', '/schools/add', '/schools/sch1/edit', '/schools/sch1/view', '/masters/board-type', '/masters/school-type', '/masters/school-level', '/masters/status', '/masters/role']) {
        expect([url, canAccess(url, SCHOOL)]).toEqual([url, false]);
      }
    });

    it('open routes work for any signed-in user, including custom roles and users with no roles', () => {
      expect(canAccess('/dashboard', ['Test Role'])).toBe(true);
      expect(canAccess('/dashboard', [])).toBe(true);
      expect(canAccess('/schools', [])).toBe(false);
      expect(canAccess('/schools', ['Test Role'])).toBe(false);
    });

    it('role names are compared ignoring case and surrounding spaces', () => {
      expect(canAccess('/schools', ['super admin'])).toBe(true);
      expect(canAccess('/schools', ['  Super Admin '])).toBe(true);
    });

    it('FAILS CLOSED: an unlisted route is denied even for Super Admin', () => {
      expect(canAccess('/not-listed', SUPER)).toBe(false);
      expect(canAccess('/schools/x/y/z', SUPER)).toBe(false);
    });
  });

  describe('stays in sync with the app', () => {
    /** Absolute patterns of every real page behind the main layout (its guarded subtree). */
    async function pagesBehindMainLayout(): Promise<string[]> {
      const walk = async (list: Route[], prefix: string): Promise<string[]> => {
        const found: string[] = [];
        for (const r of list) {
          if (r.redirectTo !== undefined || r.path === '**') continue;
          const here = [prefix, r.path].filter(Boolean).join('/');
          const children = r.children ?? (r.loadChildren ? ((await r.loadChildren()) as Route[]) : null);
          if (children) found.push(...(await walk(children, here)));
          else if (r.component || r.loadComponent) found.push('/' + here);
        }
        return found;
      };
      const main = routes.find((r) => r.canActivateChild?.length);
      expect(main, 'main layout route with the role guard').toBeDefined();
      return walk(main!.children!, '');
    }

    it('every page behind the main layout has a ROUTE_ROLES entry (nothing silently blocked)', async () => {
      const pages = await pagesBehindMainLayout();
      expect(pages.length).toBeGreaterThan(8);
      const unlisted = pages.filter((p) => !(p in ROUTE_ROLES));
      expect(unlisted, `add these to ROUTE_ROLES: ${unlisted.join(', ')}`).toEqual([]);
    });

    it('ROUTE_ROLES has no stale entries for routes that no longer exist', async () => {
      const pages = new Set(await pagesBehindMainLayout());
      const stale = Object.keys(ROUTE_ROLES).filter((p) => !pages.has(p));
      expect(stale, `remove these from ROUTE_ROLES: ${stale.join(', ')}`).toEqual([]);
    });

    it('every sidebar link is covered, so nothing in the menu is permanently hidden by accident', () => {
      const links = NAV_ITEMS.flatMap((i) => (i.children ? i.children.map((c) => c.route) : i.route ? [i.route] : []));
      expect(links.length).toBeGreaterThan(5);
      for (const link of links) expect([link, accessFor(link) !== null]).toEqual([link, true]);
    });
  });
});
