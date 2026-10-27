import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { TeacherDashboard } from '../../models/teacher-dashboard.model';
import { TeacherDashboardComponent } from './teacher-dashboard.component';

const URL = `${environment.apiUrl}/api/TeacherDashboard`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const dashboard = (o: Partial<TeacherDashboard> = {}): TeacherDashboard => ({
  today: '2026-10-27',
  profile: { employeeId: 'DPS001EM001', fullName: 'Anita Verma', designationName: 'Senior Teacher', departmentName: 'Science', photoUrl: null },
  classTeacherOf: [{ classId: 5, className: 'Class 5', sectionId: 9, sectionName: 'A' }],
  subjectsTaught: ['Maths', 'Physics'],
  totalStudents: 30,
  feePending: 4,
  presentToday: 26,
  absentToday: 3,
  notMarkedYet: false,
  unmarkedSections: 0,
  partsUnavailable: [],
  ...o,
});

describe('TeacherDashboardComponent', () => {
  let fixture: ComponentFixture<TeacherDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const stat = (label: string) => [...el().querySelectorAll('.stat')].find((s) => s.querySelector('.stat-label')?.textContent === label)!;
  const statValue = (label: string) => stat(label).querySelector('.stat-value')?.textContent?.trim();

  function open(flush: (req: TestRequest) => void): void {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TeacherDashboardComponent);
    fixture.detectChanges();
    flush(http.expectOne(URL));
    fixture.detectChanges();
  }
  const openWith = (d: TeacherDashboard) => open((req) => req.flush(env(d)));

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('makes exactly one request and greets the teacher by first name with its own subtitle', () => {
    openWith(dashboard());
    expect(text()).toContain('Welcome back, Anita');
    expect(text()).toContain('Here is your classroom at a glance');
    expect(text()).not.toContain('Manage courses');
  });

  it('shows the profile card: name, designation, department, initials without a photo, and no Edit Profile', () => {
    openWith(dashboard());
    expect(text()).toContain('Anita Verma');
    expect(text()).toContain('Senior Teacher');
    expect(text()).toContain('Science');
    expect(el().querySelector('.avatar')?.textContent?.trim()).toBe('AV');
    expect(text().toLowerCase()).not.toContain('edit profile');
    expect(el().querySelectorAll('button')).toHaveLength(0);
  });

  it('shows the photo when there is one', () => {
    openWith(dashboard({ profile: { ...dashboard().profile, photoUrl: '/uploads/x/employees/E1/photo_1.png' } }));
    expect(el().querySelector('.avatar img')?.getAttribute('src')).toBe(`${environment.apiUrl}/uploads/x/employees/E1/photo_1.png`);
  });

  describe('Class Teacher Of', () => {
    it('lists every section', () => {
      openWith(
        dashboard({
          classTeacherOf: [
            { classId: 5, className: 'Class 5', sectionId: 9, sectionName: 'A' },
            { classId: 6, className: 'Class 6', sectionId: 11, sectionName: 'B' },
          ],
        }),
      );
      expect([...el().querySelectorAll('[aria-label="Sections you are class teacher of"] li')].map((l) => l.textContent?.trim())).toEqual(['Class 5 – A', 'Class 6 – B']);
    });

    it('says so when the teacher is not a class teacher', () => {
      openWith(dashboard({ classTeacherOf: [], totalStudents: null, feePending: null, presentToday: null, absentToday: null }));
      expect(text()).toContain('You are not currently a class teacher.');
      expect(el().querySelector('[aria-label="Sections you are class teacher of"]')).toBeNull();
    });
  });

  describe('Subjects Taught', () => {
    it('lists the subjects', () => {
      openWith(dashboard());
      expect([...el().querySelectorAll('[aria-label="Subjects you teach"] li')].map((l) => l.textContent?.trim())).toEqual(['Maths', 'Physics']);
    });

    it('says "No subjects assigned yet." when there are none, even for a class teacher', () => {
      openWith(dashboard({ subjectsTaught: [] }));
      expect(text()).toContain('No subjects assigned yet.');
      expect(text()).not.toContain('You are not currently a class teacher.');
    });
  });

  describe('summary cards', () => {
    it('shows the four numbers', () => {
      openWith(dashboard());
      expect(el().querySelectorAll('.stat')).toHaveLength(4);
      expect(statValue('Total Students')).toBe('30');
      expect(statValue('Fee Pending')).toBe('4');
      expect(statValue('Present Today')).toBe('26');
      expect(statValue('Absent Today')).toBe('3');
    });

    it('shows "Not applicable", not 0, when the teacher is not a class teacher', () => {
      openWith(dashboard({ classTeacherOf: [], totalStudents: null, feePending: null, presentToday: null, absentToday: null }));
      for (const label of ['Total Students', 'Fee Pending', 'Present Today', 'Absent Today']) expect(statValue(label)).toBe('Not applicable');
    });

    it('shows "Not marked yet today" on Present and Absent only', () => {
      openWith(dashboard({ notMarkedYet: true, presentToday: null, absentToday: null }));
      expect(statValue('Present Today')).toBe('Not marked yet today');
      expect(statValue('Absent Today')).toBe('Not marked yet today');
      expect(statValue('Total Students')).toBe('30');
      expect(statValue('Fee Pending')).toBe('4');
    });

    it('with mixed marking shows the sums and how many sections are unmarked', () => {
      openWith(dashboard({ unmarkedSections: 1 }));
      expect(statValue('Present Today')).toBe('26');
      expect(stat('Present Today').textContent).toContain('1 section is not marked yet today');
    });

    it('shows "Unavailable" for a part that failed, without blanking the page', () => {
      openWith(dashboard({ feePending: null, partsUnavailable: ['feePending'] }));
      expect(statValue('Fee Pending')).toBe('Unavailable');
      expect(statValue('Total Students')).toBe('30');
      expect(text()).toContain('Anita Verma');
    });

    it('shows no money anywhere: a head count only', () => {
      openWith(dashboard());
      expect(text()).not.toMatch(/₹|\bRs\b|amount/i);
    });
  });

  it('says the dashboard could not be loaded when the request fails', () => {
    open((req) => req.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' }));
    expect(text()).toContain('Your dashboard could not be loaded.');
  });
});
