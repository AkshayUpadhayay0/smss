import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { TaxType } from '../../../fee-setup/models/tax-type.model';
import { ConcessionChoice, FeeReceipt } from '../../models/fee-payment.model';
import { StudentFeeDue } from '../../models/student-billing.model';
import { CollectPaymentDialogComponent } from './collect-payment-dialog.component';

const API = environment.apiUrl;
const URL = `${API}/api/FeePayment/students/DPS001ST001`;
const env = <T>(data: T, statusCode = 201) => ({ status: true, statusCode, message: 'ok', data });

const due = (id: number, dueDate: string, amountDue: number, amountPaid = 0, status: StudentFeeDue['status'] = 'Overdue'): StudentFeeDue => ({
  feeDueId: id, studentFeeItemId: 1, feeTypeId: 1, feeTypeName: 'Tuition', frequencyName: 'Monthly', dueDate, amountDue, amountPaid, outstanding: amountDue - amountPaid, status,
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

  function flushTaxTypes(list: TaxType[], concessions: ConcessionChoice[] = []) {
    http.expectOne((r) => r.method === 'GET' && r.url === `${API}/api/TaxType`).flush(env(list, 200));
    http.expectOne((r) => r.method === 'GET' && r.url === `${API}/api/FeePayment/concessions`).flush(env(concessions, 200));   // the school's active concessions
    fixture.detectChanges();
  }

  function amount(value: string) {
    cmp()['amountControl'].setValue(value);
    fixture.detectChanges();
  }

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CollectPaymentDialogComponent);
    fixture.componentRef.setInput('studentId', 'DPS001ST001');
    fixture.componentRef.setInput('dues', DUES);
    recorded = [];
    fixture.componentInstance.recorded.subscribe((r) => recorded.push(r));
    fixture.detectChanges();
    flushTaxTypes([]);   // the dialog asks for the school's tax types and concessions when it opens; none by default
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

  describe('tax types', () => {
    const tax = (id: number, name: string, rate: number, isActive = true): TaxType => ({ taxTypeId: id, taxName: name, taxCode: name.toUpperCase(), ratePercent: rate, isActive, createdAt: '', updatedAt: '' });
    const field = (label: string) => [...(fixture.nativeElement as HTMLElement).querySelectorAll('.tax-summary div')].find((d) => d.textContent?.includes(label))?.textContent ?? '';

    /** Rebuild the dialog with a school that has tax types (the outer beforeEach already opened one without). */
    function openWith(list: TaxType[], roles: string[] = ['School Admin'], concessions: ConcessionChoice[] = []) {
      http.verify();
      TestBed.resetTestingModule();   // AuthService caches the roles it was built with
      clearSessions();
      seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles }) });
      TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
      http = TestBed.inject(HttpTestingController);
      fixture = TestBed.createComponent(CollectPaymentDialogComponent);
      fixture.componentRef.setInput('studentId', 'DPS001ST001');
      fixture.componentRef.setInput('dues', DUES);
      fixture.detectChanges();
      flushTaxTypes(list, concessions);
    }

    it('the amount field says what it is: the amount applied to installments', () => {
      expect(text()).toContain('Amount applied to installments');
    });

    it('no tax types: the Tax type field is hidden and a School Admin gets a hint to add one; an Accountant sees nothing', () => {
      expect((fixture.nativeElement as HTMLElement).querySelector('app-select[label="Tax type"]')).toBeNull();
      expect(text()).toContain('No tax types yet');
      openWith([], ['Accountant']);
      expect(text()).not.toContain('No tax types yet');
      expect(text()).not.toContain('Tax type');
    });

    it('offers None by default plus only ACTIVE types labelled like "GST (5%)", and shows no summary until one is chosen', () => {
      openWith([tax(1, 'GST', 5), tax(2, 'Old', 3, false)]);
      expect(cmp()['taxOptions']().map((o: { label: string }) => o.label)).toEqual(['None', 'GST (5%)']);
      expect(cmp()['taxControl'].value).toBe('');
      expect((fixture.nativeElement as HTMLElement).querySelector('.tax-summary')).toBeNull();
    });

    it('the rate is not repeated when the name already carries it: "GST 18%" alone, "Service Tax (5%)" with its rate', () => {
      openWith([tax(1, 'GST 18%', 18), tax(2, 'Service Tax', 5)]);
      expect(cmp()['taxOptions']().map((o: { label: string }) => o.label)).toEqual(['None', 'GST 18%', 'Service Tax (5%)']);
      cmp()['taxControl'].setValue('1');
      amount('1000');
      expect(field('Tax: GST 18%')).toContain('180.00');
      expect((fixture.nativeElement as HTMLElement).querySelector('.tax-summary')?.textContent).not.toContain('GST 18% (18%)');
      cmp()['taxControl'].setValue('2');
      fixture.detectChanges();
      expect(field('Tax: Service Tax (5%)')).toContain('50.00');
    });

    it('the live summary shows fee, tax (name and rate) and total, and follows the amount', () => {
      openWith([tax(1, 'GST', 18)]);
      cmp()['taxControl'].setValue('1');
      amount('1000');
      expect(field('Fee amount')).toContain('1,000.00');
      expect(field('Tax: GST (18%)')).toContain('180.00');
      expect(field('Total to collect')).toContain('1,180.00');
      amount('333.33');
      cmp()['taxControl'].setValue('1');
      fixture.detectChanges();
      expect(cmp()['taxAmountPaise']()).toBe(6000);    // 59.9994 -> 60.00
      expect(cmp()['totalToCollectPaise']()).toBe(39333);
    });

    it('rounds half up like the server: 333.33 x 5% = 16.67, 0.01 x 5% = 0.00, rate 0 = 0.00', () => {
      openWith([tax(1, 'Five', 5), tax(2, 'Zero', 0)]);
      cmp()['taxControl'].setValue('1');
      amount('333.33');
      expect(cmp()['taxAmountPaise']()).toBe(1667);
      amount('0.01');
      expect(cmp()['taxAmountPaise']()).toBe(0);
      cmp()['taxControl'].setValue('2');
      amount('1000');
      expect(cmp()['taxAmountPaise']()).toBe(0);
    });

    it('the allocation split still equals the FEE amount, never the total with tax', () => {
      openWith([tax(1, 'GST', 18)]);
      cmp()['taxControl'].setValue('1');
      amount('1000');
      expect(cmp()['allocatedPaise']()).toBe(100000);
      expect(cmp()['difference']()).toBe(0);
    });

    it('sends the chosen taxTypeId and the FEE amount, never a tax amount; with None it sends no taxTypeId at all', () => {
      openWith([tax(7, 'GST', 18)]);
      cmp()['taxControl'].setValue('7');
      amount('1500');
      cmp()['submit']();
      const req = http.expectOne(URL);
      expect(req.request.body.amount).toBe(1500);
      expect(req.request.body.taxTypeId).toBe(7);
      expect(Object.keys(req.request.body)).not.toContain('taxAmount');
      req.flush(env(RECEIPT));

      openWith([tax(7, 'GST', 18)]);
      amount('1500');
      cmp()['submit']();
      const none = http.expectOne(URL);
      expect('taxTypeId' in none.request.body).toBe(false);
      none.flush(env(RECEIPT));
    });

    // ------------------------------------------------------------ concessions
    const con = (id: number, name: string, type: 'PERCENTAGE' | 'FIXED', value: number, applicableFeeTypeId: number | null = null, applicableFeeTypeName: string | null = null): ConcessionChoice =>
      ({ concessionId: id, concessionName: name, concessionCode: name.slice(0, 4).toUpperCase(), concessionType: type, value, applicableFeeTypeId, applicableFeeTypeName });
    const reasonBox = () => (fixture.nativeElement as HTMLElement).querySelector('app-textarea[label="Reason for the concession"]');
    const pick = (id: string) => { cmp()['concessionControl'].setValue(id); fixture.detectChanges(); };
    const why = (v: string) => { cmp()['reasonControl'].setValue(v); fixture.detectChanges(); };

    it('no concessions: the Concession field is not shown', () => {
      openWith([], ['School Admin'], []);
      expect((fixture.nativeElement as HTMLElement).querySelector('app-select[label="Concession"]')).toBeNull();
    });

    it('offers None by default plus the active concessions, the value shown only when the name does not already state it', () => {
      openWith([], ['Accountant'], [con(1, 'Sibling 10%', 'PERCENTAGE', 10), con(2, 'Staff ward', 'FIXED', 500), con(3, 'Staff Rs 500', 'FIXED', 500), con(4, 'Merit', 'PERCENTAGE', 5)]);
      expect(cmp()['concessionOptions']().map((o: { label: string }) => o.label)).toEqual(['None', 'Sibling 10%', 'Staff ward (₹500)', 'Staff Rs 500', 'Merit (5%)']);
      expect(cmp()['concessionControl'].value).toBe('');
      expect(reasonBox()).toBeNull();                                  // no reason box until one is chosen
      expect((fixture.nativeElement as HTMLElement).querySelector('.tax-summary')).toBeNull();
    });

    it('choosing a concession asks for a reason; Record payment stays disabled until a non-blank reason is written', () => {
      openWith([], ['School Admin'], [con(1, 'Sibling 10%', 'PERCENTAGE', 10)]);
      amount('1000');
      expect(submitButton().disabled).toBe(false);
      pick('1');
      expect(reasonBox()).not.toBeNull();
      expect(submitButton().disabled).toBe(true);
      why('   ');
      expect(submitButton().disabled).toBe(true);                      // spaces are not a reason
      why('second child in the school');
      expect(submitButton().disabled).toBe(false);
      pick('');                                                        // back to None: the reason box and the rule go away
      expect(reasonBox()).toBeNull();
      expect(submitButton().disabled).toBe(false);
    });

    it('the worked example: 1000 less a 10% concession is 900, GST 18% on it is 162, so 1062 to collect', () => {
      openWith([tax(1, 'GST 18%', 18)], ['School Admin'], [con(1, 'Sibling 10%', 'PERCENTAGE', 10)]);
      amount('1000');
      pick('1');
      cmp()['taxControl'].setValue('1');
      fixture.detectChanges();
      expect(field('Fee amount')).toContain('1,000.00');
      expect(field('Concession: Sibling 10%')).toContain('− ₹100.00');
      expect(field('Net fee')).toContain('900.00');
      expect(field('Tax: GST 18%')).toContain('162.00');               // tax on the NET fee: discount first, then tax
      expect(field('Total to collect')).toContain('1,062.00');
      // the split of the money is still over the installments, by the GROSS amount
      expect(cmp()['allocatedPaise']()).toBe(100000);
      expect(cmp()['difference']()).toBe(0);
    });

    it('rounds half up like the server: 333.33 less 10% is 33.33 off, 300.00 net, 54.00 tax, 354.00 total; a fixed concession is applied once', () => {
      openWith([tax(1, 'GST 18%', 18)], ['School Admin'], [con(1, 'Sibling 10%', 'PERCENTAGE', 10), con(2, 'Rs 100 off', 'FIXED', 100)]);
      amount('333.33');
      pick('1');
      cmp()['taxControl'].setValue('1');
      fixture.detectChanges();
      expect(cmp()['concessionAmountPaise']()).toBe(3333);
      expect(cmp()['netFeePaise']()).toBe(30000);
      expect(cmp()['taxAmountPaise']()).toBe(5400);
      expect(cmp()['totalToCollectPaise']()).toBe(35400);
      amount('2500');
      pick('2');
      expect(cmp()['concessionAmountPaise']()).toBe(10000);            // 100 once, however many installments the amount spans
    });

    it('a concession for one fee type discounts only the ticked installments of that type; none of that type = inline message and a disabled button', () => {
      openWith([], ['School Admin'], [con(1, 'Exam 10%', 'PERCENTAGE', 10, 2, 'Exam Fee')]);
      amount('2000');                                                  // due 2 (1500) + due 3 (500), both fee type 1
      pick('1');
      why('exam concession');
      expect(cmp()['concessionBasePaise']()).toBe(0);
      expect(text()).toContain("This concession doesn't apply to the selected installments");
      expect(text()).toContain('Exam Fee');
      expect(submitButton().disabled).toBe(true);
      // make due 3 an Exam Fee installment: now only its 500 is the base
      cmp()['rows'].update((r: { dueId: number; feeTypeId: number | null }[]) => r.map((x) => (x.dueId === 3 ? { ...x, feeTypeId: 2 } : x)));
      fixture.detectChanges();
      expect(cmp()['concessionBasePaise']()).toBe(50000);
      expect(cmp()['concessionAmountPaise']()).toBe(5000);
      expect(text()).not.toContain("doesn't apply");
      expect(submitButton().disabled).toBe(false);
    });

    it('a concession that would leave nothing to collect is refused inline', () => {
      openWith([], ['School Admin'], [con(1, 'Everything', 'PERCENTAGE', 100)]);
      amount('1000');
      pick('1');
      why('full waiver');
      expect(cmp()['netFeePaise']()).toBe(0);
      expect(text()).toContain('nothing would be left to collect');
      expect(submitButton().disabled).toBe(true);
    });

    it('sends the concession id and the reason (trimmed) with the GROSS amount and the split, never a discount, tax or total amount', () => {
      openWith([tax(1, 'GST 18%', 18)], ['School Admin'], [con(7, 'Sibling 10%', 'PERCENTAGE', 10)]);
      amount('1000');
      pick('7');
      why('  second child  ');
      cmp()['taxControl'].setValue('1');
      fixture.detectChanges();
      submitButton().click();
      const req = http.expectOne(URL);
      expect(req.request.body.amount).toBe(1000);
      expect(req.request.body.concessionId).toBe(7);
      expect(req.request.body.concessionReason).toBe('second child');
      expect(req.request.body.taxTypeId).toBe(1);
      expect(req.request.body.allocations.reduce((s: number, a: { amount: number }) => s + a.amount, 0)).toBe(1000);
      for (const forbidden of ['concessionAmount', 'discountAmount', 'discount', 'taxAmount', 'totalAmount', 'netFeeAmount', 'grossAmount']) expect(req.request.body).not.toHaveProperty(forbidden);
      req.flush(env(RECEIPT));
    });

    it('with None the request carries no concession fields at all, and a typed reason is not sent', () => {
      openWith([], ['School Admin'], [con(7, 'Sibling 10%', 'PERCENTAGE', 10)]);
      amount('1000');
      pick('7');
      why('typed, then changed my mind');
      pick('');
      submitButton().click();
      const req = http.expectOne(URL);
      expect(req.request.body).not.toHaveProperty('concessionId');
      expect(req.request.body).not.toHaveProperty('concessionReason');
      req.flush(env(RECEIPT));
    });

  });

  it('shows the server\'s refusal and stays open (nothing emitted)', () => {
    amount('1500');
    cmp()['submit']();
    http.expectOne(URL).flush({ status: false, statusCode: 400, message: 'The installment due 10 May 2026 has only 0.00 outstanding', data: null }, { status: 400, statusText: 'Bad Request' });
    expect(recorded).toEqual([]);
    expect(cmp()['saving']()).toBe(false);
  });
});
