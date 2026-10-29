import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { SubscriptionInvoice } from '../../models/super-admin-dashboard.model';
import { RecordPaymentModalComponent } from './record-payment-modal.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const invoice = (o: Partial<SubscriptionInvoice>): SubscriptionInvoice => ({
  invoiceId: 1, schoolId: 'S1', schoolName: 'Alpha School', billingPeriodStart: '2026-09-01', billingPeriodEnd: '2026-09-30', billingFrequency: 'MONTHLY',
  studentCount: 3, teacherCount: 2, staffCount: 1, staffBilled: false, billableUserCount: 3, ratePerStudent: 20, discountPercent: 0, grossAmount: 60, discountAmount: 0, netAmount: 60,
  dueDate: '2026-09-01', paidAmount: 25, balanceAmount: 35, status: 'Overdue', ...o,
});

// Deliberately not in billing order, and one already paid: the dialog must sort oldest first and skip the paid one
const INVOICES = [
  invoice({ invoiceId: 2, billingPeriodStart: '2026-10-01', billingPeriodEnd: '2026-10-31', netAmount: 75, paidAmount: 0, balanceAmount: 75, status: 'Pending' }),
  invoice({ invoiceId: 1 }),
  invoice({ invoiceId: 0, billingPeriodStart: '2026-08-01', billingPeriodEnd: '2026-08-31', paidAmount: 60, balanceAmount: 0, status: 'Paid' }),
];

describe('RecordPaymentModalComponent', () => {
  let fixture: ComponentFixture<RecordPaymentModalComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const cmp = () => fixture.componentInstance as unknown as {
    form: { controls: { amount: { setValue(v: number | null): void }; paymentMode: { setValue(v: string): void } } };
    manual: { at(i: number): { setValue(v: number | null): void } };
    setSplit(v: 'auto' | 'manual'): void;
    submit(): void;
  };

  function open(invoiceId: number | null = null, items: SubscriptionInvoice[] = INVOICES): void {
    clearSessions();
    seedSession({ user: makeUser() });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RecordPaymentModalComponent);
    fixture.componentRef.setInput('schoolId', 'S1');
    fixture.componentRef.setInput('schoolName', 'Alpha School');
    fixture.componentRef.setInput('preselectInvoiceId', invoiceId);
    fixture.detectChanges();
    const req = http.expectOne((r) => r.url === `${API}/api/SubscriptionInvoice`);
    expect(req.request.params.get('schoolId')).toBe('S1');
    req.flush(env({ items, total: items.length, page: 1, pageSize: 100 }));
    fixture.detectChanges();
  }

  const post = () => http.expectOne(`${API}/api/SubscriptionPayment`);

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows what the school owes across its unpaid invoices only', () => {
    open();
    expect(text()).toContain('Unpaid invoices: 2');
    expect(text()).toContain('₹110.00 owed in total');
  });

  it('automatic: previews the split oldest invoice first and sends NO allocations', () => {
    open();
    cmp().form.controls.amount.setValue(60);
    cmp().form.controls.paymentMode.setValue('Cash');
    fixture.detectChanges();
    const rows = Array.from(el().querySelectorAll('.split li')).map((li) => (li.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(rows.length).toBe(2);
    expect(rows[0]).toContain('Invoice #1');
    expect(rows[0]).toContain('₹35.00');
    expect(rows[1]).toContain('Invoice #2');
    expect(rows[1]).toContain('₹25.00');

    cmp().submit();
    const req = post();
    expect(req.request.body).toEqual({ schoolId: 'S1', amount: 60, paymentMode: 'Cash', referenceNumber: null, remarks: null, allocations: undefined });
    req.flush(env({ paymentId: 5, schoolId: 'S1', allocations: [{ invoiceId: 1, allocatedAmount: 35 }, { invoiceId: 2, allocatedAmount: 25 }] }));
  });

  it('treats amounts typed into number inputs (strings) as numbers, not text', () => {
    open();
    (cmp().form.controls.amount as { setValue(v: unknown): void }).setValue('60');
    cmp().setSplit('manual');
    cmp().manual.at(0).setValue('35' as unknown as number);
    cmp().manual.at(1).setValue('10' as unknown as number);
    fixture.detectChanges();
    expect(text()).toContain('Payment total ₹45.00');
    cmp().setSplit('auto');
    fixture.detectChanges();
    expect(Array.from(el().querySelectorAll('.split li')).length).toBe(2);
  });

  it('automatic: refuses more than the school owes in total, and sends nothing', () => {
    open();
    cmp().form.controls.amount.setValue(500);
    cmp().form.controls.paymentMode.setValue('Cash');
    cmp().submit();
    fixture.detectChanges();
    expect(text()).toContain('More than the ₹110.00 this school owes in total');
  });

  it('opened from an invoice: manual mode with that invoice settled, and only non-zero amounts are sent', () => {
    open(2);
    expect(el().querySelector('.manual')).not.toBeNull();
    expect(text()).toContain('Payment total ₹75.00');
    cmp().form.controls.paymentMode.setValue('UPI');
    cmp().submit();
    const req = post();
    expect(req.request.body.amount).toBe(75);
    expect(req.request.body.allocations).toEqual([{ invoiceId: 2, amount: 75 }]);
    req.flush(env({ paymentId: 6, schoolId: 'S1', allocations: [{ invoiceId: 2, allocatedAmount: 75 }] }));
  });

  it('manual split over two invoices: the payment total is the sum, in invoice order', () => {
    open();
    cmp().setSplit('manual');
    cmp().manual.at(0).setValue(35); // invoice #1 (oldest)
    cmp().manual.at(1).setValue(10); // invoice #2
    cmp().form.controls.paymentMode.setValue('Bank Transfer');
    fixture.detectChanges();
    expect(text()).toContain('Payment total ₹45.00');
    cmp().submit();
    const req = post();
    expect(req.request.body.amount).toBe(45);
    expect(req.request.body.allocations).toEqual([{ invoiceId: 1, amount: 35 }, { invoiceId: 2, amount: 10 }]);
    req.flush(env({ paymentId: 7, schoolId: 'S1', allocations: [{ invoiceId: 1, allocatedAmount: 35 }, { invoiceId: 2, allocatedAmount: 10 }] }));
  });

  it('manual: refuses an amount above one invoice\'s balance, and a total of zero', () => {
    open();
    cmp().setSplit('manual');
    cmp().form.controls.paymentMode.setValue('Cash');
    cmp().submit();
    fixture.detectChanges();
    expect(text()).toContain('Enter an amount for at least one invoice');
    cmp().manual.at(0).setValue(40); // #1 has only 35 left
    cmp().submit();
    fixture.detectChanges();
    expect(text()).toContain('Invoice #1 has only ₹35.00 left to pay');
  });

  it('says so when the school has nothing unpaid', () => {
    open(null, [invoice({ paidAmount: 60, balanceAmount: 0, status: 'Paid' })]);
    expect(text()).toContain('This school has no unpaid invoices.');
  });
});
