import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { Paged, SchoolSubscriptionRow, SuperAdminSummary } from '../../models/super-admin-dashboard.model';
import { SubscriptionsPageComponent } from './subscriptions-page.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const paged = <T>(items: T[], total = items.length): Paged<T> => ({ items, total, page: 1, pageSize: 10 });

const school = (o: Partial<SchoolSubscriptionRow>): SchoolSubscriptionRow => ({
  schoolId: 'S1', schoolCode: 'C1', schoolName: 'Alpha School', schoolStatusId: 1, subscriptionStatus: 'TRIAL', billingFrequency: null,
  trialEndDate: '2026-11-15', trialExpired: false, nextRenewalDate: null, customRatePerStudentMonthly: null, customYearlyDiscountPercent: null,
  includeStaffInBilling: false, studentCount: 7, teacherCount: 3, staffCount: 2, billableUserCount: 7, ...o,
});
const pricing = { pricingId: 1, ratePerStudentMonthly: 20, yearlyDiscountPercent: 10, trialDays: 30, effectiveFrom: '2026-10-01', isActive: true };
const summary = { pricing } as unknown as SuperAdminSummary;

describe('SubscriptionsPageComponent', () => {
  let fixture: ComponentFixture<SubscriptionsPageComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const schoolsReq = () => http.expectOne((r) => r.url === `${API}/api/SuperAdminDashboard/schools`);
  const rowsText = () => Array.from(el().querySelectorAll('app-table tbody tr')).map((r) => (r.textContent ?? '').replace(/\s+/g, ' '));
  const button = (label: string) => Array.from(el().querySelectorAll('app-table button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;

  function open(schools: SchoolSubscriptionRow[] = [school({})], setup?: (f: ComponentFixture<SubscriptionsPageComponent>) => void): void {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SubscriptionsPageComponent);
    setup?.(fixture);
    fixture.detectChanges();
    http.expectOne(`${API}/api/SuperAdminDashboard/summary`).flush(env(summary)); // only for the global pricing shown in the terms dialog
    schoolsReq().flush(env(paged(schools)));
    fixture.detectChanges();
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('has its own page header and the full schools-with-billing table, with no invoices table', () => {
    open();
    expect(el().querySelector('h1')?.textContent?.trim()).toBe('Subscriptions');
    const headers = Array.from(el().querySelectorAll('app-table thead th')).map((h) => h.textContent?.replace(/\s+/g, ' ').trim());
    expect(headers).toEqual(['School', 'Code', 'Subscription', 'Billed users', 'Students / teachers / staff', 'Terms', 'Next renewal', 'Actions']);
    expect(el().querySelectorAll('app-table').length).toBe(1);
    expect(text()).not.toContain('Record payment');
    expect(el().querySelector('input[placeholder="Search by name, code or id…"]')).not.toBeNull();
  });

  it('lists code, status, billed users, head counts, terms and renewal', () => {
    open([
      school({}),
      school({ schoolId: 'S2', schoolCode: 'C2', schoolName: 'Beta School', subscriptionStatus: 'ACTIVE', billingFrequency: 'YEARLY', nextRenewalDate: '2026-11-10',
        customRatePerStudentMonthly: 15, customYearlyDiscountPercent: 25, includeStaffInBilling: true, billableUserCount: 12 }),
    ]);
    const rows = rowsText();
    expect(rows[0]).toContain('Alpha School');
    expect(rows[0]).toContain('C1');
    expect(rows[0]).toContain('Trial');
    expect(rows[0]).toContain('7 / 3 / 2');
    expect(rows[0]).toContain('Global');
    expect(rows[0]).toContain('Trial ends 15 Nov 2026');
    expect(rows[1]).toContain('₹15.00 rate, 25% yearly, + staff');
    expect(rows[1]).toContain('10 Nov 2026');
  });

  it('shows "Trial expired" for a trial past its end date', () => {
    open([school({ trialExpired: true })]);
    expect(text()).toContain('Trial expired');
  });

  it('offers Configure billing only to a trial, Edit terms and Generate invoice only to a configured paying school', () => {
    open([
      school({}),
      school({ schoolId: 'S2', schoolName: 'Beta School', subscriptionStatus: 'ACTIVE', billingFrequency: 'MONTHLY' }),
      school({ schoolId: 'S3', schoolName: 'Gamma School', subscriptionStatus: 'NOT_SET' }),
      school({ schoolId: 'S4', schoolName: 'Delta School', subscriptionStatus: 'CANCELLED', billingFrequency: 'MONTHLY' }),
    ]);
    const rows = rowsText();
    const of = (name: string) => rows.find((r) => r.includes(name)) ?? '';
    expect(of('Alpha School')).toContain('Configure billing');
    expect(of('Alpha School')).not.toContain('Edit terms');
    expect(of('Beta School')).toContain('Generate invoice');
    expect(of('Beta School')).toContain('Edit terms');
    expect(of('Beta School')).not.toContain('Configure billing');
    for (const name of ['Gamma School', 'Delta School']) {
      expect(of(name)).not.toContain('Generate invoice');
      expect(of(name)).not.toContain('Configure billing');
      expect(of(name)).not.toContain('Edit terms');
    }
  });

  it('the "Invoices" action opens /invoices narrowed to that school', () => {
    open([school({ schoolId: 'S2', schoolName: 'Beta School', subscriptionStatus: 'ACTIVE', billingFrequency: 'MONTHLY' })]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    button('Invoices').click();
    expect(navigate).toHaveBeenCalledWith(['/invoices'], { queryParams: { schoolId: 'S2', schoolName: 'Beta School' } });
  });

  it('view and edit go to the existing school screens', () => {
    open();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    (el().querySelector('app-button[aria-label="View school"] button') as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith(['/schools', 'S1', 'view']);
    (el().querySelector('app-button[aria-label="Edit school"] button') as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith(['/schools', 'S1', 'edit']);
  });

  it('opens the configure dialog for a trial and the terms dialog for a configured school, with the global defaults as placeholders', () => {
    open([school({}), school({ schoolId: 'S2', schoolName: 'Beta School', subscriptionStatus: 'ACTIVE', billingFrequency: 'MONTHLY' })]);
    button('Configure billing').click();
    fixture.detectChanges();
    expect(text()).toContain('Configure billing: Alpha School');
    expect(Array.from(el().querySelectorAll('app-modal input[type=number]')).map((i) => i.getAttribute('placeholder'))).toEqual(['Global: ₹20.00', 'Global: 10%']);
    (el().querySelector('app-modal .close') as HTMLButtonElement).click();
    fixture.detectChanges();
    button('Edit terms').click();
    fixture.detectChanges();
    expect(text()).toContain('Billing terms: Beta School');
  });

  it('the ?status= query parameter pre-selects the status filter (an unknown value is ignored)', () => {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SubscriptionsPageComponent);
    fixture.componentRef.setInput('status', 'overdue');
    fixture.detectChanges();
    http.expectOne(`${API}/api/SuperAdminDashboard/summary`).flush(env(summary));
    const req = schoolsReq();
    expect(req.request.params.get('status')).toBe('OVERDUE');
    req.flush(env(paged<SchoolSubscriptionRow>([])));

    TestBed.resetTestingModule();
    open([school({})], (f) => f.componentRef.setInput('status', 'bogus'));
  });

  it('follows the query string while the page stays open: a card sets the filter, the sidebar link clears it', () => {
    open();
    fixture.componentRef.setInput('status', 'OVERDUE');
    fixture.detectChanges();
    const filtered = schoolsReq();
    expect(filtered.request.params.get('status')).toBe('OVERDUE');
    expect(filtered.request.params.get('page')).toBe('1');
    filtered.flush(env(paged<SchoolSubscriptionRow>([])));
    fixture.componentRef.setInput('status', undefined);
    fixture.detectChanges();
    const cleared = schoolsReq();
    expect(cleared.request.params.has('status')).toBe(false);
    cleared.flush(env(paged<SchoolSubscriptionRow>([])));
  });

  it('searches by name (debounced) and sends the status filter, back on page 1', () => {
    vi.useFakeTimers();
    try {
      open();
      const cmp = fixture.componentInstance as unknown as { searchControl: { setValue(v: string): void }; statusControl: { setValue(v: string): void }; page: { set(v: number): void } };
      cmp.page.set(3);
      cmp.searchControl.setValue('alpha');
      vi.advanceTimersByTime(299);
      cmp.statusControl.setValue('OVERDUE');
      const req = schoolsReq(); // the status change loads at once; the pending search is folded into it
      expect(req.request.params.get('status')).toBe('OVERDUE');
      expect(req.request.params.get('page')).toBe('1');
      req.flush(env(paged<SchoolSubscriptionRow>([])));
      vi.advanceTimersByTime(300);
      const second = schoolsReq();
      expect(second.request.params.get('search')).toBe('alpha');
      second.flush(env(paged<SchoolSubscriptionRow>([])));
    } finally {
      vi.useRealTimers();
    }
  });

  it('says so when the list cannot be loaded', () => {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SubscriptionsPageComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/SuperAdminDashboard/summary`).flush(env(summary));
    schoolsReq().flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(text()).toContain('Could not load schools.');
  });
});
