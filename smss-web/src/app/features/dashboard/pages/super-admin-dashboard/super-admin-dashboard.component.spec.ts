import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { SuperAdminSummary } from '../../../subscriptions/models/super-admin-dashboard.model';
import { SuperAdminDashboardComponent } from './super-admin-dashboard.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const summary = (o: Partial<SuperAdminSummary> = {}): SuperAdminSummary => ({
  asOf: '2026-10-29', totalSchools: 5, activeSchools: 4, trialCount: 2, activeCount: 1, overdueCount: 1, cancelledCount: 1, notSetCount: 0, trialExpiredCount: 1,
  totalCollected: 12000, totalOutstanding: 3500.5, overdueAmount: 1000, upcomingRenewalDays: 30, upcomingRenewalCount: 1,
  upcomingRenewals: [{ schoolId: 'S2', schoolName: 'Beta School', billingFrequency: 'YEARLY', nextRenewalDate: '2026-11-10' }],
  pricing: { pricingId: 1, ratePerStudentMonthly: 20, yearlyDiscountPercent: 10, trialDays: 30, effectiveFrom: '2026-10-01', isActive: true },
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
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SuperAdminDashboardComponent);
    fixture.detectChanges();
    flush(http.expectOne(`${API}/api/SuperAdminDashboard/summary`));
    fixture.detectChanges();
  }
  const ok = (s: SuperAdminSummary = summary()) => (req: TestRequest) => req.flush(env(s));

  /** The stat card with this label, as the link it is. */
  const card = (label: string) => Array.from(el().querySelectorAll<HTMLAnchorElement>('a.stat')).find((a) => a.querySelector('.label')?.textContent?.trim() === label);
  const target = (label: string) => card(label)?.getAttribute('href');

  afterEach(() => {
    http.verify(); // fails if the dashboard asked for anything besides the summary
    clearSessions();
  });

  it('shows the school and subscription numbers and the two info panels', () => {
    open(ok());
    const t = text();
    expect(t).toContain('Registered schools');
    expect(t).toContain('Total collected');
    expect(t).toContain('₹12,000.00');
    expect(t).toContain('₹3,500.50');
    expect(t).toContain('Upcoming renewals');
    expect(t).toContain('Beta School');
    expect(t).toContain('Current pricing');
    expect(t).toContain('₹20.00');
  });

  it('has the three school cards and six subscription cards', () => {
    open(ok());
    const labels = Array.from(el().querySelectorAll('a.stat .label')).map((l) => l.textContent?.trim());
    expect(labels).toEqual(['Registered schools', 'On free trial', 'Paying subscriptions', 'Trial', 'Active', 'Overdue', 'Total collected', 'Total outstanding', 'Renewals in 30 days']);
  });

  it('no longer renders the schools table, the invoices table, a search box or a pager', () => {
    open(ok());
    expect(el().querySelector('app-table')).toBeNull();
    expect(el().querySelector('app-pagination')).toBeNull();
    expect(el().querySelector('input')).toBeNull();
    expect(el().querySelector('select')).toBeNull();
    expect(text()).not.toContain('Generate invoice');
    expect(text()).not.toContain('Record payment');
  });

  it('school cards link to /subscriptions, pre-filtered where the card says the same thing', () => {
    open(ok());
    expect(target('Registered schools')).toBe('/subscriptions');
    expect(target('On free trial')).toBe('/subscriptions?status=TRIAL');
    expect(target('Paying subscriptions')).toBe('/subscriptions'); // Active + Overdue together is not one status
    expect(target('Trial')).toBe('/subscriptions?status=TRIAL');
    expect(target('Active')).toBe('/subscriptions?status=ACTIVE');
    expect(target('Overdue')).toBe('/subscriptions?status=OVERDUE');
  });

  it('money and renewal cards link to /invoices', () => {
    open(ok());
    expect(target('Total collected')).toBe('/invoices');
    expect(target('Total outstanding')).toBe('/invoices');
    expect(target('Renewals in 30 days')).toBe('/invoices');
  });

  it('shows the "Not set up" card (linking to that filter) only when some school has no subscription yet', () => {
    open(ok());
    expect(card('Not set up')).toBeUndefined();
    http.verify();
    TestBed.resetTestingModule();
    open(ok(summary({ notSetCount: 2 })));
    expect(card('Not set up')?.getAttribute('href')).toBe('/subscriptions?status=NOT_SET');
  });

  it('offers a retry when the summary fails, and reloads it', () => {
    open((req) => req.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' }));
    expect(text()).toContain('The numbers could not be loaded.');
    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Try again') as HTMLButtonElement).click();
    http.expectOne(`${API}/api/SuperAdminDashboard/summary`).flush(env(summary()));
    fixture.detectChanges();
    expect(text()).toContain('Registered schools');
  });
});
