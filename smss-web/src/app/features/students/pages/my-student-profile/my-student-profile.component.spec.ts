import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MyStudentProfileComponent } from './my-student-profile.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const ME = {
  studentId: 'LAS261103ST001', fullName: 'Aarav Sharma', admissionNumber: 'AD1256589', statusName: 'Active', profilePhotoUrl: null, academicYearName: '2026-27',
  className: 'Class 3', sectionName: 'A', rollNumber: '10', genderName: 'Male', dateOfBirth: '2016-11-10', bloodGroupName: 'O+', studentCategoryName: 'General',
  mobileNumber: null, email: 'aarav@example.com', address: '12 Park Road, Dehradun, Uttarakhand - 248001',
  guardians: [
    { fullName: 'Robert Sharma', relationship: 'Father', mobileNumber: '9876543210', email: 'father@example.com' },
    { fullName: 'Brooklyn Sharma', relationship: 'Mother', mobileNumber: '9876543211', email: null },
  ],
};
const DASH = { attendance: { presentCount: 40, absentCount: 5, totalMarkedDays: 45, presentPercentage: 88.9, from: '2026-06-01', to: '2026-11-04' } };

describe('MyStudentProfileComponent', () => {
  let fixture: ComponentFixture<MyStudentProfileComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';

  function open(respond: (req: ReturnType<HttpTestingController['expectOne']>) => void) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyStudentProfileComponent);
    fixture.detectChanges();
    respond(http.expectOne(`${API}/api/Student/me`));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows the header card, the personal info list and the guardians from GET /api/Student/me', () => {
    open((r) => r.flush(env(ME)));
    expect(text()).toContain('Aarav Sharma');
    expect(text()).toContain('AD1256589');
    expect(el().querySelector('.pill')?.textContent).toContain('Active');
    const labels = [...el().querySelectorAll('.kv dt')].map((d) => d.textContent);
    expect(labels).toEqual(['Class', 'Section', 'Roll No', 'Gender', 'Date Of Birth', 'Category', 'Academic Year', 'Phone Number', 'Email']);
    expect(text()).toContain('10 Nov 2016');
    expect(text()).toContain('Robert Sharma');
    expect(text()).toContain('Mother');
    expect(text()).toContain('12 Park Road, Dehradun');
    expect(text()).toContain('O+');
  });

  it('hides the Category row when the record has none, and never shows an edit or photo-change control', () => {
    open((r) => r.flush(env({ ...ME, studentCategoryName: null })));
    expect([...el().querySelectorAll('.kv dt')].map((d) => d.textContent)).not.toContain('Category');
    expect(el().querySelectorAll('input, textarea, select').length).toBe(0);
    expect([...el().querySelectorAll('button')].map((b) => b.textContent?.trim())).toEqual(['Student Details', 'Attendance', 'Leave', 'Fees']);
  });

  it('does not show Aadhaar or the login id (fees live on their own tab)', () => {
    open((r) => r.flush(env(ME)));
    for (const bad of ['Aadhaar', 'Aadhar', 'Login']) expect(text()).not.toContain(bad);
  });

  it('a failed load shows one calm message', () => {
    open((r) => r.flush({ status: false, statusCode: 404, message: 'nope', data: null }, { status: 404, statusText: 'Not Found' }));
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
  });

  it('the Leave tab is the shared own-leave component and loads only when opened', () => {
    open((r) => r.flush(env(ME)));
    http.expectNone(`${API}/api/StudentLeave/my`);
    expect(el().querySelector('app-student-my-leave')).toBeNull();
    (el().querySelectorAll('button[role="tab"]')[2] as HTMLButtonElement).click();
    fixture.detectChanges();
    http.expectOne(`${API}/api/StudentLeave/my`).flush(env([]));
    http.expectOne((r) => r.url === `${API}/api/LeaveType` && r.params.get('appliesTo') === 'student').flush(env([]));
    fixture.detectChanges();
    expect(el().querySelector('app-student-my-leave')).not.toBeNull();
    expect(text()).toContain('New Leave Request');
  });

  it('the Fees tab is the shared read-only fees component and loads only when opened', () => {
    open((r) => r.flush(env(ME)));
    http.expectNone(`${API}/api/Student/me/fees`);
    expect(el().querySelector('app-student-my-fees')).toBeNull();
    (el().querySelectorAll('button[role="tab"]')[3] as HTMLButtonElement).click();
    fixture.detectChanges();
    http.expectOne(`${API}/api/Student/me/fees`).flush(env({ hasFees: false, overallStatus: 'Not Set', totalDue: 0, totalPaid: 0, balance: 0, overdueAmount: 0, overdueCount: 0, heads: [], dues: [], payments: [] }));
    fixture.detectChanges();
    expect(el().querySelector('app-student-my-fees')).not.toBeNull();
    expect(text()).toContain('No fees have been assigned');
  });

  it('the Attendance tab reads the existing dashboard summary, once', () => {
    open((r) => r.flush(env(ME)));
    (el().querySelectorAll('button[role="tab"]')[1] as HTMLButtonElement).click();
    http.expectOne(`${API}/api/StudentDashboard`).flush(env(DASH));
    fixture.detectChanges();
    expect(text()).toContain('88.9%');
    expect(text()).toContain('45');
    (el().querySelectorAll('button[role="tab"]')[0] as HTMLButtonElement).click();
    (el().querySelectorAll('button[role="tab"]')[1] as HTMLButtonElement).click();
    http.expectNone(`${API}/api/StudentDashboard`);
  });
});
