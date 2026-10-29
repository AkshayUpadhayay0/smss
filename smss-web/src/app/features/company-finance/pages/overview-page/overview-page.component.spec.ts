import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { CompanyFinanceOverview } from '../../models/company-finance.model';
import { CompanyOverviewPageComponent } from './overview-page.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const overview = (o: Partial<CompanyFinanceOverview> = {}): CompanyFinanceOverview => ({
  from: '2026-01-01', to: '2026-10-29', revenue: 125000, expenses: 40000.5, salaryPayouts: 60000, netProfit: 24999.5,
  months: [
    { month: '2026-09-01', label: 'Sep 2026', revenue: 1000, expenses: 400, salaryPayouts: 300, netProfit: 300 },
    { month: '2026-10-01', label: 'Oct 2026', revenue: 0, expenses: 100, salaryPayouts: 0, netProfit: -100 },
  ],
  ...o,
});

describe('CompanyOverviewPageComponent', () => {
  let fixture: ComponentFixture<CompanyOverviewPageComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const req = () => http.expectOne((r) => r.url === `${API}/api/CompanyFinance/overview`);
  const cmp = () => fixture.componentInstance as unknown as { fromControl: { setValue(v: string): void }; toControl: { setValue(v: string): void } };
  const card = (label: string) => Array.from(el().querySelectorAll<HTMLElement>('.stat')).find((a) => a.querySelector('.label')?.textContent?.trim() === label);

  function open(flush: (r: TestRequest) => void = (r) => r.flush(env(overview()))): void {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CompanyOverviewPageComponent);
    fixture.detectChanges();
    const first = req();
    expect(first.request.params.has('from')).toBe(false); // the first load asks for the server's default range
    expect(first.request.params.has('to')).toBe(false);
    flush(first);
    fixture.detectChanges();
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows revenue, expenses, salary payouts and net profit, and the month by month table', () => {
    open();
    expect(text()).toContain('₹1,25,000.00');
    expect(text()).toContain('₹40,000.50');
    expect(text()).toContain('₹60,000.00');
    expect(text()).toContain('₹24,999.50');
    const rows = Array.from(el().querySelectorAll('app-table tbody tr')).map((r) => (r.textContent ?? '').replace(/\s+/g, ' '));
    expect(rows).toHaveLength(2);
    expect(rows[0]).toContain('Sep 2026');
    expect(rows[1]).toContain('Oct 2026');
    expect(rows[1]).toContain('-₹100.00');
  });

  it('shows the range the server chose in the date boxes', () => {
    open();
    const inputs = Array.from(el().querySelectorAll<HTMLInputElement>('input[type=date]')).map((i) => i.value);
    expect(inputs).toEqual(['2026-01-01', '2026-10-29']);
  });

  it('is numbers only: no records table, search box or add button', () => {
    open();
    expect(el().querySelectorAll('app-table')).toHaveLength(1); // only the month summary
    expect(text()).not.toContain('Add Expense');
    expect(text()).not.toContain('Record salary');
  });

  it('revenue links to /invoices, expenses to the filtered expense list, salary to the employees; net profit is not a link', () => {
    open();
    expect(card('Revenue')?.getAttribute('href')).toBe('/invoices');
    expect(card('Expenses')?.getAttribute('href')).toBe('/company/expenses?from=2026-01-01&to=2026-10-29');
    expect(card('Salary payouts')?.getAttribute('href')).toBe('/company/employees');
    expect(card('Net profit')?.tagName).toBe('ARTICLE');
  });

  it('marks a loss', () => {
    open((r) => r.flush(env(overview({ netProfit: -5000 }))));
    expect(card('Net profit')?.classList.contains('loss')).toBe(true);
    TestBed.resetTestingModule();
    open();
    expect(card('Net profit')?.classList.contains('loss')).toBe(false);
  });

  it('reloads for a new range (debounced) with both dates', () => {
    vi.useFakeTimers();
    try {
      open();
      cmp().fromControl.setValue('2026-09-01');
      cmp().toControl.setValue('2026-09-30');
      vi.advanceTimersByTime(300);
      const r = req();
      expect(r.request.params.get('from')).toBe('2026-09-01');
      expect(r.request.params.get('to')).toBe('2026-09-30');
      r.flush(env(overview({ from: '2026-09-01', to: '2026-09-30', months: [] })));
    } finally {
      vi.useRealTimers();
    }
  });

  it('does not call the API for a start date after the end date, and says so', () => {
    vi.useFakeTimers();
    try {
      open();
      cmp().fromControl.setValue('2026-10-10');
      cmp().toControl.setValue('2026-10-01');
      vi.advanceTimersByTime(300);
      fixture.detectChanges();
      http.expectNone((r) => r.url === `${API}/api/CompanyFinance/overview`);
      expect(text()).toContain('The start date is after the end date');
    } finally {
      vi.useRealTimers();
    }
  });

  it('offers a retry when the overview fails', () => {
    open((r) => r.flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' }));
    expect(text()).toContain('The overview could not be loaded.');
    (Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Try again') as HTMLButtonElement).click();
    req().flush(env(overview()));
    fixture.detectChanges();
    expect(text()).toContain('₹1,25,000.00');
  });
});
