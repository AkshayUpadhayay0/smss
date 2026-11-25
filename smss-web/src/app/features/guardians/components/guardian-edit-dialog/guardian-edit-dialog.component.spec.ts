import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { Guardian } from '../../models/guardian.model';
import { GuardianEditDialogComponent } from './guardian-edit-dialog.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const GUARDIAN = (over: Partial<Guardian> = {}): Guardian => ({
  guardianId: 5, name: 'Asha Rao', relationship: 'MOTHER', mobileNumber: '9876500000', whatsAppNumber: null, email: null, occupation: null, isActive: true,
  linkedStudentCount: 1, students: [{ studentId: 'S1', studentName: 'Child One', isPrimary: true }], contactRequired: true, ...over,
});

describe('GuardianEditDialogComponent', () => {
  let fixture: ComponentFixture<GuardianEditDialogComponent>;
  let http: HttpTestingController;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const text = () => document.body.textContent ?? '';

  function open(g: Guardian) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Admission Staff'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(GuardianEditDialogComponent);
    fixture.componentRef.setInput('guardianId', 5);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Guardians/5`).flush(env(g));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('says "applies to N students" with their names when the guardian is linked to several, and nothing when to one', () => {
    open(GUARDIAN({ linkedStudentCount: 2, students: [{ studentId: 'S1', studentName: 'Child One', isPrimary: true }, { studentId: 'S2', studentName: 'Child Two', isPrimary: false }] }));
    expect(document.querySelector('[data-note="shared"]')!.textContent).toContain('Applies to 2 students: Child One, Child Two');
  });

  it('shows no shared note for a guardian of one student', () => {
    open(GUARDIAN());
    expect(document.querySelector('[data-note="shared"]')).toBeNull();
    expect(text()).toContain('WhatsApp and email are required');
  });

  it('the primary guardian of a student cannot be saved without WhatsApp and email: nothing is sent', () => {
    open(GUARDIAN());
    cmp()['save']();
    expect(cmp()['form'].controls.email.invalid).toBe(true);
    expect(cmp()['form'].controls.whatsAppNumber.invalid).toBe(true);
    http.expectNone(`${API}/api/Guardians/5`);
  });

  it('a guardian who is not required to have them can be saved without; a typed number or email must still be valid', () => {
    open(GUARDIAN({ contactRequired: false }));
    cmp()['save']();
    expect(JSON.stringify(Object.entries(cmp()['form'].controls).filter(([, c]: [string, any]) => c.invalid).map(([k, c]: [string, any]) => [k, c.errors]))).toBe('[]');
    const put = http.expectOne(`${API}/api/Guardians/5`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toMatchObject({ name: 'Asha Rao', whatsAppNumber: null, email: null, whatsAppSameAsMobile: false });
    put.flush(env(GUARDIAN({ contactRequired: false })));
    cmp()['form'].patchValue({ whatsAppNumber: '12ab', email: 'nope' });
    cmp()['save']();
    http.expectNone(`${API}/api/Guardians/5`);
  });

  it('saves with the same-as-mobile tick (no number sent) and the trimmed email, then reports the saved guardian', () => {
    open(GUARDIAN());
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);
    cmp()['form'].patchValue({ whatsAppSameAsMobile: true, email: ' asha@example.com ', occupation: ' Teacher ' });
    cmp()['save']();
    const put = http.expectOne(`${API}/api/Guardians/5`);
    expect(put.request.body).toEqual({ name: 'Asha Rao', mobileNumber: '9876500000', whatsAppNumber: null, whatsAppSameAsMobile: true, email: 'asha@example.com', occupation: 'Teacher' });
    put.flush(env(GUARDIAN({ email: 'asha@example.com', whatsAppNumber: '+919876500000' })));
    expect(saved).toHaveBeenCalledTimes(1);
  });
});
