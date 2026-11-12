import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { ToastService } from '../../../core/services/toast.service';
import { ResetPasswordDialogComponent } from './reset-password-dialog.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const RESULT = { loginId: 'DPS001EM001', displayName: 'Asha Verma', temporaryPassword: 'Ab3$kLm9#xQw' };

describe('ResetPasswordDialogComponent', () => {
  let fixture: ComponentFixture<ResetPasswordDialogComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ResetPasswordDialogComponent);
    fixture.componentRef.setInput('loginId', 'DPS001EM001');
    fixture.componentRef.setInput('displayName', 'Asha Verma');
    fixture.detectChanges();
  });
  afterEach(() => http.verify());

  it('explains what will happen before anything is sent', () => {
    const text = el().textContent ?? '';
    expect(text).toContain('Reset password?');
    expect(text).toContain('Asha Verma');
    expect(text).toContain('signed out');
    expect(text).toContain('must choose a new password');
  });

  it('will not reset without a reason (blank or spaces only)', () => {
    cmp()['submit']();
    cmp()['reason'].setValue('   ');
    cmp()['submit']();
    fixture.detectChanges();
    http.expectNone(`${API}/api/PasswordReset`);
    expect(el().textContent).toContain('A reason is required');
  });

  it('sends the login id and the trimmed reason, then shows the credentials once', () => {
    cmp()['reason'].setValue('  Forgot the password  ');
    cmp()['submit']();
    const req = http.expectOne(`${API}/api/PasswordReset`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ loginId: 'DPS001EM001', reason: 'Forgot the password' });
    req.flush(env(RESULT));
    fixture.detectChanges();
    const text = el().textContent ?? '';
    expect(text).toContain('Copy these credentials now');
    expect(text).toContain('Ab3$kLm9#xQw');
    expect(text).toContain('No email is sent');
  });

  it('Done drops the password from memory and tells the page it is finished', () => {
    let done = 0;
    fixture.componentInstance.done.subscribe(() => done++);
    cmp()['reason'].setValue('Compromised');
    cmp()['submit']();
    http.expectOne(`${API}/api/PasswordReset`).flush(env(RESULT));
    fixture.detectChanges();
    button('Done')!.click();
    fixture.detectChanges();
    expect(done).toBe(1);
    expect(cmp()['result']()).toBeNull();
    expect(el().textContent).not.toContain('Ab3$kLm9#xQw');
  });

  it('the credentials dialog cannot be dismissed by accident (the password cannot be shown again)', () => {
    cmp()['reason'].setValue('Compromised');
    cmp()['submit']();
    http.expectOne(`${API}/api/PasswordReset`).flush(env(RESULT));
    fixture.detectChanges();
    expect(el().querySelector('app-modal')).not.toBeNull();
    expect(fixture.debugElement.query((d) => d.name === 'app-modal').componentInstance.dismissible()).toBe(false);
  });

  it('a refusal from the server shows no credentials and lets the admin close the dialog', () => {
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);
    cmp()['reason'].setValue('Reason');
    cmp()['submit']();
    http.expectOne(`${API}/api/PasswordReset`).flush({ status: false, statusCode: 409, message: 'This login is inactive or blocked', data: null }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(cmp()['result']()).toBeNull();
    expect(cmp()['saving']()).toBe(false);
    button('Cancel')!.click();
    expect(closed).toBe(1);
  });

  it('copies the password and says so', async () => {
    const written: string[] = [];
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: async (v: string) => void written.push(v) }, configurable: true });
    const toast = TestBed.inject(ToastService);
    const success = vi.spyOn(toast, 'success');
    cmp()['reason'].setValue('Reason');
    cmp()['submit']();
    http.expectOne(`${API}/api/PasswordReset`).flush(env(RESULT));
    fixture.detectChanges();
    await cmp()['copy']('Ab3$kLm9#xQw', 'Password');
    expect(written).toEqual(['Ab3$kLm9#xQw']);
    expect(success).toHaveBeenCalledWith('Password copied to clipboard.');
  });
});
