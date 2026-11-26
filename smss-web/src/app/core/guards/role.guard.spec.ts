import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { clearSessions, makeUser, seedSession } from '../testing/auth-test-utils';
import { roleGuard } from './role.guard';

describe('roleGuard: the parent shell and the staff shell, in both directions', () => {
  function run(roles: string[] | null, url: string): boolean | string {
    TestBed.resetTestingModule();
    clearSessions();
    if (roles) seedSession({ user: makeUser({ schoolId: 'S', roles, userType: roles.includes('Guardian') ? 'Guardian' : 'Employee' }) });
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const result = TestBed.runInInjectionContext(() => roleGuard(new ActivatedRouteSnapshot(), { url } as RouterStateSnapshot));
    return result === true ? true : TestBed.inject(Router).serializeUrl(result as UrlTree);
  }
  afterEach(() => clearSessions());

  it('a Guardian opens the parent pages', () => {
    for (const url of ['/family', '/family/children/GHS001ST001', '/family/children/GHS001ST001?tab=fees', '/family/notices', '/family/calendar', '/family/account', '/family/account/change-password']) {
      expect([url, run(['Guardian'], url)]).toEqual([url, true]);
    }
  });

  it('a Guardian who opens ANY other URL (a staff page, the dashboard, a typo) lands on /family', () => {
    for (const url of ['/students', '/fee-dashboard', '/school-profile', '/calendar', '/notices/my', '/dashboard', '/change-password', '/employees', '/family/unknown', '/inventory', '/nothing-here']) {
      expect([url, run(['Guardian'], url)]).toEqual([url, '/family']);
    }
  });

  it('a Student and every staff role that opens a parent page is sent to the dashboard (fail closed)', () => {
    for (const roles of [['Student'], ['School Admin'], ['Principal'], ['Teacher'], ['Accountant'], ['Admission Staff'], ['Store Keeper'], ['Super Admin'], ['Driver'], ['Custom Role']]) {
      for (const url of ['/family', '/family/children/GHS001ST001', '/family/notices']) expect([roles.join(), url, run(roles, url)]).toEqual([roles.join(), url, '/dashboard']);
    }
  });

  it('staff still open their own pages exactly as before, and nobody signed out gets in anywhere', () => {
    expect(run(['School Admin'], '/students')).toBe(true);
    expect(run(['Teacher'], '/my-students')).toBe(true);
    expect(run(['Student'], '/dashboard')).toBe(true);
    expect(run(null, '/family')).toBe('/login?returnUrl=%2Ffamily');
  });
});
