import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { StudentDashboard } from '../../models/student-dashboard.model';
import { StudentDashboardComponent } from './student-dashboard.component';

const URL = `${environment.apiUrl}/api/StudentDashboard`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const dashboard = (overrides: Partial<StudentDashboard> = {}): StudentDashboard => ({
  profile: { studentId: 'DPS001ST001', fullName: 'Asha Verma', admissionNumber: 'ADMNO20260001', className: 'Class 5', sectionName: 'A', rollNumber: '7', profilePhotoUrl: null },
  attendance: { presentCount: 18, absentCount: 2, totalMarkedDays: 20, presentPercentage: 90, from: '2026-04-01', to: '2026-10-27' },
  eventsCount: 4,
  unreadNoticesCount: 2,
  today: '2026-10-27',
  todayClasses: [
    { periodNumber: 1, subjectName: 'Maths', teacherName: 'Rita Rao', roomNo: '12', startTime: '09:00:00', endTime: '09:45:00', status: 'Completed' },
    { periodNumber: 2, subjectName: 'English', teacherName: 'Sam Roy', roomNo: null, startTime: '09:45:00', endTime: '10:30:00', status: 'InProgress' },
    { periodNumber: 3, subjectName: 'Science', teacherName: 'Lia Das', roomNo: null, startTime: '10:45:00', endTime: '11:30:00', status: 'Upcoming' },
  ],
  recentNotices: [{ noticeId: 1, title: 'Sports day', body: 'Bring your kit.', audience: 'ALL', createdBy: 1, createdByName: 'Principal', publishedAt: '2026-10-20T04:00:00Z', updatedAt: '2026-10-20T04:00:00Z', isRead: false, statusName: 'Active' }],
  upcomingEvents: [{ eventId: 9, academicYearId: 1, eventTitle: 'Diwali break', startDate: '2026-11-08', endDate: '2026-11-12', isAllDay: true, eventCategoryId: 1, categoryName: 'Holiday', categoryCode: 'HOLIDAY', colorHex: '#22c55e' }],
  recentResults: [{ assessmentId: 11, title: 'Fractions', assessmentTypeName: 'Class Test', subjectName: 'Maths', testDate: '2026-10-10', maxMarks: 20, obtainedMarks: 17.5, isAbsent: false }],
  recentLeave: [
    { leaveRequestId: 5, leaveTypeId: 1, leaveTypeName: 'Sick Leave', fromDate: '2026-10-05', toDate: '2026-10-06', days: 2, reason: 'Fever', status: 'Approved', appliedBy: 3, appliedAt: '2026-10-04T05:00:00Z', studentId: 'DPS001ST001', studentName: 'Asha Verma' },
  ],
  ...overrides,
});

describe('StudentDashboardComponent', () => {
  let fixture: ComponentFixture<StudentDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';

  function open(flush: (req: TestRequest) => void): void {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentDashboardComponent);
    fixture.detectChanges();
    flush(http.expectOne(URL));
    fixture.detectChanges();
  }
  const openWith = (d: StudentDashboard) => open((req) => req.flush(env(d)));

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('greets the student by first name, not with the teacher copy from the reference design', () => {
    openWith(dashboard());
    expect(text()).toContain('Welcome back, Asha');
    expect(text()).not.toContain('Manage courses');
  });

  it('shows the profile card with class, roll number and no Edit Profile button', () => {
    openWith(dashboard());
    expect(text()).toContain('Asha Verma');
    expect(text()).toContain('Class 5 – A');
    expect(text()).toContain('7');
    expect(text().toLowerCase()).not.toContain('edit profile');
    expect(el().querySelectorAll('button')).toHaveLength(0);
  });

  it('shows the three stat cards: events, unread notices, attendance %', () => {
    openWith(dashboard());
    const stats = [...el().querySelectorAll('.stat')].map((s) => `${s.querySelector('.stat-value')?.textContent} ${s.querySelector('.stat-label')?.textContent}`);
    expect(stats).toEqual(['4 Events this year', '2 Unread notices', '90% Attendance']);
  });

  it('draws the attendance donut with Present and Absent only, and the four numbers underneath', () => {
    openWith(dashboard());
    expect(el().querySelectorAll('circle.slice')).toHaveLength(2);
    const numbers = [...el().querySelectorAll('.numbers div')].map((d) => `${d.querySelector('dt')?.textContent} ${d.querySelector('dd')?.textContent}`);
    expect(numbers).toEqual(['Present 18', 'Absent 2', 'Total days 20', 'Percentage 90%']);
    expect(text()).not.toMatch(/half day|late/i);
  });

  it('says so, and draws no slices, when nothing is marked yet', () => {
    openWith(dashboard({ attendance: { presentCount: 0, absentCount: 0, totalMarkedDays: 0, presentPercentage: null, from: '2026-04-01', to: '2026-10-27' } }));
    expect(el().querySelectorAll('circle.slice')).toHaveLength(0);
    expect(text()).toContain('No attendance has been marked yet');
  });

  it('lists today\'s classes with a green Completed, amber In Progress and gray Upcoming pill', () => {
    openWith(dashboard());
    const pills = [...el().querySelectorAll('[aria-label="Today\'s classes"] .pill')];
    expect(pills.map((p) => p.textContent?.trim())).toEqual(['Completed', 'In Progress', 'Upcoming']);
    expect(pills.map((p) => ['green', 'amber', 'gray'].find((c) => p.classList.contains(c)))).toEqual(['green', 'amber', 'gray']);
    expect(text()).toContain('9:00 AM – 9:45 AM');
    expect(text()).toContain('Maths');
  });

  it('shows test results with ID, Test Name, Subject, Type, Date and Marks only: no grade / CGPA / status', () => {
    openWith(dashboard());
    const headers = [...el().querySelectorAll('th')].map((h) => h.textContent?.trim());
    expect(headers).toEqual(['ID', 'Test Name', 'Subject', 'Type', 'Date', 'Marks']);
    expect(text()).toContain('17.5 / 20');
    expect(text()).not.toMatch(/grade|cgpa|pass|fail/i);
  });

  it('shows leave status read-only, with no Apply button', () => {
    openWith(dashboard());
    expect(text()).toContain('Sick Leave');
    expect(text()).toContain('05 Oct 2026 – 06 Oct 2026');
    expect(el().querySelector('[aria-label="Leave requests"] .pill')?.textContent?.trim()).toBe('Approved');
    expect(text().toLowerCase()).not.toContain('apply');
  });

  it('shows the notice board (linking to the full list) and the upcoming events (linking to the calendar)', () => {
    openWith(dashboard());
    expect(text()).toContain('Sports day');
    expect(text()).toContain('Diwali break');
    const links = [...el().querySelectorAll('a')].map((a) => a.getAttribute('href'));
    expect(links).toContain('/notices/my');
    expect(links).toContain('/calendar');
  });

  it('shows friendly empty states when there is nothing to list', () => {
    openWith(dashboard({ todayClasses: [], recentNotices: [], upcomingEvents: [], recentResults: [], recentLeave: [], attendance: null }));
    expect(text()).toContain('No classes scheduled for today');
    expect(text()).toContain('No notices for you');
    expect(text()).toContain('No upcoming events');
    expect(text()).toContain('No test results yet');
    expect(text()).toContain('No leave requests');
    expect(text()).toContain('Attendance could not be loaded');
  });

  it('shows an error note when the dashboard cannot be loaded', () => {
    open((req) => req.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' }));
    expect(text()).toContain('could not be loaded');
  });
});
