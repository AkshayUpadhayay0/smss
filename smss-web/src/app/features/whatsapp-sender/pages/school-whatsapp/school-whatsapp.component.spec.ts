import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { canAccess } from '../../../../core/config/route-roles';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { SenderStatusModel } from '../../models/whatsapp-sender.model';
import { WhatsAppSenderStatusComponent } from '../../components/whatsapp-sender-status/whatsapp-sender-status.component';
import { SchoolWhatsAppComponent } from './school-whatsapp.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const STATUS = (over: Partial<SenderStatusModel> = {}): SenderStatusModel => ({
  schoolId: 'GHS1', schoolName: 'Green Hills School', whatsAppNumber: null, status: 'NOT_REGISTERED', statusLabel: 'Not registered', sendsFrom: "The platform's WhatsApp number, with the school's name in the message",
  verifiedAt: null, updatedAt: '2026-11-25T10:00:00Z', phoneNumberId: null, updatedByName: 'Super Admin', canEdit: true, ...over,
});

describe('SchoolWhatsAppComponent (Super Admin)', () => {
  let fixture: ComponentFixture<SchoolWhatsAppComponent>;
  let http: HttpTestingController;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(s = STATUS()) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: null, userType: 'Employee', roles: ['Super Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SchoolWhatsAppComponent);
    fixture.componentRef.setInput('schoolId', 'GHS1');
    fixture.detectChanges();
    http.expectOne(`${API}/api/WhatsAppSender/schools/GHS1`).flush(env(s));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows the school\'s current sender and whose number the messages go from', () => {
    open();
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-note="sends-from"]')!.textContent).toContain("platform's WhatsApp number");
    expect(cmp()['form'].getRawValue()).toMatchObject({ status: 'NOT_REGISTERED', whatsAppNumber: '', phoneNumberId: '' });
  });

  it('Pending needs the number and Active needs the phone number id: nothing is sent without them', () => {
    open();
    cmp()['form'].patchValue({ status: 'PENDING', whatsAppNumber: '' });
    cmp()['save']();
    http.expectNone(`${API}/api/WhatsAppSender/schools/GHS1`);
    cmp()['form'].patchValue({ status: 'ACTIVE', whatsAppNumber: '9876543210', phoneNumberId: '' });
    cmp()['save']();
    http.expectNone(`${API}/api/WhatsAppSender/schools/GHS1`);
    cmp()['form'].patchValue({ whatsAppNumber: '12ab' });
    cmp()['save']();
    http.expectNone(`${API}/api/WhatsAppSender/schools/GHS1`);
    cmp()['form'].patchValue({ whatsAppNumber: '9876543210', phoneNumberId: 'abc' });
    cmp()['save']();
    http.expectNone(`${API}/api/WhatsAppSender/schools/GHS1`);
  });

  it('saves the status, the number and the phone number id (trimmed) and shows the saved answer', () => {
    open();
    cmp()['form'].patchValue({ status: 'ACTIVE', whatsAppNumber: ' 9876543210 ', phoneNumberId: ' 1234567890 ' });
    cmp()['save']();
    const put = http.expectOne(`${API}/api/WhatsAppSender/schools/GHS1`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ status: 'ACTIVE', whatsAppNumber: '9876543210', phoneNumberId: '1234567890' });
    expect(Object.keys(put.request.body as object).sort()).toEqual(['phoneNumberId', 'status', 'whatsAppNumber']);   // no token or secret field exists in the request
    put.flush(env(STATUS({ status: 'ACTIVE', statusLabel: 'Active', whatsAppNumber: '+919876543210', phoneNumberId: '1234567890', verifiedAt: '2026-11-25T10:05:00Z' })));
    expect(cmp()['current']().status).toBe('ACTIVE');
  });

  it('only the Super Admin may open the page', () => {
    expect(canAccess('/schools/:schoolId/whatsapp', ['Super Admin'])).toBe(true);
    for (const role of ['School Admin', 'Principal', 'Admission Staff', 'Teacher', 'Accountant', 'Student', 'Guardian']) expect(canAccess('/schools/:schoolId/whatsapp', [role])).toBe(false);
  });
});

describe('WhatsAppSenderStatusComponent (School Admin and Principal)', () => {
  let fixture: ComponentFixture<WhatsAppSenderStatusComponent>;
  let http: HttpTestingController;

  function open(answer: () => void) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(WhatsAppSenderStatusComponent);
    fixture.detectChanges();
    answer();
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows a read-only status line with whose number sends, and no way to change it', () => {
    open(() => http.expectOne(`${API}/api/WhatsAppSender/me`).flush(env(STATUS({ status: 'ACTIVE', statusLabel: 'Active', sendsFrom: "The school's own WhatsApp number", whatsAppNumber: '+919876543210', canEdit: false, phoneNumberId: null }))));
    const line = (fixture.nativeElement as HTMLElement).querySelector('[data-line="whatsapp-sender"]')!;
    expect(line.textContent).toContain('Active');
    expect(line.textContent).toContain("school's own WhatsApp number");
    expect(line.textContent).toContain('+919876543210');
    expect((fixture.nativeElement as HTMLElement).querySelector('input, button, select')).toBeNull();
  });

  it('shows nothing when the status cannot be read (a refusal never breaks the page)', () => {
    open(() => http.expectOne(`${API}/api/WhatsAppSender/me`).flush({ status: false, statusCode: 403, message: 'no', data: null }, { status: 403, statusText: 'Forbidden' }));
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-line="whatsapp-sender"]')).toBeNull();
  });
});
