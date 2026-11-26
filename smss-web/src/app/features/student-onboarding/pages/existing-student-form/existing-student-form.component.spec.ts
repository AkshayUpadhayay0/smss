import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { ExistingStudentFormComponent } from './existing-student-form.component';

const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const TYPES = [
  { admissionTypeId: 7, typeCode: 'NEW_ADMISSION', typeName: 'New Admission' },
  { admissionTypeId: 8, typeCode: 'EXISTING_STUDENT', typeName: 'Existing Student' },
];

describe('ExistingStudentFormComponent admission type', () => {
  let fixture: ComponentFixture<ExistingStudentFormComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  function open(types: unknown[]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ExistingStudentFormComponent);
    fixture.detectChanges();
    for (let round = 0; round < 6; round++) {
      const pending = http.match(() => true);
      if (!pending.length) break;
      for (const req of pending) req.flush(env(req.request.url === `${environment.apiUrl}/api/AdmissionTypes` ? types : []));
      fixture.detectChanges();
    }
  }
  afterEach(() => {
    http?.verify();
    clearSessions();
  });

  const typeField = () => [...el().querySelectorAll('app-input')].find((i) => i.textContent?.includes('Admission type'))!.querySelector('input') as HTMLInputElement;

  it('shows "Existing Student" as the admission type, read-only, and no banner', () => {
    open(TYPES);
    expect(typeField().value).toBe('Existing Student');
    expect(typeField().disabled).toBe(true);
    expect(el().querySelector('.banner')).toBeNull();
  });

  it('the type is found by its CODE: a list without EXISTING_STUDENT (even one named "Existing Student") shows the contact-support banner', () => {
    open([{ admissionTypeId: 9, typeCode: 'SOMETHING_ELSE', typeName: 'Existing Student' }]);
    expect(el().querySelector('.banner')?.textContent).toContain('contact support');
  });
});

describe('ExistingStudentFormComponent charge-fees-from', () => {
  let fixture: ComponentFixture<ExistingStudentFormComponent>;
  let http: HttpTestingController;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const todayIso = () => new Date().toISOString().slice(0, 10);
  const VALID = {
    admissionDate: '2025-06-01', academicYearId: '1', classSectionId: '5', firstName: 'Asha', lastName: 'Rao', dateOfBirth: '2015-01-02',
    guardianName: 'Mr Rao', guardianRelationship: 'Father', guardianMobileNumber: '9876543210', guardianEmail: 'g@example.com', guardianWhatsAppSameAsMobile: true, guardianConsent: true,
  };

  function open() {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ExistingStudentFormComponent);
    fixture.detectChanges();
    for (let round = 0; round < 6; round++) {
      const pending = http.match(() => true);
      if (!pending.length) break;
      for (const req of pending) req.flush(env(req.request.url === `${environment.apiUrl}/api/AdmissionTypes` ? TYPES : []));
      fixture.detectChanges();
    }
  }
  afterEach(() => {
    http?.verify();
    clearSessions();
  });

  const post = () => http.match((r) => r.method === 'POST' && r.url.endsWith('/existing-student'));

  it('"Charge fees from" starts at today, separately from the (historical) admission date', () => {
    open();
    expect(cmp()['form'].controls.chargeFrom.value).toBe(todayIso());
    cmp()['form'].patchValue(VALID);
    expect(cmp()['form'].controls.chargeFrom.value).toBe(todayIso());
  });

  it('the last name is optional: only the first name is mandatory, and a blank last name is sent as null', () => {
    open();
    cmp()['form'].patchValue({ ...VALID, lastName: '' });
    expect(cmp()['form'].controls.lastName.valid).toBe(true);
    cmp()['submit']();
    const [req] = post();
    expect(req.request.body.lastName).toBeNull();
    cmp()['form'].patchValue({ firstName: '' });
    expect(cmp()['form'].controls.firstName.valid).toBe(false);   // the first name still is
  });

  it('a normal save sends today as chargeFrom with no back-dating confirmation', () => {
    open();
    cmp()['form'].patchValue(VALID);
    cmp()['submit']();
    const [req] = post();
    expect(req.request.body).toMatchObject({ chargeFrom: todayIso(), confirmBackdating: false, backdatingReason: null });
  });

  it('an earlier date needs a reason, then a confirmation, and is sent as confirmed with the reason', async () => {
    open();
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    cmp()['form'].patchValue({ ...VALID, chargeFrom: '2026-09-01' });
    cmp()['onBackdating']({ isBackdated: true, allowed: true, message: null, count: 3, amount: 1800 });

    cmp()['submit']();                                  // no reason yet
    expect(post()).toHaveLength(0);
    expect(confirm).not.toHaveBeenCalled();

    cmp()['form'].patchValue({ backdatingReason: ' Joined in September ' });
    cmp()['submit']();
    await Promise.resolve();
    await Promise.resolve();
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(confirm.mock.calls[0][0].message).toContain('3 due(s)');
    const [req] = post();
    expect(req.request.body).toMatchObject({ chargeFrom: '2026-09-01', confirmBackdating: true, backdatingReason: 'Joined in September' });
  });

  it('declining the confirmation saves nothing', async () => {
    open();
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    cmp()['form'].patchValue({ ...VALID, chargeFrom: '2026-09-01', backdatingReason: 'x' });
    cmp()['onBackdating']({ isBackdated: true, allowed: true, message: null, count: 1, amount: 100 });
    cmp()['submit']();
    await Promise.resolve();
    await Promise.resolve();
    expect(post()).toHaveLength(0);
  });

  it('a back-dated charge the server says is not possible is not sent', () => {
    open();
    cmp()['form'].patchValue({ ...VALID, chargeFrom: '2025-09-01', backdatingReason: 'x' });
    cmp()['onBackdating']({ isBackdated: true, allowed: false, message: 'Earlier dates are only possible in the current academic year.', count: 0, amount: 0 });
    cmp()['submit']();
    expect(post()).toHaveLength(0);
  });
});
