import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { clearSessions, seedSession } from './core/testing/auth-test-utils';

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

  async function go(from: string) {
    const harness = await RouterTestingHarness.create();
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
});
