import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { ApexOptions } from 'ng-apexcharts';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ChartHostComponent } from '../../components/chart/chart.component';
import { SchoolAdminDashboard } from '../../models/school-admin-dashboard.model';
import { dashboard } from '../../utils/school-admin-dashboard.testing';
import { SchoolAdminDashboardComponent } from './school-admin-dashboard.component';

const URL = `${environment.apiUrl}/api/Dashboard/school-admin`;
const YEARS = `${environment.apiUrl}/api/AcademicYear`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

/** ApexCharts needs a real browser, so the specs swap the one component that draws them. */
@Component({ selector: 'app-chart', changeDetection: ChangeDetectionStrategy.OnPush, template: '<span class="chart-stub"></span>' })
class StubChartComponent {
  readonly options = input.required<ApexOptions>();
  readonly label = input('Chart');
}

describe('SchoolAdminDashboardComponent', () => {
  let fixture: ComponentFixture<SchoolAdminDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const card = (id: string) => el().querySelector(`[data-card="${id}"]`) as HTMLElement | null;

  const year = (id: number, name: string, over: Record<string, unknown> = {}) => ({
    academicYearId: id, schoolId: 'S', yearName: name, startDate: '2026-04-01', endDate: '2027-03-31', isCurrent: false, statusName: 'Active', isClosed: false, ...over,
  });
  /** The page also lists the school's years for its selector. */
  function flushYears(years: unknown[] = [year(3, '2026-27', { isCurrent: true })]): void {
    http.match(YEARS).forEach((r) => r.flush(env(years)));
  }

  function open(flush?: (req: TestRequest) => void, years?: unknown[]): void {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'School Id', roles: ['School Admin'], schoolName: 'ZZ Demo School' }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.overrideComponent(SchoolAdminDashboardComponent, { remove: { imports: [ChartHostComponent] }, add: { imports: [StubChartComponent] } });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SchoolAdminDashboardComponent);
    fixture.detectChanges();
    flushYears(years);
    if (flush) {
      flush(http.expectOne(URL));
      fixture.detectChanges();
    }
  }
  const withData = (d: SchoolAdminDashboard) => (req: TestRequest) => req.flush(env(d));

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('asks the one endpoint, with no school id anywhere in the request', () => {
    open();
    const req = http.expectOne(URL);
    expect(req.request.url).toBe(URL);   // no id in the path
    expect(req.request.params.keys()).toEqual([]);   // none in the query either
    req.flush(env(dashboard()));
  });

  it('shows loading skeletons while the request is open, then the three sections', () => {
    open();
    expect(el().querySelector('[data-state="loading"]')).not.toBeNull();
    expect(el().querySelectorAll('.sk-card').length).toBeGreaterThan(0);
    http.expectOne(URL).flush(env(dashboard()));
    fixture.detectChanges();
    expect(el().querySelector('[data-state="loading"]')).toBeNull();
    expect([...el().querySelectorAll('h2')].map((h) => h.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Students & Fees November 2026', 'Money', 'People']);
  });

  it('shows every figure of the seeded response', () => {
    open(withData(dashboard()));
    expect(card('total-students')!.textContent).toContain('6');
    expect(card('paid')!.textContent).toContain('2');
    expect(card('not-paid')!.textContent).toContain('2');
    expect(card('collected')!.textContent).toContain('₹2,500');
    expect(card('outstanding')!.textContent).toContain('₹1,100');
    expect(card('income-month')!.textContent).toContain('₹2,500');
    expect(card('income-session')!.textContent).toContain('₹2,700');
    expect(card('income-session')!.textContent).toContain('Academic Year 2026-27');
    expect(card('attendance')!.textContent).toContain('2 present');
    expect(card('attendance')!.textContent).toContain('out of 3 students');
    expect(card('new-admissions')!.textContent).toContain('3');
    expect(card('employees')!.textContent).toContain('Teachers');
    expect(card('vehicles')!.textContent).toContain('2');
  });

  it('expenses read "Not set up yet", never a zero', () => {
    open(withData(dashboard()));
    for (const id of ['expenses-month', 'expenses-session']) {
      expect(card(id)!.textContent).toContain('Not set up yet');
      expect(card(id)!.textContent).not.toContain('₹0');
    }
    expect(el().querySelector('[data-note="expenses"]')).not.toBeNull();
  });

  it('the not-paid card links to the class-wise fee report for the current month; the other cards are not links', () => {
    open(withData(dashboard()));
    const link = card('not-paid') as HTMLAnchorElement;
    expect(link.tagName).toBe('A');
    expect(link.getAttribute('href')).toBe('/fee-reports/class?month=2026-11');
    for (const id of ['paid', 'collected', 'outstanding', 'total-students']) expect(card(id)!.tagName, id).toBe('DIV');
  });

  it('tooltips say what each fee card counts', () => {
    open(withData(dashboard()));
    expect(card('collected')!.getAttribute('title')).toContain('not voided');
    expect(card('paid')!.getAttribute('title')).toContain('discounted due counts as paid');
  });

  it('hides the vehicles card when the school has no vehicles', () => {
    open(withData(dashboard({ activeVehicles: 0 })));
    expect(card('vehicles')).toBeNull();
  });

  it('attendance says "Not marked yet" when nothing was marked', () => {
    open(withData(dashboard({ attendanceMarkedToday: 0, attendancePresentToday: 0 })));
    expect(card('attendance')!.textContent).toContain('Not marked yet');
    expect(card('attendance')!.textContent).not.toMatch(/0 present/);
  });

  it('a school with no current academic year shows "Not set up yet" for the session figures, not zeros', () => {
    open(withData(dashboard({ session: null, incomeThisSession: null, newAdmissionsThisSession: null })));
    expect(card('income-session')!.textContent).toContain('Not set up yet');
    expect(card('new-admissions')!.textContent).toContain('Not set up yet');
  });

  it('shows no donut, but a note, while nobody has a due this month', () => {
    open(withData(dashboard({ studentsPaidThisMonth: 0, studentsNotPaidThisMonth: 0 })));
    expect(el().querySelector('[data-empty="dues"]')).not.toBeNull();
  });

  it('a brand-new school (no students, no staff) gets an empty-state note', () => {
    open(withData(dashboard({ totalStudents: 0, employees: { total: 0, teachers: 0, accountants: 0, admissionStaff: 0, drivers: 0, other: 0 }, activeVehicles: 0 })));
    expect(el().querySelector('[data-state="empty"]')).not.toBeNull();
    expect(text()).toContain('No students or staff have been added yet');
  });

  it('shows an error with a Try again button that asks again', () => {
    open();
    http.expectOne(URL).flush({ message: 'x' }, { status: 500, statusText: 'err' });
    fixture.detectChanges();
    expect(el().querySelector('[data-state="error"]')).not.toBeNull();
    expect(text()).toContain('could not be loaded');
    (el().querySelector('[data-state="error"] button') as HTMLButtonElement).click();
    http.expectOne(URL).flush(env(dashboard()));
    fixture.detectChanges();
    expect(el().querySelector('[data-state="error"]')).toBeNull();
    expect(card('total-students')).not.toBeNull();
  });

  it('the welcome banner names the school and the academic year', () => {
    open(withData(dashboard()));
    expect(text()).toContain('Welcome Back, School Admin');
    expect(text()).toContain('ZZ Demo School');
    expect(text()).toContain('Academic Year 2026-27');
  });
  it('lists every year of the school in the selector, closed ones marked', () => {
    open(withData(dashboard()), [year(1, '2025-26', { isClosed: true }), year(3, '2026-27', { isCurrent: true })]);
    const options = [...el().querySelectorAll('app-select option, app-select [role="option"]')].map((o) => o.textContent?.trim());
    expect(options.join('|')).toContain('2025-26 (closed, read-only)');
  });

  it('choosing another year asks the endpoint for that year only and never changes the current year', () => {
    open(withData(dashboard()));
    const select = el().querySelector('app-select');
    expect(select).not.toBeNull();
    fixture.componentInstance['yearControl'].setValue('2');
    const req = http.expectOne((r) => r.url === URL);
    expect(req.request.params.get('academicYearId')).toBe('2');
    expect(req.request.method).toBe('GET');
    req.flush(env(dashboard({ session: { academicYearId: 2, name: '2027-28', startDate: '2027-04-01', endDate: '2028-03-31', isCurrent: false, isClosed: false } })));
    fixture.detectChanges();
    expect(el().querySelector('[data-note="viewing"]')!.textContent).toContain('You are viewing 2027-28');
    expect(el().querySelector('[data-note="viewing"]')!.textContent).toContain('current year is 2026-27');
  });

  it('says so, in words, when "this month" was moved into the viewed year', () => {
    open(withData(dashboard({ monthClamped: true, monthNote: 'Showing March 2027: today is after the academic year 2026-27.' })));
    expect(el().querySelector('[data-note="month"]')!.textContent).toContain('Showing March 2027');
  });

  it('the Pending documents tile counts the students missing a required document and links to the list', () => {
    open(withData(dashboard({ studentsWithPendingDocuments: 4 })));
    expect(card('pending-documents')!.textContent).toContain('4');
    expect(card('pending-documents')!.getAttribute('href')).toBe('/student-documents');
  });

  it('the Pending documents tile says Unavailable, never 0, when the count could not be worked out', () => {
    open(withData(dashboard({ studentsWithPendingDocuments: null })));
    expect(card('pending-documents')!.textContent).toContain('Unavailable');
    expect(card('pending-documents')!.textContent).not.toMatch(/0/);
  });

  it('the Parent details to complete tile counts the parents and links to the list', () => {
    open(withData(dashboard({ parentDetailsToComplete: 6 })));
    expect(card('parent-details')!.textContent).toContain('6');
    expect(card('parent-details')!.getAttribute('href')).toBe('/parent-details');
  });

  it('the Parent details to complete tile says Unavailable, never 0, when the count could not be worked out', () => {
    open(withData(dashboard({ parentDetailsToComplete: null })));
    expect(card('parent-details')!.textContent).toContain('Unavailable');
  });

  it('asks for the school WhatsApp number with a banner when it is missing, and shows nothing when it is set', () => {
    open(withData(dashboard({ whatsAppMissing: true })));
    expect(el().querySelector('[data-banner="whatsapp-missing"]')!.textContent).toContain('no WhatsApp number');
    expect(el().querySelector('[data-note="viewing"]')).toBeNull();
    expect(card('total-students')).not.toBeNull();   // the rest of the dashboard still works
  });

  it('no WhatsApp banner when the number is set', () => {
    open(withData(dashboard({ whatsAppMissing: false })));
    expect(el().querySelector('[data-banner="whatsapp-missing"]')).toBeNull();
  });

  it('a year with no fee items shows "No fee structure yet" on the fee cards, not zeros', () => {
    open(withData(dashboard({ hasFeeSetup: false, totalStudents: 0, studentsPaidThisMonth: 0, studentsNotPaidThisMonth: 0, feeCollectedThisMonth: 0, feeOutstandingThisMonth: 0, incomeThisMonth: 0, incomeThisSession: 0 })));
    for (const id of ['paid', 'not-paid', 'collected', 'outstanding', 'income-month', 'income-session']) expect(card(id)!.textContent).toContain('No fee structure yet');
    expect(card('total-students')!.textContent).toContain('0');
    expect(el().querySelector('[data-empty="fees"]')).not.toBeNull();
  });

  it('the fee tiles cannot be read as total cash received', () => {
    open(withData(dashboard()));
    expect(card('collected')!.textContent).toContain('Fees received this month');
    expect(card('income-session')!.textContent).toContain('Fees received for this session');
    expect(card('collected')!.getAttribute('title')).toContain('not the total cash received');
  });
});
