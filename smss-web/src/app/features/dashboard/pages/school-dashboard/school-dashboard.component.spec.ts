import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { ApexOptions } from 'ng-apexcharts';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ChartHostComponent } from '../../components/chart/chart.component';
import { SchoolDashboard } from '../../models/school-dashboard.model';
import { schoolDashboard } from '../../utils/school-dashboard-format.spec';
import { SchoolDashboardComponent } from './school-dashboard.component';

const URL = `${environment.apiUrl}/api/SchoolDashboard/summary`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

/** ApexCharts needs a real browser (SVG measuring), so the specs swap the one component that draws them. */
@Component({ selector: 'app-chart', changeDetection: ChangeDetectionStrategy.OnPush, template: '<span class="chart-stub"></span>' })
class StubChartComponent {
  readonly options = input.required<ApexOptions>();
  readonly label = input('Chart');
}

const full = (): SchoolDashboard =>
  schoolDashboard({
    upcomingEvents: [
      { eventId: 1, academicYearId: 7, eventTitle: 'Annual Day', startDate: '2026-11-10', endDate: '2026-11-10', isAllDay: true, eventCategoryId: 1, categoryName: 'Event', categoryCode: 'EVENT', colorHex: '#7c5cfc' },
      { eventId: 2, academicYearId: 7, eventTitle: 'Book fair', startDate: '2026-11-02', endDate: '2026-11-05', isAllDay: true, eventCategoryId: 1, categoryName: 'Event', categoryCode: 'EVENT', colorHex: '#16a34a', isOngoing: true },
    ],
    recentNotices: [{ noticeId: 9, title: 'Diwali holiday', audience: 'STUDENTS_GUARDIANS', createdByName: 'Admin', publishedAt: '2026-11-01T05:00:00Z' }],
    recentAdmissions: [
      { admissionId: 31, applicationNumber: 'APS26-0031', applicantFullName: 'Asha Verma', appliedClassName: 'Class 1', statusName: 'UNDER_REVIEW', applicationDate: '2026-11-02T00:00:00' },
      { admissionId: 30, applicationNumber: 'APS26-0030', applicantFullName: 'Ravi Rao', appliedClassName: null, statusName: 'APPROVED', applicationDate: '2026-11-01T00:00:00' },
    ],
  });

describe('SchoolDashboardComponent', () => {
  let fixture: ComponentFixture<SchoolDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const card = (id: string) => el().querySelector(`[data-card="${id}"]`)!;
  const alert = (id: string) => el().querySelector(`[data-alert="${id}"]`)!;

  function open(flush: (req: TestRequest) => void): void {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'School Admin', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.overrideComponent(SchoolDashboardComponent, { remove: { imports: [ChartHostComponent] }, add: { imports: [StubChartComponent] } });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SchoolDashboardComponent);
    fixture.detectChanges();
    flush(http.expectOne(URL));
    fixture.detectChanges();
  }
  const openWith = (d: SchoolDashboard) => open((req) => req.flush(env(d)));

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('makes exactly one GET request, with no school id in it', () => {
    openWith(full());
    expect(http.match(() => true)).toHaveLength(0);
  });

  it('shows a loading note and then the dashboard, titled with the academic year', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.overrideComponent(SchoolDashboardComponent, { remove: { imports: [ChartHostComponent] }, add: { imports: [StubChartComponent] } });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SchoolDashboardComponent);
    fixture.detectChanges();
    expect(text()).toContain('Loading your dashboard');
    http.expectOne(URL).flush(env(full()));
    fixture.detectChanges();
    expect(text()).not.toContain('Loading your dashboard');
    expect(text()).toContain('academic year 2026-27');
  });

  it('shows an error with a Try again button that asks again', () => {
    open((req) => req.flush({ status: false, message: 'x' }, { status: 500, statusText: 'err' }));
    expect(text()).toContain('Your dashboard could not be loaded.');
    const retry = [...el().querySelectorAll('button')].find((b) => b.textContent?.includes('Try again'))!;
    retry.click();
    http.expectOne(URL).flush(env(full()));
    fixture.detectChanges();
    expect(el().querySelectorAll('[data-card]')).toHaveLength(6);
  });

  describe('the six cards', () => {
    it('show the numbers and link to the module each one comes from', () => {
      openWith(full());
      const cards = [...el().querySelectorAll('[data-card]')];
      expect(cards.map((c) => c.getAttribute('data-card'))).toEqual(['students', 'employees', 'attendance', 'collected', 'outstanding', 'admissions']);
      expect(cards.map((c) => c.getAttribute('href'))).toEqual(['/students', '/employees', '/attendance/mark', '/fee-dashboard', '/fee-dashboard', '/admissions']);
      expect(card('students').querySelector('strong')?.textContent).toBe('420');
      expect(card('attendance').querySelector('strong')?.textContent).toBe('92%');
      expect(card('collected').querySelector('strong')?.textContent).toBe('₹1,25,000.00');
      expect(card('outstanding').textContent).toContain('₹4,80,000.50');
      expect(card('admissions').querySelector('strong')?.textContent).toBe('6');
    });

    it('a card whose part failed says Unavailable while the others keep their numbers', () => {
      openWith(schoolDashboard({ partsUnavailable: ['fees'], stats: { ...schoolDashboard().stats, feeCollectedThisMonth: null, feeOutstanding: null } }));
      expect(card('collected').textContent).toContain('Unavailable');
      expect(card('outstanding').textContent).toContain('Unavailable');
      expect(card('students').textContent).toContain('420');
    });

    it('a brand-new school shows zeros, not blanks', () => {
      openWith(
        schoolDashboard({
          stats: { totalStudents: 0, totalEmployees: 0, attendanceMarkedToday: 0, attendancePresentToday: 0, attendancePercentToday: null, sectionsMarkedToday: 0, sectionsTotal: 0, feeCollectedThisMonth: 0, feeOutstanding: 0, pendingAdmissions: 0 },
          attendanceTrend: schoolDashboard().attendanceTrend.map((p) => ({ ...p, marked: 0, present: 0, presentPercent: null })),
          feeCollectionTrend: schoolDashboard().feeCollectionTrend.map((m) => ({ ...m, amount: 0 })),
          alerts: { pendingEmployeeLeaves: 0, pendingStudentLeaves: 0, sectionsWithoutClassTeacher: 0, feeOverdueCount: 0, feeOverdueAmount: 0 },
        }),
      );
      expect(card('students').querySelector('strong')?.textContent).toBe('0');
      expect(card('attendance').querySelector('strong')?.textContent).toBe('Not marked yet');
      expect(card('collected').querySelector('strong')?.textContent).toBe('₹0.00');
    });
  });

  describe('the two trend charts', () => {
    it('draw when there is data (one chart each)', () => {
      openWith(full());
      expect(el().querySelectorAll('.chart-stub')).toHaveLength(2);
    });

    it('show the empty state, not an empty frame, when nothing was marked / collected', () => {
      openWith(
        schoolDashboard({
          attendanceTrend: schoolDashboard().attendanceTrend.map((p) => ({ ...p, marked: 0, present: 0, presentPercent: null })),
          feeCollectionTrend: schoolDashboard().feeCollectionTrend.map((m) => ({ ...m, amount: 0 })),
        }),
      );
      expect(el().querySelectorAll('.chart-stub')).toHaveLength(0);
      expect(el().querySelector('[data-empty="attendance"]')?.textContent).toContain('No attendance has been marked in the last 14 days.');
      expect(el().querySelector('[data-empty="fees"]')?.textContent).toContain('No fees were collected in the last 6 months.');
    });

    it('say so when their part could not load', () => {
      openWith(schoolDashboard({ partsUnavailable: ['attendance', 'fees'], attendanceTrend: [], feeCollectionTrend: [] }));
      expect(el().querySelectorAll('.chart-stub')).toHaveLength(0);
      expect(text().match(/This could not be loaded\./g)?.length).toBeGreaterThanOrEqual(2);
    });
  });

  describe('Alerts & reminders', () => {
    it('lists the four alerts; the ones with a screen link to it, the class-teacher one is plain text', () => {
      openWith(full());
      expect(alert('employeeLeave').querySelector('a')?.getAttribute('href')).toBe('/leave/requests');
      expect(alert('studentLeave').querySelector('a')?.getAttribute('href')).toBe('/leave/students');
      expect(alert('feeOverdue').querySelector('a')?.getAttribute('href')).toBe('/fee-dashboard');
      expect(alert('classTeachers').querySelector('a')).toBeNull();
      expect(alert('classTeachers').textContent).toContain('3 sections with no class teacher');
      expect(alert('feeOverdue').textContent).toContain('14 installments');
    });

    it('rows with nothing to do are shown as clear, not hidden', () => {
      openWith(schoolDashboard({ alerts: { pendingEmployeeLeaves: 0, pendingStudentLeaves: 0, sectionsWithoutClassTeacher: 0, feeOverdueCount: 0, feeOverdueAmount: 0 } }));
      expect(el().querySelectorAll('[data-alert].clear')).toHaveLength(4);
      expect(alert('employeeLeave').textContent).toContain('No requests waiting');
    });
  });

  describe('events, notices and admissions', () => {
    it('list the next events (with ongoing flagged), the latest notices and the latest applications', () => {
      openWith(full());
      const events = [...el().querySelectorAll('[aria-label="Upcoming events"] li')];
      expect(events).toHaveLength(2);
      expect(events[0].textContent).toContain('Annual Day');
      expect(events[1].textContent).toContain('Book fair');
      expect(events[1].textContent).toContain('Ongoing');
      const notices = [...el().querySelectorAll('[aria-label="Recent notices"] li')];
      expect(notices[0].textContent).toContain('Diwali holiday');
      expect(notices[0].textContent).toContain('Students & guardians');
      const adms = [...el().querySelectorAll('[aria-label="Recent admission applications"] li')];
      expect(adms).toHaveLength(2);
      expect(adms[0].textContent).toContain('Asha Verma');
      expect(adms[0].textContent).toContain('APS26-0031');
      expect(adms[0].textContent).toContain('Under Review');
      expect(adms[1].textContent).toContain('Approved');
      expect(adms[0].querySelector('a')?.getAttribute('href')).toBe('/admissions/31');
    });

    it('each panel has its own empty state', () => {
      openWith(schoolDashboard());
      expect(el().querySelector('[data-empty="events"]')?.textContent).toContain('No upcoming events.');
      expect(el().querySelector('[data-empty="notices"]')?.textContent).toContain('No notices have been published yet.');
      expect(el().querySelector('[data-empty="admissions"]')?.textContent).toContain('No admission applications yet.');
    });

    it('a panel whose part failed says so instead of claiming it is empty', () => {
      openWith(schoolDashboard({ partsUnavailable: ['events', 'notices', 'admissions'] }));
      expect(el().querySelector('[data-empty]')).toBeNull();
      expect(text().match(/This could not be loaded\./g)?.length).toBe(3);
    });

    it('each panel links to its module', () => {
      openWith(full());
      const hrefs = [...el().querySelectorAll('.panel-head a')].map((a) => a.getAttribute('href'));
      expect(hrefs).toEqual(['/calendar', '/notices', '/admissions']);
    });
  });
});
