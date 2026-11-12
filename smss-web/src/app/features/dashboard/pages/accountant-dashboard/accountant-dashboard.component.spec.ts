import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { ApexOptions } from 'ng-apexcharts';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ChartHostComponent } from '../../components/chart/chart.component';
import { AccountantDashboard } from '../../models/accountant-dashboard.model';
import { AccountantDashboardComponent } from './accountant-dashboard.component';

const URL = `${environment.apiUrl}/api/AccountantDashboard/summary`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

@Component({ selector: 'app-chart', changeDetection: ChangeDetectionStrategy.OnPush, template: '<span class="chart-stub"></span>' })
class StubChartComponent {
  readonly options = input.required<ApexOptions>();
  readonly label = input('Chart');
}

const months = ['2026-06-01', '2026-07-01', '2026-08-01', '2026-09-01', '2026-10-01', '2026-11-01'];

export function accountantDashboard(over: Partial<AccountantDashboard> = {}): AccountantDashboard {
  return {
    today: '2026-11-14', academicYearId: 7, academicYearName: '2026-27',
    stats: { collectedToday: 2500, collectedThisMonth: 15000, outstanding: 40000, overdueCount: 6, overdueAmount: 12000, upcomingCount: 4, upcomingAmount: 8000, upcomingDays: 7 },
    collectionTrend: months.map((m, i) => ({ month: m, label: m, amount: i === 5 ? 15000 : 1000 * i })),
    recentPayments: [{ paymentId: 1, receiptNumber: 'RCP-2026-0001', studentId: 'DPS001ST001', studentName: 'Abhinav Kumar', amount: 5000, paymentMode: 'UPI', paymentDate: '2026-11-14T05:00:00Z' }],
    partsUnavailable: [],
    ...over,
  };
}

describe('AccountantDashboardComponent', () => {
  let fixture: ComponentFixture<AccountantDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const card = (id: string) => el().querySelector(`[data-card="${id}"]`)!;

  function create(): void {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['Accountant'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.overrideComponent(AccountantDashboardComponent, { remove: { imports: [ChartHostComponent] }, add: { imports: [StubChartComponent] } });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AccountantDashboardComponent);
    fixture.detectChanges();
  }
  function open(d: AccountantDashboard): void {
    create();
    http.expectOne(URL).flush(env(d));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('makes one GET request and shows the five headline numbers', () => {
    open(accountantDashboard());
    expect(http.match(() => true)).toHaveLength(0);
    expect(card('today').textContent).toContain('2,500');
    expect(card('month').textContent).toContain('15,000');
    expect(card('outstanding').textContent).toContain('40,000');
    expect(card('overdue').textContent).toContain('12,000');
    expect(card('overdue').textContent).toContain('6 installments past due');
    expect(card('upcoming').textContent).toContain('8,000');
    expect(card('upcoming').textContent).toContain('Due in the next 7 days');
    expect(card('upcoming').textContent).toContain('4 installments falling due');
  });

  it('links to the class-wise report and the Fee Dashboard', () => {
    open(accountantDashboard());
    expect(el().querySelector('[data-link="class-report"]')?.getAttribute('href')).toBe('/fee-reports/class');
    expect(card('today').getAttribute('href')).toBe('/fee-dashboard');
    expect(card('outstanding').getAttribute('href')).toBe('/fee-reports/class');
  });

  it('lists the last payments in a table', () => {
    open(accountantDashboard());
    const t = text();
    for (const s of ['RCP-2026-0001', 'Abhinav Kumar (DPS001ST001)', '5,000', 'UPI']) expect(t).toContain(s);
  });

  it('nothing overdue and nothing due soon read as such, not as 0', () => {
    open(accountantDashboard({ stats: { ...accountantDashboard().stats, overdueCount: 0, overdueAmount: 0, upcomingCount: 0, upcomingAmount: 0 } }));
    expect(card('overdue').textContent).toContain('Nothing overdue');
    expect(card('upcoming').textContent).toContain('Nothing due');
  });

  it('a day with no payments is ₹0 collected and the empty states say so', () => {
    open(accountantDashboard({
      stats: { ...accountantDashboard().stats, collectedToday: 0 }, recentPayments: [],
      collectionTrend: months.map((m) => ({ month: m, label: m, amount: 0 })),
    }));
    expect(card('today').textContent).toMatch(/₹\s*0/);
    expect(el().querySelector('[data-empty="payments"]')).not.toBeNull();
    expect(el().querySelector('[data-empty="fees"]')).not.toBeNull();
  });

  it('a part the server could not load says Unavailable, never 0', () => {
    open(accountantDashboard({
      stats: { collectedToday: null, collectedThisMonth: null, outstanding: null, overdueCount: null, overdueAmount: null, upcomingCount: null, upcomingAmount: null, upcomingDays: 7 },
      partsUnavailable: ['fees', 'collectedToday', 'payments'], collectionTrend: [], recentPayments: [],
    }));
    for (const id of ['today', 'month', 'outstanding', 'overdue', 'upcoming']) {
      expect(card(id).textContent).toContain('Unavailable');
      expect(card(id).textContent).not.toMatch(/₹\s*0/);
    }
    expect(text()).toContain('This could not be loaded.');
    expect(el().querySelector('[data-empty="payments"]')).toBeNull();
  });

  it('one failed part leaves the others showing', () => {
    open(accountantDashboard({ stats: { ...accountantDashboard().stats, collectedToday: null }, partsUnavailable: ['collectedToday'] }));
    expect(card('today').textContent).toContain('Unavailable');
    expect(card('month').textContent).toContain('15,000');
    expect(text()).toContain('RCP-2026-0001');
  });

  it('a failed request offers Try again', () => {
    create();
    http.expectOne(URL).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'err' });
    fixture.detectChanges();
    expect(text()).toContain('could not be loaded');
    expect([...el().querySelectorAll('button')].some((b) => b.textContent?.includes('Try again'))).toBe(true);
  });
});
