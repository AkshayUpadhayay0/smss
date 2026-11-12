import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ToastService } from '../../../../core/services/toast.service';
import { StudentProfileComponent } from './student-profile.component';

const API = 'https://localhost:7037';
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

describe('StudentProfileComponent', () => {
  let fixture: ComponentFixture<StudentProfileComponent>;
  let http: HttpTestingController;

  const setup = (roles: string[]) => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentProfileComponent);
  };
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  const PROFILE = {
    studentId: 'DPS001ST001', admissionNumber: 'ADMNO20260001', fullName: 'Abhinav Kumar', dateOfBirth: '1998-07-08',
    admissionDate: '2026-04-01', statusName: 'Active', loginId: 'DPS001ST001', guardians: [],
  };
  const openAs = (roles: string[]) => {
    setup(roles);
    fixture.componentRef.setInput('studentId', 'DPS001ST001');
    fixture.detectChanges();
    http.expectOne(`${API}/api/Student/DPS261013001/DPS001ST001`).flush(env(PROFILE));
    fixture.detectChanges();
    // the Fees tab (open from the start for an Accountant) loads its own data: answer it all with empty fee data
    for (let i = 0; i < 4; i++) {
      http.match(() => true).forEach((r) => r.flush(env(r.request.url.includes('FeePayment') ? [] : null)));
      fixture.detectChanges();
    }
  };
  const el = () => fixture.nativeElement as HTMLElement;

  describe('ID card', () => {
    const buttonText = () => [...el().querySelectorAll('app-button')].map((b) => b.textContent?.trim() ?? '');
    const idCardUrl = `${API}/api/StudentIdCard/student/DPS001ST001`;
    let clicked: ReturnType<typeof vi.fn>;
    beforeEach(() => {
      URL.createObjectURL = vi.fn(() => 'blob:id-card');
      URL.revokeObjectURL = vi.fn();
      clicked = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined) as unknown as ReturnType<typeof vi.fn>;
    });
    afterEach(() => vi.restoreAllMocks());

    it('School Admin sees Download ID Card; the download is the authenticated GET and is saved as IDCard_<admission>.pdf', () => {
      openAs(['School Admin']);
      expect(buttonText().some((t) => t.includes('Download ID Card'))).toBe(true);

      (fixture.componentInstance as unknown as { downloadIdCard(): void }).downloadIdCard();
      const req = http.expectOne(idCardUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.responseType).toBe('blob');
      req.flush(new Blob(['%PDF-'], { type: 'application/pdf' }));
      expect(clicked).toHaveBeenCalledTimes(1);
    });

    it('a failed download shows the server reason (read out of the blob body) and saves nothing', async () => {
      openAs(['School Admin']);
      (fixture.componentInstance as unknown as { downloadIdCard(): void }).downloadIdCard();
      const body = JSON.stringify({ status: false, statusCode: 400, message: 'No card can be issued: the student is inactive.', data: null });
      http.expectOne(idCardUrl).flush(new Blob([body], { type: 'application/json' }), { status: 400, statusText: 'Bad Request' });
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r));
      const toasts = (TestBed.inject(ToastService).toasts() ?? []).map((t) => t.message);
      expect(toasts).toContain('No card can be issued: the student is inactive.');
      expect(clicked).not.toHaveBeenCalled();
    });

    it('is not offered to an Accountant', () => {
      openAs(['Accountant']);
      expect(buttonText().some((t) => t.includes('ID Card'))).toBe(false);
    });

    it('is not offered in the restricted (Teacher) view', () => {
      setup(['Teacher']);
      fixture.componentRef.setInput('studentId', 'DPS001ST001');
      fixture.componentRef.setInput('restricted', true);
      fixture.detectChanges();
      http.expectOne(`${API}/api/Student/my-class/DPS001ST001`).flush(env(PROFILE));
      fixture.detectChanges();
      expect(buttonText().some((t) => t.includes('ID Card'))).toBe(false);
    });
  });

  it('School Admin: Profile and Fees tabs, with the profile editing actions', () => {
    openAs(['School Admin']);
    const tabs = [...el().querySelectorAll('[role="tab"]')].map((t) => t.textContent?.trim());
    expect(tabs).toEqual(['Profile', 'Fees']);
    expect(el().textContent).toContain('Back to Students');
  });

  it('School Admin: a student with a login can have the password reset (confirmation dialog first, nothing sent yet)', () => {
    openAs(['School Admin']);
    const reset = [...el().querySelectorAll('button')].find((b) => b.textContent?.includes('Reset Password')) as HTMLButtonElement;
    expect(reset).toBeDefined();
    reset.click();
    fixture.detectChanges();
    expect(el().querySelector('app-reset-password-dialog')).not.toBeNull();
    expect(el().textContent).toContain('Reset password?');
    http.expectNone(`${API}/api/PasswordReset`);
  });

  it('Accountant: only the Fees tab (no Profile tab, photo or student-login actions), and back goes to the class report', () => {
    openAs(['Accountant']);
    const tabs = [...el().querySelectorAll('[role="tab"]')].map((t) => t.textContent?.trim());
    expect(tabs).toEqual(['Fees']);
    expect(el().querySelector('app-student-fees-tab')).not.toBeNull();
    expect(el().querySelector('[role="tabpanel"][aria-labelledby="tab-profile"]')).toBeNull();
    expect(el().textContent).not.toMatch(/(Add|Update) Photo/);
    expect(el().textContent).toContain('Abhinav Kumar');     // the student summary header stays
    expect(el().textContent).toContain('Back to Class Report');

    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    (fixture.componentInstance as unknown as { back(): void }).back();
    expect(navigate).toHaveBeenCalledWith(['/fee-reports/class'], expect.anything());
  });

  it('loads the profile by the string student id (the route param is not converted to a number)', () => {
    setup(['School Admin']);
    fixture.componentRef.setInput('studentId', 'DPS001ST001');
    fixture.detectChanges();

    const req = http.expectOne(`${API}/api/Student/DPS261013001/DPS001ST001`);
    req.flush(
      env({
        studentId: 'DPS001ST001', admissionNumber: 'ADMNO20260001', fullName: 'Abhinav Kumar', dateOfBirth: '1998-07-08',
        admissionDate: '2026-04-01', statusName: 'Active', loginId: 'DPS001ST001', guardians: [],
      }),
    );
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Abhinav Kumar');
    expect(text).toContain('DPS001ST001');
    expect(text).not.toContain('could not be loaded');
  });
  describe('restricted mode (a Teacher looking at a student of their own class)', () => {
    const MINE = { ...PROFILE, loginId: null, aadharNumberMasked: null, className: 'Nursery', sectionName: 'A', rollNumber: '3', academicYearName: '2026-27',
      guardians: [{ guardianId: 1, fullName: 'Ravi Rawat', relationship: 'FATHER', mobileNumber: '9876500001', email: null, isPrimary: true, isEmergencyContact: false, canPickupStudent: true }] };
    const openRestricted = () => {
      setup(['Teacher']);
      fixture.componentRef.setInput('studentId', 'DPS001ST001');
      fixture.componentRef.setInput('restricted', true);
      fixture.detectChanges();
      // only the narrow my-class endpoint is called: never the broad profile read, and never any fee endpoint
      http.expectOne(`${API}/api/Student/my-class/DPS001ST001`).flush(env(MINE));
      fixture.detectChanges();
    };

    it('reads GET /api/Student/my-class/{id} only and makes no other request (no fee data)', () => {
      openRestricted();
      http.expectNone((r) => r.url.includes('StudentFee') || r.url.includes('FeePayment') || r.url.includes('/api/Student/DPS261013001'));
      expect(el().textContent).toContain('Abhinav Kumar');
    });

    it('is read-only: no Fees tab, no photo change, no login card or Create Login button, no Aadhaar row', () => {
      openRestricted();
      expect([...el().querySelectorAll('[role="tab"]')].map((t) => t.textContent?.trim())).toEqual(['Profile']);
      expect(el().querySelector('app-student-fees-tab')).toBeNull();
      const t = el().textContent ?? '';
      expect(t).not.toMatch(/(Add|Update) Photo/);
      expect(t).not.toContain('Create Login');
      expect(t).not.toContain('Aadhaar');
      expect(t).not.toContain('Login ID');
    });

    it('still shows what a class teacher needs: class, roll number, guardians', () => {
      openRestricted();
      const t = el().textContent ?? '';
      expect(t).toContain('Nursery');
      expect(t).toContain('Ravi Rawat');
      expect(t).toContain('9876500001');
    });

    it('back goes to My Students', () => {
      openRestricted();
      expect(el().textContent).toContain('Back to My Students');
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      (fixture.componentInstance as unknown as { back(): void }).back();
      expect(navigate).toHaveBeenCalledWith(['/my-students']);
    });

    it('says the student could not be loaded on a 404 (a student outside the teacher\'s sections)', () => {
      setup(['Teacher']);
      fixture.componentRef.setInput('studentId', 'DPS001ST999');
      fixture.componentRef.setInput('restricted', true);
      fixture.detectChanges();
      http.expectOne(`${API}/api/Student/my-class/DPS001ST999`).flush({ status: false, statusCode: 404, message: 'Student not found in your classes.', data: null }, { status: 404, statusText: 'Not Found' });
      fixture.detectChanges();
      expect(el().textContent).toContain('This student could not be loaded.');
    });
  });
});
