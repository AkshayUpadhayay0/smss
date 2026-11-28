import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import type { ApexOptions } from 'ng-apexcharts';
import { environment } from '../../../../../environments/environment';
import { YearContextService } from '../../../../core/services/year-context.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ChartHostComponent } from '../../components/chart/chart.component';
import { PendingPromotionTileComponent } from '../../../promotion/components/pending-promotion-tile/pending-promotion-tile.component';
import { PrincipalDashboard } from '../../models/principal-dashboard.model';
import { PrincipalDashboardComponent } from './principal-dashboard.component';

const URL = `${environment.apiUrl}/api/PrincipalDashboard/summary`;
const YEARS = `${environment.apiUrl}/api/AcademicYear`;
const year = (id: number, name: string, over: Record<string, unknown> = {}) => ({ academicYearId: id, schoolId: 'S', yearName: name, startDate: '2026-04-01', endDate: '2027-03-31', isCurrent: false, statusName: 'Active', isClosed: false, ...over });
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

@Component({ selector: 'app-chart', changeDetection: ChangeDetectionStrategy.OnPush, template: '<span class="chart-stub"></span>' })
class StubChartComponent {
  readonly options = input.required<ApexOptions>();
  readonly label = input('Chart');
}

const day = (i: number) => `2026-11-${String(i + 1).padStart(2, '0')}`;

function principalDashboard(over: Partial<PrincipalDashboard> = {}): PrincipalDashboard {
  return {
    today: '2026-11-14', academicYearId: 7, academicYearName: '2026-27', academicYearIsCurrent: true, academicYearIsClosed: false, currentYearName: '2026-27', viewingNote: null, hasFeeSetup: true,
    stats: { totalStudents: 420, totalStaff: 31, attendanceMarkedToday: 400, attendancePresentToday: 372, attendancePercentToday: 93, feeTotalDue: 1000000, feeCollected: 650000, feeOutstanding: 350000 },
    attendanceTrend: Array.from({ length: 14 }, (_, i) => ({ date: day(i), marked: 50, present: 45, presentPercent: 90 as number | null })),
    alerts: { pendingEmployeeLeaves: 3, applicationsWaitingToBeConfirmed: 2, studentsWithPendingDocuments: 5, parentDetailsToComplete: 8 },
    recentPublications: [{ publicationId: 5, className: 'Class 5', sectionName: 'A', scope: 'TERM', termName: 'Term 1', version: 1, status: 'PUBLISHED', publishedAt: '2026-11-01T05:00:00Z', studentCount: 30 }],
    upcomingEvents: [{ eventId: 1, academicYearId: 7, eventTitle: 'Annual Day', startDate: '2026-11-20', endDate: '2026-11-20', isAllDay: true, eventCategoryId: 1, categoryName: 'Event', categoryCode: 'EVENT', colorHex: '#7c5cfc' }],
    recentNotices: [{ noticeId: 9, title: 'Diwali holiday', audience: 'ALL', createdByName: 'Admin', publishedAt: '2026-11-01T05:00:00Z' }],
    partsUnavailable: [],
    ...over,
  };
}

/** The Pending promotion tile asks for its own count (it has its own spec): here it is only a marker, so this page is tested on its own. */
@Component({ selector: 'app-pending-promotion-tile', template: '', changeDetection: ChangeDetectionStrategy.OnPush })
class StubPendingTileComponent {}

describe('PrincipalDashboardComponent', () => {
  let fixture: ComponentFixture<PrincipalDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const card = (id: string) => el().querySelector(`[data-card="${id}"]`)!;
  const alert = (id: string) => el().querySelector(`[data-alert="${id}"]`)!;

  function create(): void {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['Principal'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.overrideComponent(PrincipalDashboardComponent, { remove: { imports: [ChartHostComponent, PendingPromotionTileComponent] }, add: { imports: [StubChartComponent, StubPendingTileComponent] } });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PrincipalDashboardComponent);
    fixture.detectChanges();
    http.match(YEARS).forEach((r) => r.flush(env([year(6, '2025-26', { isClosed: true }), year(7, '2026-27', { isCurrent: true })])));
    TestBed.tick();   // the page follows the header's viewing year: it asks for the summary once the year is known
    fixture.detectChanges();
  }
  function open(d: PrincipalDashboard): void {
    create();
    http.expectOne((r) => r.url === URL).flush(env(d));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('carries the Pending promotion tile (it fetches its own count)', () => {
    open(principalDashboard());
    expect(fixture.nativeElement.querySelector('app-pending-promotion-tile')).not.toBeNull();
  });

  it('makes one GET request and shows the six headline numbers', () => {
    open(principalDashboard());
    expect(http.match(() => true)).toHaveLength(0);
    expect(card('students').textContent).toContain('420');
    expect(card('staff').textContent).toContain('31');
    expect(card('attendance').textContent).toContain('93%');
    expect(card('attendance').textContent).toContain('372 present of 400 marked');
    expect(card('collected').textContent).toContain('6,50,000');
    expect(card('outstanding').textContent).toContain('3,50,000');
    expect(card('rate').textContent).toContain('65%');
  });

  it('head-count cards have no link (the Principal has no student or employee screen); the fee cards go to the read-only Fee Dashboard', () => {
    open(principalDashboard());
    expect(card('students').tagName).toBe('DIV');
    expect(card('staff').tagName).toBe('DIV');
    expect(card('collected').getAttribute('href')).toBe('/fee-dashboard');
  });

  it('lists what waits for the Principal, linking to the screen that can act on it', () => {
    open(principalDashboard());
    expect(alert('employeeLeave').textContent).toContain('3 requests waiting for a decision');
    expect(alert('admissionsConfirm').textContent).toContain('Applications waiting to be confirmed');
    expect(alert('admissionsConfirm').textContent).toContain('2 applications added, not yet confirmed');
    expect(document.body.textContent).not.toContain('Admissions to decide');   // the decide step is gone
    expect(alert('pendingDocuments').textContent).toContain('5 students missing a required document');
    expect(alert('parentDetails').textContent).toContain('8 parents missing a WhatsApp number or an email');
    expect(alert('employeeLeave').querySelector('a')?.getAttribute('href')).toBe('/leave/requests');
  });

  it('shows the latest published results, events and notices', () => {
    open(principalDashboard());
    expect(text()).toContain('Class 5 · A');
    expect(text()).toContain('Term 1 · version 1 · 30 students');
    expect(text()).toContain('Annual Day');
    expect(text()).toContain('Diwali holiday');
  });

  it('a part the server could not load says Unavailable, never 0', () => {
    open(principalDashboard({
      stats: { ...principalDashboard().stats, feeCollected: null, feeOutstanding: null, feeTotalDue: null },
      partsUnavailable: ['fees', 'employeeLeave', 'results'],
      alerts: { pendingEmployeeLeaves: null, applicationsWaitingToBeConfirmed: 2, studentsWithPendingDocuments: 5, parentDetailsToComplete: 8 },
      recentPublications: [],
    }));
    for (const id of ['collected', 'outstanding', 'rate']) {
      expect(card(id).textContent).toContain('Unavailable');
      expect(card(id).textContent).not.toMatch(/₹\s*0/);
    }
    expect(alert('employeeLeave').textContent).toContain('Could not be loaded');
    expect(text()).toContain('This could not be loaded.');
    expect(el().querySelector('[data-empty="results"]')).toBeNull();   // unavailable is not "empty"
    expect(card('students').textContent).toContain('420');            // the rest still shows
  });

  it('each empty category says it is empty', () => {
    open(principalDashboard({
      recentPublications: [], upcomingEvents: [], recentNotices: [],
      attendanceTrend: Array.from({ length: 14 }, (_, i) => ({ date: day(i), marked: 0, present: 0, presentPercent: null })),
    }));
    for (const k of ['results', 'events', 'notices', 'attendance']) expect(el().querySelector(`[data-empty="${k}"]`)).not.toBeNull();
  });

  it('nothing marked today is "Not marked yet", not 0%', () => {
    open(principalDashboard({ stats: { ...principalDashboard().stats, attendanceMarkedToday: 0, attendancePresentToday: 0, attendancePercentToday: null } }));
    expect(card('attendance').textContent).toContain('Not marked yet');
  });

  it('a failed request offers Try again', () => {
    create();
    http.expectOne((r) => r.url === URL).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'err' });
    fixture.detectChanges();
    expect(text()).toContain('could not be loaded');
    expect([...el().querySelectorAll('button')].some((b) => b.textContent?.includes('Try again'))).toBe(true);
  });

  it('has no year picker of its own: it asks for the header viewing year, explicitly', () => {
    create();
    const req = http.expectOne((r) => r.url === URL);
    expect(req.request.params.get('academicYearId')).toBe('7');           // the current year, sent as an explicit id
    req.flush(env(principalDashboard()));
    fixture.detectChanges();
    expect(el().querySelector('app-select')).toBeNull();
    expect(el().querySelector('[data-note="viewing"]')).toBeNull();     // the current year needs no note
  });

  it('picking another year in the header asks the endpoint for that year and says which year is shown; the current year is not changed', () => {
    open(principalDashboard());
    TestBed.inject(YearContextService).select('6');
    TestBed.tick();
    const req = http.expectOne((r) => r.url === URL);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('academicYearId')).toBe('6');
    req.flush(env(principalDashboard({ academicYearId: 6, academicYearName: '2025-26', academicYearIsCurrent: false, viewingNote: 'You are viewing 2025-26 (closed, read-only). The school current year is 2026-27.' })));
    fixture.detectChanges();
    expect(el().querySelector('[data-note="viewing"]')!.textContent).toContain('You are viewing 2025-26');
    expect(text()).toContain('academic year 2025-26');
  });

  it('a year without fee items says "No fee structure yet" on the fee cards, not zeros, and still shows the head counts', () => {
    open(principalDashboard({ hasFeeSetup: false, stats: { ...principalDashboard().stats, feeTotalDue: null, feeCollected: null, feeOutstanding: null } }));
    for (const id of ['collected', 'outstanding', 'rate']) expect(card(id).textContent).toContain('No fee structure yet');
    expect(card('students').textContent).toContain('420');
    expect(card('students').textContent).toContain('Enrolled in 2026-27');
  });

  it('the fee card is called Fees received, for the viewed year', () => {
    open(principalDashboard());
    expect(card('collected').textContent).toContain('Fees received');
    expect(card('collected').textContent).toContain('For 2026-27');
  });
});
