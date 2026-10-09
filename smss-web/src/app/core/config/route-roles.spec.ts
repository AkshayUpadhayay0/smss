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

  describe('canAccess', () => {
    const SUPER = [ROLES.SUPER_ADMIN];
    const SCHOOL = [ROLES.SCHOOL_ADMIN];

    it('Super Admin reaches everything that is listed except school-side pages', () => {
      const schoolSide = ['/school-profile', '/school-setup/academic-year', '/school-setup/classes', '/school-setup/sections', '/school-setup/subjects', '/school-setup/document-types', '/school-setup/class-subjects', '/school-setup/employee-designations', '/school-setup/employee-departments', '/school-setup/admission-types'];
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
