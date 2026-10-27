import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { HttpTestingController } from '@angular/common/http/testing';
import { ToastService } from './core/services/toast.service';
import { clearSessions, makeUser, seedSession } from './core/testing/auth-test-utils';

describe('app routes', () => {
  beforeEach(() => {
    clearSessions();
    // jsdom has no matchMedia (used by MainLayoutComponent)
    window.matchMedia = ((query: string) => ({ matches: false, media: query })) as typeof window.matchMedia;
    TestBed.configureTestingModule({
      providers: [provideRouter(routes, withComponentInputBinding()), provideHttpClient(), provideHttpClientTesting()],
    });
  });
  afterEach(clearSessions);

  let sharedHarness: RouterTestingHarness | null = null;
  beforeEach(() => (sharedHarness = null));

  async function go(from: string) {
    // Angular allows ONE harness per test, so repeated go() calls in a test reuse it.
    const harness = (sharedHarness ??= await RouterTestingHarness.create());
    // The harness starts at "/", and same-URL navigations are ignored, so begin somewhere else.
    await harness.navigateByUrl('/nowhere-start');
    await harness.navigateByUrl(from);
    return { harness, url: TestBed.inject(Router).url };
  }
  const heading = (h: RouterTestingHarness) => h.routeNativeElement?.querySelector('h1, h2')?.textContent?.trim();

  describe('signed in', () => {
    beforeEach(() => seedSession());

    const cases: [string, string, string][] = [
      ['/', '/dashboard', 'Dashboard'],
      ['/dashboard', '/dashboard', 'Dashboard'],
      ['/schools', '/schools', 'Registered Schools'],
      ['/masters', '/masters/board-type', 'Board Type'],
      ['/masters/school-type', '/masters/school-type', 'School Type'],
      ['/masters/school-level', '/masters/school-level', 'School Level'],
      ['/masters/status', '/masters/status', 'Status'],
      ['/masters/role', '/masters/role', 'Role'],
      ['/change-password', '/change-password', 'Change Password'],
      ['/nope', '/dashboard', 'Dashboard'],
    ];

    it.each(cases)('%s -> %s shows "%s"', async (from, url, expected) => {
      const result = await go(from);
      expect(result.url).toBe(url);
      expect(heading(result.harness)).toBe(expected);
    });

    it('keeps signed-in users off the login page', async () => {
      expect((await go('/login')).url).toBe('/dashboard');
    });

    it('shows the signed-in user in the top bar', async () => {
      const { harness } = await go('/dashboard');
      const text = harness.routeNativeElement?.textContent ?? '';
      expect(text).toContain('SMSS Admin'); // organization
      expect(text).toContain('Akshay Upadhayay'); // the person
      expect(text).toContain('SUAD01');
      expect(text).toContain('Super Admin');
    });
  });

  describe('signed out', () => {
    it.each(['/dashboard', '/schools', '/schools/add', '/masters/board-type', '/change-password'])('%s redirects to /login and remembers where you were going', async (path) => {
      const { url } = await go(path);
      expect(url).toBe(`/login?returnUrl=${encodeURIComponent(path)}`);
    });

    it('/ also lands on the login page', async () => {
      expect((await go('/')).url).toContain('/login');
    });

    it('shows the sign-in form on /login', async () => {
      const { harness, url } = await go('/login');
      expect(url).toBe('/login');
      expect(heading(harness)).toBe('Sign in');
    });

    it('an expired refresh token counts as signed out', async () => {
      seedSession({ refreshTokenExpiresAt: new Date(Date.now() - 1000).toISOString() });
      expect((await go('/dashboard')).url).toContain('/login');
    });
  });

  describe('first login (temporary password)', () => {
    beforeEach(() => seedSession({ user: makeUser({ isFirstLogin: true }) }));

    it.each(['/dashboard', '/schools', '/masters/board-type', '/nope'])('%s is redirected to /change-password', async (from) => {
      const { url, harness } = await go(from);
      expect(url).toBe('/change-password');
      expect(heading(harness)).toBe('Change Password');
    });

    it('stays blocked when navigating from the change-password page to another page', async () => {
      const { harness } = await go('/change-password');
      await harness.navigateByUrl('/schools');
      expect(TestBed.inject(Router).url).toBe('/change-password');
    });

    it('is not blocked once the flag is cleared', async () => {
      clearSessions();
      seedSession({ user: makeUser({ isFirstLogin: false }) });
      expect((await go('/dashboard')).url).toBe('/dashboard');
    });
  });

  describe('role-based access', () => {
    const asSchoolAdmin = () => seedSession({ user: makeUser({ displayName: 'ZZ Role Test School', orgUserId: 'sch2026007', userType: 'School Id', schoolName: 'ZZ Role Test School', schoolCode: '99999999901', roles: ['School Admin'] }) });
    /** Visible menu labels, in order, e.g. "Dashboard | Registered Schools | Masters". */
    const nav = (h: RouterTestingHarness) =>
      [...(h.routeNativeElement?.querySelectorAll('app-sidebar nav a, app-sidebar nav button') ?? [])].map((e) => e.textContent?.replace(/\s+/g, ' ').trim()).join(' | ');

    describe('School Admin', () => {
      beforeEach(asSchoolAdmin);

      it('/students/onboard-existing opens the onboarding form, not a student profile (route order)', async () => {
        const { harness, url } = await go('/students/onboard-existing');
        expect(url).toBe('/students/onboard-existing');
        expect(heading(harness)).toBe('Add Existing Student');
      });

      it('highlights only "Add Existing Student" (not "Students") on /students/onboard-existing', async () => {
        const { harness } = await go('/students/onboard-existing');
        const active = [...(harness.routeNativeElement?.querySelectorAll('app-sidebar nav a.active') ?? [])].map((e) => e.textContent?.replace(/\s+/g, ' ').trim());
        expect(active).toEqual(['Add Existing Student']);
      });

      it.each(['/students', '/students/DPS001ST001'])('highlights only "Students" on %s', async (path) => {
        const { harness } = await go(path);
        const active = [...(harness.routeNativeElement?.querySelectorAll('app-sidebar nav a.active') ?? [])].map((e) => e.textContent?.replace(/\s+/g, ' ').trim());
        expect(active).toEqual(['Students']);
      });

      it('/students/:studentId opens a student profile', async () => {
        const { harness, url } = await go('/students/42');
        expect(url).toBe('/students/42');
        expect(heading(harness)).not.toBe('Add Existing Student');
        expect(harness.routeNativeElement?.querySelector('app-student-profile')).not.toBeNull();
      });

      it('sees Dashboard, Calendar, Timetable, Mark Attendance, Homework, Assessments, My Leave, Leave Requests, Student Leave, School Profile, Students, Admissions, Add Existing Student, School Setup, Employees, Fee Setup and Fees in the menu — no Registered Schools, no Masters', async () => {
        const { harness } = await go('/dashboard');
        expect(nav(harness)).toBe('Dashboard | Calendar | Timetable | Mark Attendance | Notices | My Notices | Homework | Assessments | My Leave | Leave Requests | Student Leave | School Profile | Students | Admissions | Add Existing Student | School Setup | Employees | Fee Setup | Fees');
      });

      it.each(['/schools', '/schools/add', '/schools/sch2026001/edit', '/schools/sch2026001/view', '/masters/board-type', '/masters/school-type', '/masters/school-level', '/masters/status', '/masters/role', '/dev/components-preview'])(
        '%s is refused: redirected to /dashboard with a toast, page never rendered, no API call made',
        async (path) => {
          const http = TestBed.inject(HttpTestingController);
          const { harness, url } = await go(path);
          expect(url).toBe('/dashboard');
          expect(heading(harness)).toBe('Dashboard');
          const text = harness.routeNativeElement?.textContent ?? '';
          expect(text).not.toContain('Registered Schools');
          expect(text).not.toContain('Board Type');
          http.expectNone((r) => r.url.includes('/api/SchoolRegistration') || r.url.includes('/api/MasterData'));

          const denied = TestBed.inject(ToastService).toasts().filter((t) => t.title === 'Access denied');
          expect(denied).toHaveLength(1);
          expect(denied[0].type).toBe('error');
        },
      );

      it('is also refused when navigating from one page to another inside the layout (guard re-runs per navigation)', async () => {
        const { harness } = await go('/dashboard');
        await harness.navigateByUrl('/schools');
        expect(TestBed.inject(Router).url).toBe('/dashboard');
        await harness.navigateByUrl('/masters/role');
        expect(TestBed.inject(Router).url).toBe('/dashboard');
      });

      it('can still open Dashboard and Change password', async () => {
        expect((await go('/dashboard')).url).toBe('/dashboard');
        const cp = await go('/change-password');
        expect(cp.url).toBe('/change-password');
        expect(heading(cp.harness)).toBe('Change Password');
        expect(TestBed.inject(ToastService).toasts()).toHaveLength(0);
      });

      it('gets the right menu on first render after a page reload (no Super Admin menu flash)', async () => {
        // A reload = brand-new app, session restored from storage, deep link straight to a Super Admin page.
        const { harness, url } = await go('/masters/status');
        expect(url).toBe('/dashboard');
        expect(nav(harness)).not.toMatch(/Schools|Masters|Board Type|Role/);
      });
    });

    describe('Super Admin', () => {
      beforeEach(() => seedSession());

      it('sees Dashboard, Registered Schools and Masters, and every Masters child', async () => {
        const { harness } = await go('/dashboard');
        harness.routeNativeElement!.querySelector<HTMLButtonElement>('.group-toggle')?.click();
        harness.detectChanges();
        expect(nav(harness)).toBe('Dashboard | Registered Schools | Masters | Board Type | School Type | School Level | Status | Role | Religion/Caste Category | Blood Group | Gender | Student Category');
      });

      it.each(['/schools', '/schools/add', '/schools/sch1/edit', '/schools/sch1/view', '/masters/board-type', '/masters/school-type', '/masters/school-level', '/masters/status', '/masters/role', '/change-password'])(
        '%s opens',
        async (path) => {
          expect((await go(path)).url).toBe(path);
          expect(TestBed.inject(ToastService).toasts().filter((t) => t.title === 'Access denied')).toHaveLength(0);
        },
      );
    });

    describe.each([
      ['Teacher', { orgUserId: 'DPS001EM001', userType: 'Employee', displayName: 'Anita Verma' }, 'Dashboard | Calendar | My Timetable | Mark Attendance | My Notices | Homework | Assessments | My Leave | Student Leave', 'Your classroom at a glance'],
      ['Student', { orgUserId: 'DPS001ST001', userType: 'Student', displayName: 'Kiran Kid' }, 'Dashboard | Calendar | My Notices | My Homework | My Results', 'Your school day at a glance'],
    ])('%s login', (role, who, menu, dashboardText) => {
      beforeEach(() => seedSession({ user: makeUser({ ...who, roles: [role], schoolId: 'DPS261013001', schoolName: 'Delhi Public School' }) }));

      it('lands on the Dashboard (a Student and a Teacher each get their own dashboard) and sees only its own menu', async () => {
        const { harness, url } = await go('/dashboard');
        expect(url).toBe('/dashboard');
        expect(heading(harness)).toBe('Dashboard');
        expect(nav(harness)).toBe(menu);
        expect(harness.routeNativeElement?.textContent).toContain(dashboardText);
      });

      it.each(['/students', '/employees', '/school-setup/sections', '/fee-dashboard', '/admissions'])('%s is refused: back to the Dashboard, no API call', async (path) => {
        const http = TestBed.inject(HttpTestingController);
        const { url } = await go(path);
        expect(url).toBe('/dashboard');
        // a Student's own dashboard calls /api/StudentDashboard, which is allowed; any other student / employee / section call is not
        http.expectNone((r) => r.url.includes('/api/Employee') || (r.url.includes('/api/Student') && !r.url.includes('/api/StudentDashboard')) || r.url.includes('/api/Sections'));
      });
    });

    describe('Admission Staff login', () => {
      beforeEach(() => seedSession({ user: makeUser({ orgUserId: 'DPS001EM009', userType: 'Employee', displayName: 'Asha Staff', roles: ['Admission Staff'], schoolId: 'DPS261013001', schoolName: 'Delhi Public School' }) }));

      it('the menu is Dashboard, Calendar and Admissions - nothing else', async () => {
        const { harness } = await go('/dashboard');
        expect(nav(harness)).toBe('Dashboard | Calendar | My Notices | Admissions');
      });

      it.each(['/admissions', '/admissions/new', '/admissions/7', '/admissions/7/edit'])('%s opens', async (path) => {
        const { url } = await go(path);
        expect(url).toBe(path);
        expect(TestBed.inject(ToastService).toasts().filter((t) => t.title === 'Access denied')).toHaveLength(0);
      });

      it.each(['/students', '/students/onboard-existing', '/employees', '/school-setup/classes', '/fee-dashboard', '/leave/requests'])('%s is refused: back to the Dashboard', async (path) => {
        const { url } = await go(path);
        expect(url).toBe('/dashboard');
      });
    });

    it('a user with a custom role still gets the Dashboard (no redirect loop) but nothing else', async () => {
      seedSession({ user: makeUser({ roles: ['Test Role'] }) });
      const { harness } = await go('/dashboard');
      expect(nav(harness)).toBe('Dashboard');
      await harness.navigateByUrl('/schools');
      expect(TestBed.inject(Router).url).toBe('/dashboard');
    });

    it('a user with no roles at all still gets the Dashboard but nothing else', async () => {
      seedSession({ user: makeUser({ roles: [] }) });
      const { harness, url } = await go('/masters/role');
      expect(url).toBe('/dashboard');
      expect(heading(harness)).toBe('Dashboard');
    });

    it('signed-out users still go to /login (not the dashboard)', async () => {
      clearSessions();
      expect((await go('/schools')).url).toBe('/login?returnUrl=%2Fschools');
    });
  });
});
