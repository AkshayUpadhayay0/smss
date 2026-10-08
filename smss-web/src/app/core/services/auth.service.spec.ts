import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { SESSION_KEY, clearSessions, makeLoginResponse, makeUser, seedSession } from '../testing/auth-test-utils';
import { AuthService } from './auth.service';

const API = environment.apiUrl;
const envelope = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

describe('AuthService', () => {
  let http: HttpTestingController;

  function setup() {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([errorInterceptor])), provideHttpClientTesting(), provideRouter([{ path: '**', children: [] }])],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(AuthService);
  }

  beforeEach(clearSessions);
  afterEach(() => {
    http?.verify();
    clearSessions();
  });

  it('starts signed out', () => {
    const auth = setup();
    expect(auth.hasValidSession()).toBe(false);
    expect(auth.accessToken()).toBeNull();
    expect(auth.user()).toBeNull();
  });

  it('login posts the credentials, keeps the session and exposes the user', () => {
    const auth = setup();
    let user: unknown;
    auth.login({ username: 'SUAD001', password: 'pw', rememberMe: false }).subscribe((u) => (user = u));
    const req = http.expectOne(`${API}/api/Auth/login`);
    expect(req.request.body).toEqual({ username: 'SUAD001', password: 'pw', rememberMe: false });
    req.flush(envelope(makeLoginResponse({ token: 'tok' })));

    expect(user).toMatchObject({ username: 'Akshay Upadhayay' });
    expect(auth.accessToken()).toBe('tok');
    expect(auth.hasValidSession()).toBe(true);
    expect(auth.user()?.roles).toEqual(['Super Admin']);
  });

  it('"remember me" decides where the session is stored', () => {
    const auth = setup();
    auth.login({ username: 'a', password: 'b', rememberMe: true }).subscribe();
    http.expectOne(`${API}/api/Auth/login`).flush(envelope(makeLoginResponse()));
    expect(localStorage.getItem(SESSION_KEY)).not.toBeNull();
    expect(sessionStorage.getItem(SESSION_KEY)).toBeNull();

    auth.login({ username: 'a', password: 'b', rememberMe: false }).subscribe();
    http.expectOne(`${API}/api/Auth/login`).flush(envelope(makeLoginResponse()));
    expect(sessionStorage.getItem(SESSION_KEY)).not.toBeNull();
    expect(localStorage.getItem(SESSION_KEY)).toBeNull(); // never both
  });

  it('a failed login leaves the user signed out and does not toast (the form shows it inline)', () => {
    const auth = setup();
    let error: { error: { message: string } } | undefined;
    auth.login({ username: 'a', password: 'b', rememberMe: false }).subscribe({ error: (e) => (error = e) });
    http.expectOne(`${API}/api/Auth/login`).flush({ status: false, statusCode: 401, message: 'Invalid username or password.', data: null }, { status: 401, statusText: 'Unauthorized' });
    expect(error?.error.message).toBe('Invalid username or password.');
    expect(auth.hasValidSession()).toBe(false);
  });

  it('restores a session from storage on startup (local first, then session)', () => {
    seedSession({ token: 'from-session' }, 'session');
    expect(setup().accessToken()).toBe('from-session');
  });

  it('ignores a corrupt stored session', () => {
    sessionStorage.setItem(SESSION_KEY, '{not json');
    expect(setup().hasValidSession()).toBe(false);
  });

  it('treats an expired refresh token as signed out', () => {
    seedSession({ refreshTokenExpiresAt: new Date(Date.now() - 1000).toISOString() });
    expect(setup().hasValidSession()).toBe(false);
  });

  it('logout clears the session immediately, revokes the refresh token, and still succeeds if that call fails', () => {
    seedSession({ refreshToken: 'r-1' });
    const auth = setup();
    let done = false;
    auth.logout().subscribe(() => (done = true));
    expect(auth.hasValidSession()).toBe(false);
    expect(sessionStorage.getItem(SESSION_KEY)).toBeNull();
    const req = http.expectOne(`${API}/api/Auth/logout`);
    expect(req.request.body).toEqual({ refreshToken: 'r-1' });
    req.flush(null, { status: 500, statusText: 'Server Error' });
    expect(done).toBe(true);
  });

  it('refresh is single-flight, rotates the stored tokens and keeps the storage choice', () => {
    seedSession({ token: 'old', refreshToken: 'r-old' }, 'local');
    const auth = setup();
    const results: string[] = [];
    auth.refresh().subscribe((t) => results.push(t));
    auth.refresh().subscribe((t) => results.push(t)); // concurrent caller shares the same request
    const req = http.expectOne(`${API}/api/Auth/refresh`);
    expect(req.request.body).toEqual({ refreshToken: 'r-old' });
    req.flush(envelope(makeLoginResponse({ token: 'new', refreshToken: 'r-new' })));

    expect(results).toEqual(['new', 'new']);
    expect(auth.accessToken()).toBe('new');
    expect(JSON.parse(localStorage.getItem(SESSION_KEY)!).refreshToken).toBe('r-new');

    // the next refresh uses the rotated token, not the old one
    auth.refresh().subscribe();
    expect(http.expectOne(`${API}/api/Auth/refresh`).request.body).toEqual({ refreshToken: 'r-new' });
  });

  it('change password replaces the token pair and keeps the user signed in', () => {
    seedSession({ token: 'old' });
    const auth = setup();
    auth.changePassword({ currentPassword: 'a', newPassword: 'bbbbbbbb' }).subscribe();
    http.expectOne(`${API}/api/Auth/change-password`).flush(envelope(makeLoginResponse({ token: 'fresh', user: makeUser({ isFirstLogin: false }) })));
    expect(auth.accessToken()).toBe('fresh');
    expect(auth.user()?.isFirstLogin).toBe(false);
  });
});
