import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
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

  it('School Admin: Profile and Fees tabs, with the profile editing actions', () => {
    openAs(['School Admin']);
    const tabs = [...el().querySelectorAll('[role="tab"]')].map((t) => t.textContent?.trim());
    expect(tabs).toEqual(['Profile', 'Fees']);
    expect(el().textContent).toContain('Back to Students');
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
});
