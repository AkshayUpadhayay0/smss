import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { ApexOptions } from 'ng-apexcharts';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { DashboardStat, SuperAdminSummary } from '../../../subscriptions/models/super-admin-dashboard.model';
import { ChartHostComponent } from '../../components/chart/chart.component';
import { SuperAdminDashboardComponent } from './super-admin-dashboard.component';

const API = environment.apiUrl;
const SUMMARY = `${API}/api/SuperAdminDashboard/summary`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

/** ApexCharts needs a real browser (SVG measuring), so the specs swap the one component that draws them. */
@Component({ selector: 'app-chart', changeDetection: ChangeDetectionStrategy.OnPush, template: '' })
class StubChartComponent {
  readonly options = input.required<ApexOptions>();
  readonly label = input('Chart');
}

const stat = (value: number, previousValue: number | null = null, trendPercent: number | null = null): DashboardStat => ({ value, previousValue, trendPercent });

const summary = (o: Partial<SuperAdminSummary> = {}): SuperAdminSummary => ({
  asOf: '2026-10-29', totalSchools: 5, activeSchools: 4, trialCount: 2, activeCount: 1, overdueCount: 1, cancelledCount: 1, notSetCount: 0, trialExpiredCount: 1,
  totalCollected: 12000, totalOutstanding: 3500.5, overdueAmount: 1000, upcomingRenewalDays: 30, upcomingRenewalCount: 1,
  upcomingRenewals: [{ schoolId: 'S2', schoolName: 'Beta School', billingFrequency: 'YEARLY', nextRenewalDate: '2026-11-10' }],
  pricing: { pricingId: 1, ratePerStudentMonthly: 20, yearlyDiscountPercent: 10, trialDays: 30, effectiveFrom: '2026-10-01', isActive: true },
  from: '2026-10-01', to: '2026-10-29', previousFrom: '2026-09-01', previousTo: '2026-09-29',
  stats: {
    totalSchools: stat(5, 4, 25), activeSubscriptions: stat(2), activeSubscriptionsPercentOfTotal: 40, paymentsCollected: stat(8000, 10000, -20),
    outstandingPayments: stat(3500.5), expenses: stat(3000, 2000, 50), salaryPayouts: 1000, cashSurplus: stat(5000, 8000, -37.5),
  },
  revenueVsExpenses: [{ month: '2026-10-01', label: 'Oct 2026', revenue: 8000, expenses: 3000 }],
  schoolRegistrations: [{ month: '2026-10-01', label: 'Oct 2026', count: 2 }],
  paymentCollection: [
    { key: 'Paid', count: 2, amount: 3000, outstanding: 0 },
    { key: 'Pending', count: 1, amount: 1000, outstanding: 1000 },
    { key: 'PartiallyPaid', count: 1, amount: 1000, outstanding: 400 },
    { key: 'Overdue', count: 0, amount: 0, outstanding: 0 },
  ],
  recentSchools: [{ schoolId: 'S2', schoolCode: 'BETA', schoolName: 'Beta School', boardType: 'CBSE', schoolType: 'Private', registeredOn: '2026-10-20', status: 'Active' }],
  recentTransactions: [{ paymentId: 9, referenceNumber: 'UTR123', schoolId: 'S2', schoolName: 'Beta School', paymentDate: '2026-10-21', amount: 500, paymentMode: 'UPI', status: 'Active' }],
  ...o,
});

describe('SuperAdminDashboardComponent', () => {
  let fixture: ComponentFixture<SuperAdminDashboardComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');

  function open(flush: (req: TestRequest) => void): void {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    TestBed.overrideComponent(SuperAdminDashboardComponent, { remove: { imports: [ChartHostComponent] }, add: { imports: [StubChartComponent] } });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SuperAdminDashboardComponent);
    fixture.detectChanges();
    flush(http.expectOne(SUMMARY));
    fixture.detectChanges();
  }
  const ok = (s: SuperAdminSummary = summary()) => (req: TestRequest) => req.flush(env(s));

  /** The headline card with this label, as the link it is. */
  const card = (label: string) => Array.from(el().querySelectorAll<HTMLAnchorElement>('a.stat')).find((a) => a.querySelector('.label')?.textContent?.trim() === label);
  const target = (label: string) => card(label)?.getAttribute('href');
  const click = (label: string) => (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement).click();

  afterEach(() => {
    http.verify(); // fails if the dashboard asked for anything besides the summary
    clearSessions();
  });

  it('asks for the default period first (no dates), then shows the dates the server chose', () => {
    open((req) => {
      expect(req.request.params.keys()).toEqual([]);
      req.flush(env(summary()));
    });
    const inputs = Array.from(el().querySelectorAll<HTMLInputElement>('input[type=date]'));
    expect(inputs.map((i) => i.value)).toEqual(['2026-10-01', '2026-10-29']);
    expect(text()).toMatch(/Compared with 01 Sep\w* 2026 to 29 Sep\w* 2026/); // the previous period
  });

  it('has the six headline cards, in order', () => {
    open(ok());
    const labels = Array.from(el().querySelectorAll('a.stat .label')).map((l) => l.textContent?.trim());
    expect(labels).toEqual(['Total registered schools', 'Active subscriptions', 'Payments collected', 'Outstanding payments', 'Expenses', 'Cash surplus']);
  });

  it('shows each figure, and every number the old dashboard had is still somewhere on the page', () => {
    open(ok());
    const t = text();
    expect(t).toContain('₹8,000.00'); // payments collected in the period
    expect(t).toContain('₹12,000.00 all time'); // old "Total collected"
    expect(t).toContain('₹3,500.50'); // old "Total outstanding"
    expect(t).toContain('4 active, 1 inactive'); // old "Registered schools"
    expect(t).toContain('40% of all schools'); // active subscriptions
    expect(t).toContain('includes ₹1,000.00 salary');
    expect(t).toContain('Schools on free trial'); // old "On free trial"
    expect(t).toContain('2 schools, 1 past the trial end date');
    expect(t).toContain('1 school, ₹1,000.00 past due'); // old Overdue + overdue amount
    expect(t).toContain('1 subscription due to renew'); // old renewals card
    expect(t).toContain('Upcoming renewals');
    expect(t).toContain('Beta School');
    expect(t).toContain('Current pricing');
    expect(t).toContain('₹20.00');
  });

  it('shows the trend with its direction, and a rise in expenses as bad', () => {
    open(ok());
    const trend = (label: string) => card(label)?.querySelector('.trend');
    expect(trend('Total registered schools')?.textContent?.trim()).toBe('25%');
    expect(trend('Total registered schools')?.classList).toContain('good');
    expect(trend('Payments collected')?.classList).toContain('bad'); // fell 20%
    expect(trend('Expenses')?.textContent?.trim()).toBe('50%');
    expect(trend('Expenses')?.classList).toContain('bad'); // spending more is bad even though the number went up
    expect(trend('Cash surplus')?.classList).toContain('bad');
    expect(trend('Active subscriptions')).toBeNull(); // no history for subscription status
    expect(trend('Outstanding payments')).toBeNull();
  });

  it('shows a loss with the minus in front', () => {
    open(ok(summary({ stats: { ...summary().stats, cashSurplus: stat(-1500, 500, -400) } })));
    expect(card('Cash surplus')?.querySelector('strong')?.textContent?.trim()).toBe('-₹1,500.00');
  });

  it('shows no trend badge when there is nothing to compare with', () => {
    open(ok(summary({ stats: { ...summary().stats, paymentsCollected: stat(8000, 0, null) } })));
    expect(card('Payments collected')?.querySelector('.trend')).toBeNull();
  });

  it('cards link to the screens behind them, pre-filtered where a filter says the same thing', () => {
    open(ok());
    expect(target('Total registered schools')).toBe('/schools');
    expect(target('Active subscriptions')).toBe('/subscriptions?status=ACTIVE');
    expect(target('Payments collected')).toBe('/invoices');
    expect(target('Outstanding payments')).toBe('/invoices');
    expect(target('Expenses')).toBe('/company/expenses');
    expect(target('Cash surplus')).toBe('/company/overview');
  });

  it('draws the three charts from the summary', () => {
    open(ok());
    const charts = el().querySelectorAll('app-chart');
    expect(charts.length).toBe(3);
  });

  it('lists the subscription statuses, each linking to its filter, and "Not set up" only when some school has no subscription', () => {
    open(ok());
    const links = Array.from(el().querySelectorAll<HTMLAnchorElement>('.legend a')).map((a) => [a.textContent?.replace(/\s+/g, ' ').trim(), a.getAttribute('href')]);
    expect(links).toEqual([
      ['Trial2', '/subscriptions?status=TRIAL'],
      ['Active1', '/subscriptions?status=ACTIVE'],
      ['Overdue1', '/subscriptions?status=OVERDUE'],
      ['Cancelled1', '/subscriptions?status=CANCELLED'],
    ]);
    http.verify();
    TestBed.resetTestingModule();
    open(ok(summary({ notSetCount: 2 })));
    expect(el().querySelector('.legend a[href="/subscriptions?status=NOT_SET"]')).not.toBeNull();
  });

  it('shows the payment collection bar and breakdown for the period', () => {
    open(ok());
    expect(text()).toContain('Invoices generated 01 Oct 2026 to 29 Oct 2026');
    const segs = Array.from(el().querySelectorAll<HTMLElement>('.bar .seg'));
    expect(segs.length).toBe(3); // Overdue has no invoices: no segment
    expect(segs[0].style.flexBasis).toBe('60%');
    const rows = Array.from(el().querySelectorAll('.breakdown tbody tr')).map((r) => r.textContent?.replace(/\s+/g, ' ').trim());
    expect(rows[0]).toBe('Paid2₹3,000.0060%₹0.00');
    expect(rows[2]).toBe('Partially paid1₹1,000.0020%₹400.00');
    expect(rows[3]).toBe('Overdue0₹0.000%₹0.00');
  });

  it('says so when no invoice was generated in the period', () => {
    open(ok(summary({ paymentCollection: summary().paymentCollection.map((b) => ({ ...b, count: 0, amount: 0, outstanding: 0 })) })));
    expect(text()).toContain('No invoices were generated in this period.');
    expect(el().querySelector('.bar')).toBeNull();
  });

  it('quick actions link to existing screens (and there is no download-report button)', () => {
    open(ok());
    expect(el().querySelector('a.action[href="/invoices"]')?.textContent).toContain('View payments');
    expect(el().querySelector('a.action[href="/subscriptions"]')?.textContent).toContain('Manage subscriptions');
    expect(text()).toContain('Add school');
    expect(text()).not.toContain('Download');
  });

  it('alerts: overdue, renewals and trial, each linking to where to act; a calm line when there is nothing', () => {
    open(ok());
    const hrefs = Array.from(el().querySelectorAll<HTMLAnchorElement>('.alerts a')).map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual(['/invoices?status=Overdue', '/invoices', '/subscriptions?status=TRIAL']);
    http.verify();
    TestBed.resetTestingModule();
    open(ok(summary({ overdueCount: 0, overdueAmount: 0, upcomingRenewalCount: 0, upcomingRenewals: [], trialCount: 0, trialExpiredCount: 0 })));
    expect(text()).toContain('Nothing is past due');
    expect(text()).toContain('No renewals due');
    expect(text()).toContain('No schools on trial');
  });

  it('recent registrations show board (type when there is no board), date and status, with a link to all schools', () => {
    open(ok(summary({ recentSchools: [
      { schoolId: 'S2', schoolCode: 'BETA', schoolName: 'Beta School', boardType: 'CBSE', schoolType: 'Private', registeredOn: '2026-10-20', status: 'Active' },
      { schoolId: 'S3', schoolCode: 'GAM', schoolName: 'Gamma School', boardType: null, schoolType: 'Government', registeredOn: '2026-10-19', status: 'Inactive' },
    ] })));
    const t = text();
    expect(t).toContain('Recent school registrations');
    expect(t).toContain('Beta School');
    expect(t).toContain('CBSE');
    expect(t).toContain('20 Oct 2026');
    expect(t).toContain('Government');
    expect(el().querySelector('.table-head a[href="/schools"]')?.textContent).toContain('View all');
  });

  it('recent transactions show reference, school, date, amount, method and status, with a link to the payments screen', () => {
    open(ok(summary({ recentTransactions: [
      { paymentId: 9, referenceNumber: 'UTR123', schoolId: 'S2', schoolName: 'Beta School', paymentDate: '2026-10-21', amount: 500, paymentMode: 'UPI', status: 'Active' },
      { paymentId: 8, referenceNumber: null, schoolId: 'S2', schoolName: 'Beta School', paymentDate: '2026-10-20', amount: 700, paymentMode: 'Cash', status: 'Voided' },
    ] })));
    const rows = Array.from(el().querySelectorAll('.table-head + app-table, app-table')).pop()?.textContent?.replace(/\s+/g, ' ') ?? '';
    expect(rows).toContain('UTR123');
    expect(rows).toContain('#8'); // no reference: the payment number
    expect(rows).toContain('₹500.00');
    expect(rows).toContain('Voided');
    expect(el().querySelector('.table-head a[href="/invoices"]')?.textContent).toContain('View all');
  });

  it('changing a date reloads once with both dates; "This month" goes back to the default', () => {
    open(ok());
    vi.useFakeTimers();
    try {
      const [from] = Array.from(el().querySelectorAll<HTMLInputElement>('input[type=date]'));
      from.value = '2026-09-01';
      from.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(300);
      const req = http.expectOne((r) => r.url === SUMMARY);
      expect(req.request.params.get('from')).toBe('2026-09-01');
      expect(req.request.params.get('to')).toBe('2026-10-29');
      req.flush(env(summary({ from: '2026-09-01' })));
      fixture.detectChanges();
      click('This month');
      const reset = http.expectOne((r) => r.url === SUMMARY);
      expect(reset.request.params.keys()).toEqual([]);
      reset.flush(env(summary()));
      fixture.detectChanges();
    } finally {
      vi.useRealTimers();
    }
  });

  it('does not reload for a start date after the end date, and says so', () => {
    open(ok());
    vi.useFakeTimers();
    try {
      const [from] = Array.from(el().querySelectorAll<HTMLInputElement>('input[type=date]'));
      from.value = '2026-11-15';
      from.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(300);
      fixture.detectChanges();
      http.expectNone((r) => r.url === SUMMARY);
      expect(text()).toContain('The start date is after the end date');
    } finally {
      vi.useRealTimers();
    }
  });

  it('no longer renders the schools table, the invoices table, a search box or a pager', () => {
    open(ok());
    expect(el().querySelector('app-pagination')).toBeNull();
    expect(el().querySelector('input:not([type=date])')).toBeNull();
    expect(text()).not.toContain('Generate invoice');
    expect(text()).not.toContain('Record payment');
  });

  it('offers a retry when the summary fails, and reloads it', () => {
    open((req) => req.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' }));
    expect(text()).toContain('The numbers could not be loaded.');
    click('Try again');
    http.expectOne((r) => r.url === SUMMARY).flush(env(summary()));
    fixture.detectChanges();
    expect(text()).toContain('Total registered schools');
  });
});
