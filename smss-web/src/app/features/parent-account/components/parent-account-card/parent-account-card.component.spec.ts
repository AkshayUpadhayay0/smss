import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ParentAccountStatus } from '../../models/parent-account.model';
import { ParentAccountCardComponent } from './parent-account-card.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const status = (over: Partial<ParentAccountStatus> = {}): ParentAccountStatus => ({
  guardianId: 9, guardianName: 'Asha Rao', hasAccount: true, loginId: 'GHS001GD001', passwordSet: false, consentRecorded: true, whatsAppOnFile: true, emailOnFile: true,
  linksInLast24Hours: 1, maxLinksPer24Hours: 4, deliveries: [{ deliveryId: 1, channel: 'WHATSAPP', status: 'FAILED', templateCode: 't', purpose: 'SET_PASSWORD', senderKind: 'CLOUD', errorMessage: 'WhatsApp could not be reached.', createdAt: '2026-11-26T00:00:00Z' }], ...over,
});

describe('ParentAccountCardComponent', () => {
  let fixture: ComponentFixture<ParentAccountCardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');

  function open(s: ParentAccountStatus, canReset = false) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'GHS261103002', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ParentAccountCardComponent);
    fixture.componentRef.setInput('guardianId', 9);
    fixture.componentRef.setInput('canReset', canReset);
    fixture.detectChanges();
    http.expectOne(`${API}/api/ParentAccounts/9`).flush(env(s));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('shows the login id, that no password is chosen yet, and how the last message went in words', () => {
    open(status());
    expect(el().querySelector('[data-field="login-id"]')?.textContent).toBe('GHS001GD001');
    expect(el().querySelector('[data-field="password-state"]')?.textContent).toContain('Has not chosen a password yet');
    expect(el().querySelector('[data-list="deliveries"]')?.textContent).toContain('WhatsApp: failed (WhatsApp could not be reached.)');
    expect(el().querySelector('[data-field="limit"]')?.textContent).toContain('1 of 4');
    expect(el().querySelector('[data-action="reset"]')).toBeNull();
  });

  it('Resend asks the server to send a new link and shows nothing of it', () => {
    open(status());
    (el().querySelector('[data-action="resend"] button') as HTMLButtonElement).click();
    const req = http.expectOne(`${API}/api/ParentAccounts/9/resend`);
    expect(req.request.method).toBe('POST');
    req.flush(env({ outcome: 'RESENT', canResend: true, loginId: 'GHS001GD001', guardianId: 9, deliveries: [{ deliveryId: 2, channel: 'WHATSAPP', status: 'SENT' }] }));
    http.expectOne(`${API}/api/ParentAccounts/9`).flush(env(status({ linksInLast24Hours: 2 })));
    fixture.detectChanges();
    expect(el().querySelector('[data-field="limit"]')?.textContent).toContain('2 of 4');
    expect(el().innerHTML).not.toMatch(/\?t=|https?:\/\//);
  });

  it('Resend is off at the limit and without consent; Send reset link appears only for the School Admin and only after the parent chose a password', () => {
    open(status({ linksInLast24Hours: 4 }));
    expect((el().querySelector('[data-action="resend"] button') as HTMLButtonElement).disabled).toBe(true);
    TestBed.resetTestingModule();
    open(status({ passwordSet: true }), false);
    expect(el().querySelector('[data-action="resend"]')).toBeNull();
    expect(el().querySelector('[data-action="reset"]')).toBeNull();
    TestBed.resetTestingModule();
    open(status({ passwordSet: true }), true);
    expect(el().querySelector('[data-action="reset"]')).not.toBeNull();
  });

  it('with no account it says so and why none can be made', () => {
    open(status({ hasAccount: false, loginId: null, consentRecorded: false, deliveries: [] }));
    expect(text()).toContain('has no parent account yet');
    expect(text()).toContain('No parent consent is on record');
    expect(el().querySelector('[data-action="resend"]')).toBeNull();
  });
});
