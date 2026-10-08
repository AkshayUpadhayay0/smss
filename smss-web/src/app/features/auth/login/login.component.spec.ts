import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { errorInterceptor } from '../../../core/interceptors/error.interceptor';
import { ToastService } from '../../../core/services/toast.service';
import { SESSION_KEY, clearSessions, makeLoginResponse, makeUser } from '../../../core/testing/auth-test-utils';
import { LoginComponent, safeReturnUrl } from './login.component';

const LOGIN = `${environment.apiUrl}/api/Auth/login`;

describe('safeReturnUrl', () => {
  it('only allows in-app absolute paths', () => {
    expect(safeReturnUrl('/schools/add')).toBe('/schools/add');
    expect(safeReturnUrl('/schools?x=1')).toBe('/schools?x=1');
    for (const bad of [undefined, '', 'schools', '//evil.com', 'https://evil.com', 'javascript:alert(1)', '/login', '/login?returnUrl=/x']) {
      expect(safeReturnUrl(bad)).toBeNull();
    }
  });
});

describe('LoginComponent', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<LoginComponent>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;

  const el = () => fixture.nativeElement as HTMLElement;
  const type = (selector: string, value: string) => {
    const input = el().querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  };
  const submit = () => {
    el().querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    fixture.detectChanges();
  };
  function fill(username = 'SUAD001', password = 'secret') {
    type('input[type="text"]', username);
    type('input[type="password"]', password);
  }

  function create(returnUrl?: string) {
    fixture = TestBed.createComponent(LoginComponent);
    if (returnUrl) fixture.componentRef.setInput('returnUrl', returnUrl);
    fixture.detectChanges();
  }

  beforeEach(() => {
    clearSessions();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([errorInterceptor])), provideHttpClientTesting(), provideRouter([{ path: '**', children: [] }])],
    });
    http = TestBed.inject(HttpTestingController);
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('does not call the API with empty fields and shows what is missing', () => {
    create();
    submit();
    http.expectNone(LOGIN);
    expect(el().textContent).toContain('Enter your username');
    expect(el().textContent).toContain('Enter your password');
  });

  it('signs in, sends the trimmed username and goes to the dashboard', () => {
    create();
    fill('  SUAD001 ', 'secret');
    submit();
    const req = http.expectOne(LOGIN);
    expect(req.request.body).toEqual({ username: 'SUAD001', password: 'secret', rememberMe: false });
    req.flush({ status: true, statusCode: 200, message: 'ok', data: makeLoginResponse() });
    expect(navigateByUrl).toHaveBeenCalledWith('/dashboard');
    expect(sessionStorage.getItem(SESSION_KEY)).not.toBeNull();
  });

  it('"keep me signed in" is sent as rememberMe and persists the session in localStorage', () => {
    create();
    fill();
    const box = el().querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    box.click();
    submit();
    const req = http.expectOne(LOGIN);
    expect(req.request.body.rememberMe).toBe(true);
    req.flush({ status: true, statusCode: 200, message: 'ok', data: makeLoginResponse() });
    expect(localStorage.getItem(SESSION_KEY)).not.toBeNull();
  });

  it('returns to the page the user was heading to', () => {
    create('/schools/add');
    fill();
    submit();
    http.expectOne(LOGIN).flush({ status: true, statusCode: 200, message: 'ok', data: makeLoginResponse() });
    expect(navigateByUrl).toHaveBeenCalledWith('/schools/add');
  });

  it('ignores an off-site returnUrl', () => {
    create('//evil.com/phish');
    fill();
    submit();
    http.expectOne(LOGIN).flush({ status: true, statusCode: 200, message: 'ok', data: makeLoginResponse() });
    expect(navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('sends a first-time user (temporary password) to change their password first', () => {
    create('/schools');
    fill();
    submit();
    http
      .expectOne(LOGIN)
      .flush({ status: true, statusCode: 200, message: 'ok', data: makeLoginResponse({ user: makeUser({ isFirstLogin: true }) }) });
    expect(navigateByUrl).toHaveBeenCalledWith('/change-password');
  });

  it('shows the server message inline on bad credentials, clears the password and does not toast', () => {
    const toast = TestBed.inject(ToastService);
    create();
    fill();
    submit();
    http
      .expectOne(LOGIN)
      .flush({ status: false, statusCode: 401, message: 'Invalid username or password.', data: null }, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    expect(el().querySelector('[role="alert"]')?.textContent).toContain('Invalid username or password.');
    expect(el().querySelector<HTMLInputElement>('input[type="password"]')!.value).toBe('');
    expect(toast.toasts()).toHaveLength(0);
    expect(navigateByUrl).not.toHaveBeenCalled();
    expect(el().querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(false); // can retry
  });

  it('says so when the server cannot be reached', () => {
    create();
    fill();
    submit();
    http.expectOne(LOGIN).error(new ProgressEvent('error'), { status: 0 });
    fixture.detectChanges();
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('Cannot reach the server');
  });

  it('disables the button while signing in so it cannot be double-submitted', () => {
    create();
    fill();
    submit();
    expect(el().querySelector<HTMLButtonElement>('button[type="submit"]')!.disabled).toBe(true);
    submit();
    http.expectOne(LOGIN); // exactly one request
  });
});
