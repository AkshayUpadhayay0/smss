import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { Paged, SchoolSubscriptionRow, SubscriptionInvoice } from '../../models/super-admin-dashboard.model';
import { InvoicesPageComponent } from './invoices-page.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const paged = <T>(items: T[], total = items.length): Paged<T> => ({ items, total, page: 1, pageSize: 10 });

const invoice = (o: Partial<SubscriptionInvoice> = {}): SubscriptionInvoice => ({
  invoiceId: 41, schoolId: 'S2', schoolName: 'Beta School', billingPeriodStart: '2026-10-01', billingPeriodEnd: '2026-10-31', billingFrequency: 'MONTHLY',
  studentCount: 3, teacherCount: 2, staffCount: 1, staffBilled: false, billableUserCount: 3, ratePerStudent: 20, discountPercent: 0,
  grossAmount: 60, discountAmount: 0, netAmount: 60, dueDate: '2026-10-01', paidAmount: 50, balanceAmount: 10, status: 'Overdue', ...o,
});
const schoolRow = (id: string, name: string) => ({ schoolId: id, schoolName: name }) as SchoolSubscriptionRow;

describe('InvoicesPageComponent', () => {
  let fixture: ComponentFixture<InvoicesPageComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const invoicesReq = () => http.expectOne((r) => r.url === `${API}/api/SubscriptionInvoice`);
  const schoolsReq = () => http.expectOne((r) => r.url === `${API}/api/SuperAdminDashboard/schools`);

  function open(invoices: SubscriptionInvoice[] = [invoice()], inputs: Record<string, string> = {}): void {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(InvoicesPageComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    const schools = schoolsReq(); // the school filter's options
    expect(schools.request.params.get('pageSize')).toBe('100');
    schools.flush(env(paged([schoolRow('S1', 'Alpha School'), schoolRow('S2', 'Beta School')])));
    invoicesReq().flush(env(paged(invoices)));
    fixture.detectChanges();
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('has its own page header and the invoices table only (no schools table)', () => {
    open();
    expect(el().querySelector('h1')?.textContent?.trim()).toBe('Invoices');
    expect(el().querySelectorAll('app-table').length).toBe(1);
    const headers = Array.from(el().querySelectorAll('app-table thead th')).map((h) => h.textContent?.trim());
    expect(headers).toEqual(['Invoice', 'School', 'Period', 'Billed users', 'Amount', 'Paid', 'Balance', 'Due', 'Status', 'Actions']);
    expect(text()).not.toContain('Generate invoice');
    expect(text()).not.toContain('Configure billing');
  });

  it('lists each invoice with its derived status and balance', () => {
    open();
    expect(text()).toContain('Beta School');
    expect(text()).toContain('Overdue');
    expect(text()).toContain('₹10.00');
    expect(text()).toContain('01 Oct 2026 – 31 Oct 2026');
  });

  it('shows Record payment only while an invoice has a balance', () => {
    open([invoice(), invoice({ invoiceId: 42, status: 'Paid', paidAmount: 60, balanceAmount: 0 })]);
    const rows = Array.from(el().querySelectorAll('app-table tbody tr'));
    expect(rows[0].textContent).toContain('Record payment');
    expect(rows[1].textContent).not.toContain('Record payment');
  });

  it('sends the status filter and goes back to page 1', () => {
    open();
    const cmp = fixture.componentInstance as unknown as { statusControl: { setValue(v: string): void }; page: { set(v: number): void } };
    cmp.page.set(3);
    cmp.statusControl.setValue('Overdue');
    const req = invoicesReq();
    expect(req.request.params.get('status')).toBe('Overdue');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(env(paged([invoice()])));
  });

  it('sends the school filter, and can go back to all schools', () => {
    open();
    const cmp = fixture.componentInstance as unknown as { schoolControl: { setValue(v: string): void } };
    cmp.schoolControl.setValue('S2');
    const req = invoicesReq();
    expect(req.request.params.get('schoolId')).toBe('S2');
    req.flush(env(paged([invoice()])));
    cmp.schoolControl.setValue('');
    expect(invoicesReq().request.params.has('schoolId')).toBe(false);
  });

  it('opened from another page: ?schoolId and ?status pre-select both filters, and the first request already carries them', () => {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(InvoicesPageComponent);
    fixture.componentRef.setInput('schoolId', 'S2');
    fixture.componentRef.setInput('schoolName', 'Beta School');
    fixture.componentRef.setInput('status', 'Pending');
    fixture.detectChanges();
    schoolsReq().flush(env(paged([schoolRow('S1', 'Alpha School'), schoolRow('S2', 'Beta School')])));
    const req = invoicesReq();
    expect(req.request.params.get('schoolId')).toBe('S2');
    expect(req.request.params.get('status')).toBe('Pending');
    req.flush(env(paged([invoice({ status: 'Pending' })])));
    fixture.detectChanges();
    expect(text()).toContain('Beta School'); // the school filter shows it
  });

  it('a school opened from another page stays selectable even when it is not among the first 100 schools', () => {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(InvoicesPageComponent);
    fixture.componentRef.setInput('schoolId', 'S999');
    fixture.componentRef.setInput('schoolName', 'Far Away School');
    fixture.detectChanges();
    schoolsReq().flush(env(paged([schoolRow('S1', 'Alpha School')])));
    invoicesReq().flush(env(paged<SubscriptionInvoice>([])));
    fixture.detectChanges();
    const options = (fixture.componentInstance as unknown as { schoolOptions(): { value: string; label: string }[] }).schoolOptions();
    expect(options.map((o) => o.value)).toEqual(['', 'S1', 'S999']);
    expect(options[2].label).toBe('Far Away School');
  });

  it('follows the query string while the page stays open: one reload with both filters, and the sidebar link clears them', () => {
    open();
    fixture.componentRef.setInput('schoolId', 'S2');
    fixture.componentRef.setInput('schoolName', 'Beta School');
    fixture.componentRef.setInput('status', 'Overdue');
    fixture.detectChanges();
    const filtered = invoicesReq(); // a single request, not one per input
    expect(filtered.request.params.get('schoolId')).toBe('S2');
    expect(filtered.request.params.get('status')).toBe('Overdue');
    filtered.flush(env(paged([invoice()])));
    fixture.componentRef.setInput('schoolId', undefined);
    fixture.componentRef.setInput('schoolName', undefined);
    fixture.componentRef.setInput('status', undefined);
    fixture.detectChanges();
    const cleared = invoicesReq();
    expect(cleared.request.params.has('schoolId')).toBe(false);
    expect(cleared.request.params.has('status')).toBe(false);
    cleared.flush(env(paged([invoice()])));
  });

  it('an unknown ?status= is ignored', () => {
    open([invoice()], { status: 'late' });
  });

  it('opens the invoice dialog and loads that invoice with its payments', () => {
    open();
    (el().querySelector('app-table app-button[aria-label="View invoice"] button') as HTMLButtonElement).click();
    fixture.detectChanges();
    http.expectOne(`${API}/api/SubscriptionInvoice/41`).flush(env(invoice({
      payments: [{ paymentId: 9, schoolId: 'S2', paymentDate: '2026-10-05T10:00:00Z', amount: 50, allocations: [{ invoiceId: 41, allocatedAmount: 50 }], paymentMode: 'UPI', referenceNumber: 'UTR1', remarks: null, status: 'Active', voidedAt: null, voidReason: null }],
    })));
    fixture.detectChanges();
    expect(text()).toContain('Invoice #41');
    expect(text()).toContain('Billed users');
    expect(text()).toContain('Not billed');
    expect(text()).toContain('Only students are billed for this school.');
    expect(text()).toContain('UTR1');
  });

  it('Record payment opens the payment dialog for that invoice\'s school and loads its unpaid invoices', () => {
    open();
    (Array.from(el().querySelectorAll('app-table button')).find((b) => b.textContent?.trim() === 'Record payment') as HTMLButtonElement).click();
    fixture.detectChanges();
    const req = invoicesReq();
    expect(req.request.params.get('schoolId')).toBe('S2');
    req.flush(env(paged([invoice()])));
    fixture.detectChanges();
    expect(text()).toContain('Record payment: Beta School');
    expect(text()).toContain('Unpaid invoices: 1');
  });
});
