import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AuthService } from '../services/auth.service';
import { ToastService } from '../services/toast.service';
import { clearSessions, makeLoginResponse, seedSession } from '../testing/auth-test-utils';
import { authInterceptor } from './auth.interceptor';
import { errorInterceptor } from './error.interceptor';

const API = environment.apiUrl;
const ok = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const unauthorized = { status: 401, statusText: 'Unauthorized' };

describe('authInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let auth: AuthService;
  let toast: ToastService;
  let router: Router;

  function setup() {
    TestBed.configureTestingModule({
      // same order as app.config.ts
      providers: [provideHttpClient(withInterceptors([errorInterceptor, authInterceptor])), provideHttpClientTesting(), provideRouter([{ path: '**', children: [] }])],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    auth = TestBed.inject(AuthService);
    toast = TestBed.inject(ToastService);
    router = TestBed.inject(Router);
  }

  beforeEach(clearSessions);
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('adds the bearer token to API calls but not to other hosts or the public auth calls', () => {
    seedSession({ token: 'abc' });
    setup();
    client.get(`${API}/api/SchoolRegistration/schools`).subscribe();
    expect(http.expectOne(`${API}/api/SchoolRegistration/schools`).request.headers.get('Authorization')).toBe('Bearer abc');

    client.get('https://example.com/other').subscribe();
    expect(http.expectOne('https://example.com/other').request.headers.has('Authorization')).toBe(false);

    client.post(`${API}/api/Auth/login`, {}).subscribe({ error: () => undefined });
    expect(http.expectOne(`${API}/api/Auth/login`).request.headers.has('Authorization')).toBe(false);
  });

  it('on 401 refreshes once and retries with the new token — the caller never sees the failure', () => {
    seedSession({ token: 'old', refreshToken: 'r-old' });
    setup();
    let body: unknown;
    client.get(`${API}/x`).subscribe((b) => (body = b));

    http.expectOne(`${API}/x`).flush(null, unauthorized);
    const refresh = http.expectOne(`${API}/api/Auth/refresh`);
    expect(refresh.request.body).toEqual({ refreshToken: 'r-old' });
    refresh.flush(ok(makeLoginResponse({ token: 'new', refreshToken: 'r-new' })));

    const retry = http.expectOne(`${API}/x`);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
    retry.flush(ok('done'));

    expect(body).toEqual(ok('done'));
    expect(toast.toasts()).toHaveLength(0); // a recovered 401 is invisible
    expect(auth.hasValidSession()).toBe(true);
  });

  it('shares ONE refresh between concurrent 401s (refresh tokens are single-use on the server)', () => {
    seedSession({ token: 'old' });
    setup();
    const results: unknown[] = [];
    client.get(`${API}/a`).subscribe((r) => results.push(r));
    client.get(`${API}/b`).subscribe((r) => results.push(r));

    http.expectOne(`${API}/a`).flush(null, unauthorized);
    http.expectOne(`${API}/b`).flush(null, unauthorized);
    http.expectOne(`${API}/api/Auth/refresh`).flush(ok(makeLoginResponse({ token: 'new' })));

    http.expectOne(`${API}/a`).flush(ok('A'));
    http.expectOne(`${API}/b`).flush(ok('B'));
    expect(results).toEqual([ok('A'), ok('B')]);
  });

  it('a request that 401s after another already refreshed just retries with the newer token', () => {
    seedSession({ token: 'old' });
    setup();
    client.get(`${API}/slow`).subscribe();
    const slow = http.expectOne(`${API}/slow`);

    // meanwhile another call refreshed the session
    auth.refresh().subscribe();
    http.expectOne(`${API}/api/Auth/refresh`).flush(ok(makeLoginResponse({ token: 'newer' })));

    slow.flush(null, unauthorized);
    expect(http.expectOne(`${API}/slow`).request.headers.get('Authorization')).toBe('Bearer newer');
    http.expectNone(`${API}/api/Auth/refresh`);
  });

  it('when the refresh fails the session ends, the user goes to /login?returnUrl, and the caller gets the error', async () => {
    seedSession({ token: 'old' });
    setup();
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    let failed = false;
    client.get(`${API}/x`).subscribe({ error: () => (failed = true) });

    http.expectOne(`${API}/x`).flush(null, unauthorized);
    http
      .expectOne(`${API}/api/Auth/refresh`)
      .flush({ status: false, statusCode: 401, message: 'Session expired', data: null }, unauthorized);

    expect(failed).toBe(true);
    expect(auth.hasValidSession()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: expect.any(String) } });
    expect(toast.toasts().at(-1)?.message).toMatch(/session has expired/i);
  });

  it('does not refresh for non-401 errors or when signed out', () => {
    seedSession();
    setup();
    client.get(`${API}/x`).subscribe({ error: () => undefined });
    http.expectOne(`${API}/x`).flush({ status: false, statusCode: 403, message: 'Nope', data: null }, { status: 403, statusText: 'Forbidden' });
    http.expectNone(`${API}/api/Auth/refresh`);
    expect(toast.toasts().at(-1)?.message).toBe('Nope');
  });

  it('a signed-out 401 is passed through without trying to refresh', () => {
    setup();
    client.get(`${API}/x`).subscribe({ error: () => undefined });
    http.expectOne(`${API}/x`).flush(null, unauthorized);
    http.expectNone(`${API}/api/Auth/refresh`);
  });
});
