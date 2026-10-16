import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { FeeReceipt } from '../../models/fee-payment.model';
import { StudentFeeDue } from '../../models/student-billing.model';
import { CollectPaymentDialogComponent } from './collect-payment-dialog.component';

const API = environment.apiUrl;
const URL = `${API}/api/FeePayment/students/DPS001ST001`;
const env = <T>(data: T, statusCode = 201) => ({ status: true, statusCode, message: 'ok', data });

const due = (id: number, dueDate: string, amountDue: number, amountPaid = 0, status: StudentFeeDue['status'] = 'Overdue'): StudentFeeDue => ({
  feeDueId: id, studentFeeItemId: 1, feeTypeName: 'Tuition', frequencyName: 'Monthly', dueDate, amountDue, amountPaid, outstanding: amountDue - amountPaid, status,
});
// deliberately NOT in date order: the dialog must list oldest first by itself
const DUES: StudentFeeDue[] = [
  due(3, '2026-06-10', 2500),
  due(1, '2026-04-10', 2500, 2500, 'Paid'),   // fully paid: not offered
  due(2, '2026-05-10', 2500, 1000),
  due(4, '2026-11-10', 2500, 0, 'Pending'),
];
const RECEIPT = { receiptNumber: 'RCP-2026-0001' } as FeeReceipt;

describe('CollectPaymentDialogComponent', () => {
  let fixture: ComponentFixture<CollectPaymentDialogComponent>;
  let http: HttpTestingController;
  let recorded: FeeReceipt[];
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';
  const rows = () => cmp()['rows']() as { dueId: number; checked: boolean; amount: string; outstanding: number }[];
  const submitButton = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((b) => b.textContent?.includes('Record payment')) as HTMLButtonElement;

  function amount(value: string) {
    cmp()['amountControl'].setValue(value);
    fixture.detectChanges();
  }

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CollectPaymentDialogComponent);
    fixture.componentRef.setInput('studentId', 'DPS001ST001');
    fixture.componentRef.setInput('dues', DUES);
    recorded = [];
    fixture.componentInstance.recorded.subscribe((r) => recorded.push(r));
    fixture.detectChanges();
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('lists only installments with a balance, oldest first, each defaulting to its remaining balance', () => {
    expect(rows().map((r) => r.dueId)).toEqual([2, 3, 4]);
    expect(rows().map((r) => r.amount)).toEqual(['1500.00', '2500.00', '2500.00']);
    expect(rows().some((r) => r.checked)).toBe(false);
    expect(submitButton().disabled).toBe(true);
  });

  it('typing an amount splits it oldest-first and the running total matches', () => {
    amount('5000');
    expect(rows().map((r) => [r.checked, r.amount])).toEqual([[true, '1500.00'], [true, '2500.00'], [true, '1000.00']]);
    expect(cmp()['allocatedPaise']()).toBe(500000);
    expect(cmp()['difference']()).toBe(0);
    expect(submitButton().disabled).toBe(false);
    expect(text()).toContain('Matches');
  });

  it('submit is disabled until the split equals the amount: editing one allocation breaks it, fixing it restores it', () => {
    amount('3000');
    expect(submitButton().disabled).toBe(false);

    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('.dues li:nth-child(2) input[type="number"]')!;
    input.value = '1000';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(cmp()['difference']()).toBe(50000);   // 500 still to apply
    expect(submitButton().disabled).toBe(true);
    expect(text()).toContain('still to apply');

    input.value = '1500';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(cmp()['difference']()).toBe(0);
    expect(submitButton().disabled).toBe(false);
  });

  it('an allocation above what an installment still owes is an error and blocks submit', () => {
    amount('2000');
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('.dues li:nth-child(1) input[type="number"]')!;
    input.value = '2000';   // installment 2 has only 1,500 left
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(text()).toContain('is outstanding');
    expect(submitButton().disabled).toBe(true);
  });

  it('once the user edits the split, a new amount no longer re-splits it, until "Allocate oldest first again"', () => {
    amount('1500');
    const box = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('.dues li input[type="checkbox"]')[1];
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(cmp()['manual']()).toBe(true);

    amount('4000');
    expect(rows().map((r) => r.checked)).toEqual([true, true, false]);   // the manual choice was kept
    cmp()['allocateAgain']();
    fixture.detectChanges();
    expect(rows().map((r) => r.amount)).toEqual(['1500.00', '2500.00', '2500.00']);
    expect(cmp()['allocatedPaise']()).toBe(400000);
  });

  it('an amount above everything outstanding is refused', () => {
    amount('6501');   // 1,500 + 2,500 + 2,500 = 6,500
    expect(text()).toContain('More than the');
    expect(submitButton().disabled).toBe(true);
  });

  it('"Pay everything outstanding" fills the amount and ticks every installment', () => {
    cmp()['payEverything']();
    fixture.detectChanges();
    expect(cmp()['amountControl'].value).toBe('6500.00');
    expect(rows().every((r) => r.checked)).toBe(true);
    expect(submitButton().disabled).toBe(false);
  });

  it('sends an explicit split of the ticked installments only, with mode and remarks, and emits the receipt', () => {
    cmp()['modeControl'].setValue('UPI');
    cmp()['remarksControl'].setValue('  ref 123 ');
    amount('4000');
    cmp()['submit']();

    const req = http.expectOne(URL);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      amount: 4000,
      paymentMode: 'UPI',
      allocations: [{ feeDueId: 2, amount: 1500 }, { feeDueId: 3, amount: 2500 }],
      remarks: 'ref 123',
    });
    req.flush(env(RECEIPT));
    expect(recorded).toEqual([RECEIPT]);
  });

  it('floating point never gets in the way: 0.1 + 0.2 style amounts still match exactly', () => {
    amount('0.30');
    cmp()['rows'].update((r: { dueId: number }[]) => r.map((x) => (x.dueId === 2 ? { ...x, checked: true, amount: '0.10' } : x.dueId === 3 ? { ...x, checked: true, amount: '0.20' } : x)));
    fixture.detectChanges();
    expect(cmp()['difference']()).toBe(0);
    expect(submitButton().disabled).toBe(false);
  });

  it('shows the server\'s refusal and stays open (nothing emitted)', () => {
    amount('1500');
    cmp()['submit']();
    http.expectOne(URL).flush({ status: false, statusCode: 400, message: 'The installment due 10 May 2026 has only 0.00 outstanding', data: null }, { status: 400, statusText: 'Bad Request' });
    expect(recorded).toEqual([]);
    expect(cmp()['saving']()).toBe(false);
  });
});
