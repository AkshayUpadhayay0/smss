import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Meta } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ParentSetPasswordComponent } from './set-password.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const TOKEN = 'Qm9X3kP0aT7vL2nR8sY4cE6wH1dJ5uZfG9bV-qA_MtI';

describe('ParentSetPasswordComponent', () => {
  let fixture: ComponentFixture<ParentSetPasswordComponent>;
  let http: HttpTestingController;
  let replaceState: ReturnType<typeof vi.fn>;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(query: Record<string, string>, path = 'parent/set-password') {
    replaceState = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query), routeConfig: { path } } } },
        { provide: Location, useValue: { path: () => '/' + path + (query['t'] ? '?t=' + query['t'] : ''), replaceState } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ParentSetPasswordComponent);
    fixture.detectChanges();
  }
  afterEach(() => http.verify());

  it('removes the token from the address bar at once, asks for no referrer, and checks the link with the token in the BODY', () => {
    open({ t: TOKEN });
    expect(replaceState).toHaveBeenCalledWith('/parent/set-password');
    expect(TestBed.inject(Meta).getTag('name="referrer"')?.getAttribute('content')).toBe('no-referrer');
    const req = http.expectOne(`${API}/api/parent/auth/check-link`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ token: TOKEN });
    expect(req.request.url).not.toContain(TOKEN);
    req.flush(env({ valid: true }));
    fixture.detectChanges();
    expect(el().querySelector('form')).not.toBeNull();
    expect(el().innerHTML).not.toContain(TOKEN);   // never rendered
  });

  it('a missing link shows one message and no form, and asks the server nothing', () => {
    open({});
    http.expectNone(() => true);
    expect(el().querySelector('[data-state="invalid"]')?.textContent).toContain('not valid any more');
    expect(el().querySelector('form')).toBeNull();
  });

  it('an unusable link from the server shows the same message', () => {
    open({ t: TOKEN });
    http.expectOne(`${API}/api/parent/auth/check-link`).flush(env({ valid: false }));
    fixture.detectChanges();
    expect(el().querySelector('[data-state="invalid"]')).not.toBeNull();
    expect(el().querySelector('form')).toBeNull();
  });

  it('refuses a short password or two different ones without asking the server, then posts the token and password in the body and shows the login id', () => {
    open({ t: TOKEN });
    http.expectOne(`${API}/api/parent/auth/check-link`).flush(env({ valid: true }));
    fixture.detectChanges();
    cmp()['form'].setValue({ password: 'short', confirm: 'short' }); cmp()['submit'](); fixture.detectChanges();
    cmp()['form'].setValue({ password: 'Sunny-Day-2026', confirm: 'Sunny-Day-2027' }); cmp()['submit'](); fixture.detectChanges();
    http.expectNone(`${API}/api/parent/auth/set-password`);
    expect(text()).toContain('The two passwords are not the same');
    cmp()['form'].setValue({ password: 'Sunny-Day-2026', confirm: 'Sunny-Day-2026' }); cmp()['submit'](); fixture.detectChanges();
    const req = http.expectOne(`${API}/api/parent/auth/set-password`);
    expect(req.request.body).toEqual({ token: TOKEN, newPassword: 'Sunny-Day-2026' });
    req.flush(env({ loginId: 'GHS001GD001' }));
    fixture.detectChanges();
    expect(el().querySelector('[data-field="login-id"]')?.textContent).toBe('GHS001GD001');
    expect(el().querySelector('[data-link="login"]')).not.toBeNull();
    expect(el().innerHTML).not.toContain('Sunny-Day-2026');
  });

  it('a link used by someone else meanwhile ends on the same invalid message', () => {
    open({ t: TOKEN });
    http.expectOne(`${API}/api/parent/auth/check-link`).flush(env({ valid: true }));
    fixture.detectChanges();
    cmp()['form'].setValue({ password: 'Sunny-Day-2026', confirm: 'Sunny-Day-2026' }); cmp()['submit']();
    http.expectOne(`${API}/api/parent/auth/set-password`).flush({ status: false, statusCode: 400, message: 'This link is not valid any more. Ask the school for a new one.', data: null }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(el().querySelector('[data-state="invalid"]')).not.toBeNull();
  });

  it('the reset address words it as a new password', () => {
    open({ t: TOKEN }, 'parent/reset-password');
    http.expectOne(`${API}/api/parent/auth/check-link`).flush(env({ valid: true }));
    fixture.detectChanges();
    expect(text()).toContain('Choose a new password');
  });
});
