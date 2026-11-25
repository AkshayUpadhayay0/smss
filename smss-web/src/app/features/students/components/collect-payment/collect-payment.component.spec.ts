import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { TaxType } from '../../../fee-setup/models/tax-type.model';
import { ConcessionChoice, FeeReceipt } from '../../models/fee-payment.model';
import { StudentFeeDue } from '../../models/student-billing.model';
import { CollectPaymentDialogComponent } from './collect-payment.component';

const API = environment.apiUrl;
const URL = `${API}/api/FeePayment/students/DPS001ST001`;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

const TUITION = 1;
const EXAM = 2;
const due = (id: number, dueDate: string, amountDue: number, amountPaid = 0, feeTypeId = TUITION, status: StudentFeeDue['status'] = 'Overdue'): StudentFeeDue => ({
  feeDueId: id, studentFeeItemId: 1, feeTypeId, feeTypeName: feeTypeId === TUITION ? 'Tuition Fee' : 'Exam Fee', frequencyName: 'Monthly', dueDate, amountDue, amountPaid, outstanding: amountDue - amountPaid, status,
});
// deliberately NOT in date order: the dialog must list oldest first by itself; id 1 is fully paid and never offered
const DUES: StudentFeeDue[] = [
  due(3, '2026-06-10', 2500),
  due(1, '2026-04-10', 2500, 2500, TUITION, 'Paid'),
  due(2, '2026-05-10', 2500, 1000),
  due(4, '2026-11-10', 800, 0, EXAM, 'Pending'),
];
const tax = (id: number, name: string, rate: number, isActive = true): TaxType => ({ taxTypeId: id, taxName: name, taxCode: name.toUpperCase(), ratePercent: rate, isActive, createdAt: '', updatedAt: '' });
const con = (id: number, name: string, type: 'PERCENTAGE' | 'FIXED', value: number, feeTypeId: number | null = null, feeTypeName: string | null = null): ConcessionChoice =>
  ({ concessionId: id, concessionName: name, concessionCode: name.slice(0, 4).toUpperCase(), concessionType: type, value, applicableFeeTypeId: feeTypeId, applicableFeeTypeName: feeTypeName });
const RECEIPT = (total: number) => ({ receiptNumber: 'RCP-2026-0001', amount: total, totalAmount: total }) as FeeReceipt;

describe('CollectPaymentDialogComponent', () => {
  let fixture: ComponentFixture<CollectPaymentDialogComponent>;
  let http: HttpTestingController;
  let recorded: FeeReceipt[];
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const tick = () => { fixture.detectChanges(); fixture.detectChanges(); };
  const rows = () => cmp()['rows']() as { dueId: number; checked: boolean; amount: string; outstanding: number; feeTypeId: number | null }[];
  const submitButton = () => [...el().querySelectorAll('button')].find((b) => /Record payment|Recording/.test(b.textContent ?? '')) as HTMLButtonElement;
  const summary = () => (el().querySelector('.summary')?.textContent ?? '').replace(/\s+/g, ' ');
  const line = (label: string) => ([...el().querySelectorAll('.summary div')].find((d) => d.textContent?.includes(label))?.textContent ?? '').replace(/\s+/g, ' ');
  const check = (id: number, on = true) => { const cb = el().querySelector<HTMLInputElement>(`.dues li:nth-child(${rows().findIndex((r) => r.dueId === id) + 1}) input[type=checkbox]`)!; cb.checked = on; cb.dispatchEvent(new Event('change')); tick(); };
  const setRowAmount = (id: number, v: string) => { const i = el().querySelector<HTMLInputElement>(`.dues li:nth-child(${rows().findIndex((r) => r.dueId === id) + 1}) .amt input`)!; i.value = v; i.dispatchEvent(new Event('input')); tick(); };
  const pickConcession = (id: string) => { cmp()['concessionControl'].setValue(id); tick(); };
  const why = (v: string) => { cmp()['reasonControl'].setValue(v); tick(); };
  const pickTax = (id: string) => { cmp()['taxControl'].setValue(id); tick(); };
  const setMode = (m: string) => { cmp()['modeControl'].setValue(m); tick(); };
  const typeReceived = (v: string) => { const i = el().querySelector<HTMLInputElement>('#cp-received')!; i.value = v; i.dispatchEvent(new Event('input')); tick(); };

  function open(opts: { taxes?: TaxType[]; concessions?: ConcessionChoice[]; roles?: string[]; lateRule?: unknown } = {}) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: opts.roles ?? ['School Admin'] }) });
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CollectPaymentDialogComponent);
    fixture.componentRef.setInput('studentId', 'DPS001ST001');
    fixture.componentRef.setInput('dues', DUES);
    recorded = [];
    fixture.componentInstance.recorded.subscribe((r) => recorded.push(r));
    fixture.detectChanges();
    http.expectOne((r) => r.method === 'GET' && r.url === `${API}/api/TaxType`).flush(env(opts.taxes ?? []));
    http.expectOne((r) => r.method === 'GET' && r.url === `${API}/api/FeePayment/concessions`).flush(env(opts.concessions ?? []));
    http.expectOne((r) => r.method === 'GET' && r.url === `${API}/api/LateFee/rule`).flush(env(opts.lateRule ?? { enabled: false, ruleType: null, amount: null, percent: null, graceDays: 0, capPerDue: null, taxApplies: false, updatedAt: null, updatedBy: null, feeTypes: [] }));
    tick();
  }
  afterEach(() => { http.verify(); clearSessions(); vi.restoreAllMocks(); });

  // ============================================================== order and the empty state
  it('is laid out in order: installments, concession, tax type, then (pinned) the summary, payment mode and amount received', () => {
    open({ taxes: [tax(1, 'GST 18%', 18)], concessions: [con(1, 'Sibling 10%', 'PERCENTAGE', 10)] });
    const pos = (sel: string) => el().querySelector(sel)!;
    const before = (a: Element, b: Element) => !!(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
    expect(before(pos('#cp-installments'), pos('#cp-concession'))).toBe(true);
    expect(before(pos('#cp-concession'), pos('#cp-tax'))).toBe(true);
    expect(before(pos('#cp-tax'), pos('.summary'))).toBe(true);
    expect(before(pos('.summary'), pos('.pay'))).toBe(true);
    expect(before(pos('.pay'), pos('.actions'))).toBe(true);
    // the summary, mode, received and buttons live in the modal footer (pinned), not in the scrolling body
    expect(el().querySelector('.footer .summary')).not.toBeNull();
    expect(el().querySelector('.footer .actions')).not.toBeNull();
    expect(el().querySelector('.body .summary')).toBeNull();
    expect(text()).toContain('Installments being paid');
  });

  it('lists only installments with a balance, oldest first, each defaulting to its full outstanding, none ticked', () => {
    open();
    expect(rows().map((r) => r.dueId)).toEqual([2, 3, 4]);
    expect(rows().map((r) => r.amount)).toEqual(['1500.00', '2500.00', '800.00']);
  });

  it('with nothing selected: concession and tax are disabled, every figure is ₹0.00 (never negative or NaN) and Record payment is disabled', () => {
    open({ taxes: [tax(1, 'GST 18%', 18)], concessions: [con(1, 'Sibling 10%', 'PERCENTAGE', 10)] });
    expect(cmp()['concessionControl'].disabled).toBe(true);
    expect(cmp()['taxControl'].disabled).toBe(true);
    expect(el().querySelector<HTMLSelectElement>('app-select[label="Concession"] select')!.disabled).toBe(true);
    expect(text()).toContain('Select an installment first');
    expect(line('Fee amount')).toContain('₹0.00');
    expect(line('Total to collect')).toContain('₹0.00');
    expect(summary()).not.toMatch(/NaN|-\s?₹|₹-/);
    expect(submitButton().disabled).toBe(true);
  });

  it('the regression: a concession cannot be chosen before an installment is, so the net fee and total are never negative', () => {
    open({ concessions: [con(1, 'Staff Rs 700', 'FIXED', 700)] });
    pickConcession('1');                                              // even forced through the control, it is dropped at once
    expect(cmp()['selectedConcession']()).toBeNull();
    expect(cmp()['preview']().netPaise).toBe(0);
    expect(summary()).not.toMatch(/NaN|-\s?₹|₹-/);
    expect(text()).toContain('no installment is selected');
  });

  // ============================================================== installments
  it('ticking a line starts it at its full outstanding; the controls unlock; Select all / Clear work', () => {
    open({ taxes: [tax(1, 'GST 18%', 18)], concessions: [con(1, 'Sibling 10%', 'PERCENTAGE', 10)] });
    check(2);
    expect(rows().find((r) => r.dueId === 2)).toMatchObject({ checked: true, amount: '1500.00' });
    expect(cmp()['concessionControl'].enabled).toBe(true);
    expect(cmp()['taxControl'].enabled).toBe(true);
    expect(line('Fee amount')).toContain('₹1,500.00');
    (cmp() as any)['selectAll']();
    tick();
    expect(rows().every((r) => r.checked)).toBe(true);
    expect(line('Fee amount')).toContain('₹4,800.00');                // 1500 + 2500 + 800
    (cmp() as any)['clear']();
    tick();
    expect(rows().some((r) => r.checked)).toBe(false);
    expect(cmp()['concessionControl'].disabled).toBe(true);
    expect(line('Fee amount')).toContain('₹0.00');
  });

  it('a line amount is never above what is outstanding nor below ₹0.01; an invalid line adds nothing and blocks Record payment', () => {
    open();
    check(2);
    setRowAmount(2, '1500.01');
    expect(text()).toContain('At most ₹1,500.00');
    expect(line('Fee amount')).toContain('₹0.00');
    expect(submitButton().disabled).toBe(true);
    setRowAmount(2, '0');
    expect(text()).toContain('At least ₹0.01');
    setRowAmount(2, '-5');
    expect(cmp()['anyRowError']()).toBe(true);
    expect(line('Fee amount')).toContain('₹0.00');
    setRowAmount(2, '10.005');
    expect(text()).toContain('at most 2 decimal');
    setRowAmount(2, '0.01');
    expect(cmp()['anyRowError']()).toBe(false);
    expect(line('Fee amount')).toContain('₹0.01');
    setRowAmount(2, '1500.00');
    expect(line('Fee amount')).toContain('₹1,500.00');
  });

  it('typing in a line ticks it; the "Fee amount" total is read-only and equals the selected lines', () => {
    open();
    setRowAmount(3, '10.50');
    expect(rows().find((r) => r.dueId === 3)!.checked).toBe(true);
    expect(el().querySelector('.fee-total output')!.textContent).toContain('₹10.50');
    expect(el().querySelector('.fee-total input')).toBeNull();
  });

  it('"Enter an amount to allocate oldest first" fills the selection oldest first; more than what is outstanding is refused', () => {
    open();
    expect(el().querySelector('#cp-oldest')).toBeNull();               // an optional helper, closed by default
    (cmp() as any)['toggleOldest']();
    tick();
    const input = el().querySelector<HTMLInputElement>('#cp-oldest')!;
    input.value = '3000';
    input.dispatchEvent(new Event('input'));
    (cmp() as any)['applyOldest']();
    tick();
    expect(rows().map((r) => [r.checked, r.amount])).toEqual([[true, '1500.00'], [true, '1500.00'], [false, '800.00']]);
    expect(line('Fee amount')).toContain('₹3,000.00');
    input.value = '5000';
    input.dispatchEvent(new Event('input'));
    (cmp() as any)['applyOldest']();
    tick();
    expect(text()).toContain('more than the');
    expect(rows().map((r) => r.checked)).toEqual([true, true, false]);  // unchanged
  });

  // ============================================================== concession
  it('lists active concessions, the ones that do not apply to the selection disabled with a short reason; a general one is always offered', () => {
    open({ concessions: [con(1, 'Sibling 10%', 'PERCENTAGE', 10), con(2, 'Exam 10%', 'PERCENTAGE', 10, EXAM, 'Exam Fee'), con(3, 'Tuition ward', 'FIXED', 500, TUITION, 'Tuition Fee')] });
    check(2);                                                          // a Tuition installment only
    const opts = cmp()['concessionOptions']() as { label: string; disabled?: boolean }[];
    expect(opts.map((o) => o.label)).toEqual(['None', 'Sibling 10%', 'Exam 10% — applies to Exam Fee only', 'Tuition ward (₹500)']);
    expect(opts.map((o) => !!o.disabled)).toEqual([false, false, true, false]);
    check(4);                                                          // add the Exam installment: now it applies
    expect((cmp()['concessionOptions']() as { disabled?: boolean }[]).map((o) => !!o.disabled)).toEqual([false, false, false, false]);
  });

  it('a chosen concession that stops applying is cleared with a short inline message (and its reason is dropped)', () => {
    open({ concessions: [con(2, 'Exam 10%', 'PERCENTAGE', 10, EXAM, 'Exam Fee')] });
    check(2);
    check(4);
    pickConcession('2');
    why('exam waiver');
    expect(cmp()['selectedConcession']()).not.toBeNull();
    check(4, false);                                                   // only Tuition left: the Exam concession no longer applies
    expect(cmp()['concessionControl'].value).toBe('');
    expect(cmp()['selectedConcession']()).toBeNull();
    expect(text()).toContain('"Exam 10%" was removed');
    expect(text()).toContain('Exam Fee only');
    expect(el().querySelector('app-textarea[label="Reason for the concession"]')).toBeNull();
    expect(cmp()['reasonControl'].value).toBe('');
    expect(line('Fee amount')).toContain('₹1,500.00');
    expect(summary()).not.toContain('Concession:');
  });

  it('the discount is never above its base: net fee and total never go negative (100 %, a larger fixed amount, a percentage of the matching lines only)', () => {
    open({ concessions: [con(1, 'Everything', 'PERCENTAGE', 100), con(2, 'Rs 5000 off', 'FIXED', 5000), con(3, 'Exam 50%', 'PERCENTAGE', 50, EXAM, 'Exam Fee')] });
    check(2);
    pickConcession('1');
    expect(cmp()['preview']()).toMatchObject({ grossPaise: 150000, concessionPaise: 150000, netPaise: 0, totalPaise: 0 });
    pickConcession('2');
    expect(cmp()['preview']()).toMatchObject({ concessionPaise: 150000, netPaise: 0 });   // capped at what the lines come to
    check(4);
    pickConcession('3');                                               // 50 % of the Exam line only (₹800) = ₹400, not of the whole ₹2,300
    expect(cmp()['preview']()).toMatchObject({ grossPaise: 230000, baseConcessionPaise: 80000, concessionPaise: 40000, netPaise: 190000 });
    expect(summary()).not.toMatch(/NaN|-\s?₹|₹-/);
  });

  it('a reason is required when a concession is selected (blank spaces do not count), at most 500 characters', () => {
    open({ concessions: [con(1, 'Sibling 10%', 'PERCENTAGE', 10)] });
    check(2);
    expect(submitButton().disabled).toBe(false);
    pickConcession('1');
    expect(el().querySelector('app-textarea[label="Reason for the concession"]')).not.toBeNull();
    expect(submitButton().disabled).toBe(true);
    why('   ');
    expect(submitButton().disabled).toBe(true);
    why('second child');
    expect(submitButton().disabled).toBe(false);
    why('x'.repeat(501));
    expect(submitButton().disabled).toBe(true);
    why('x'.repeat(500));
    expect(submitButton().disabled).toBe(false);
    pickConcession('');                                                // back to None: the reason box and the rule go away
    expect(el().querySelector('app-textarea[label="Reason for the concession"]')).toBeNull();
    expect(submitButton().disabled).toBe(false);
  });

  // ============================================================== summary, tax
  it('the worked example: 1000 less a 10% concession is 900, GST 18% on it is 162, so 1062 to collect', () => {
    open({ taxes: [tax(1, 'GST 18%', 18)], concessions: [con(1, 'Sibling 10%', 'PERCENTAGE', 10)] });
    check(3);
    setRowAmount(3, '1000');
    pickConcession('1');
    pickTax('1');
    expect(line('Fee amount')).toContain('₹1,000.00');
    expect(line('Concession: Sibling 10%')).toContain('− ₹100.00');
    expect(line('Net fee')).toContain('₹900.00');
    expect(line('Tax: GST 18%')).toContain('₹162.00');                // tax on the NET fee: discount first
    expect(line('Total to collect')).toContain('₹1,062.00');
  });

  it('rounds half up like the server: 333.33 less 10% is 33.33 off, 300.00 net, 54.00 tax, 354.00 total', () => {
    open({ taxes: [tax(1, 'GST 18%', 18)], concessions: [con(1, 'Sibling 10%', 'PERCENTAGE', 10)] });
    check(3);
    setRowAmount(3, '333.33');
    pickConcession('1');
    pickTax('1');
    expect(cmp()['preview']()).toMatchObject({ concessionPaise: 3333, netPaise: 30000, taxPaise: 5400, totalPaise: 35400 });
  });

  it('tax alone works without a concession; the tax hint for no tax types depends on the role', () => {
    open({ taxes: [tax(1, 'GST 18%', 18)] });
    check(2);
    pickTax('1');
    expect(cmp()['preview']()).toMatchObject({ grossPaise: 150000, taxPaise: 27000, totalPaise: 177000 });
    open({ taxes: [], roles: ['School Admin'] });
    expect(text()).toContain('Add one in Fee Setup');
    open({ taxes: [], roles: ['Accountant'] });
    expect(text()).not.toContain('Add one in Fee Setup');
    expect(text()).toContain('No tax types are set up');
  });

  // ============================================================== payment mode and the amount received
  it('Cash: the received amount is prefilled with the total, may be higher (change is shown) and may not be lower', () => {
    open({ taxes: [tax(1, 'GST 18%', 18)], concessions: [con(1, 'Sibling 10%', 'PERCENTAGE', 10)] });
    check(3);
    setRowAmount(3, '1000');
    pickConcession('1');
    pickTax('1');
    why('second child');
    expect(cmp()['modeControl'].value).toBe('Cash');
    expect(el().querySelector<HTMLInputElement>('#cp-received')!.value).toBe('1062.00');
    expect(text()).not.toContain('Change to return');
    typeReceived('2000');
    expect(text()).toContain('Change to return ₹938.00');
    expect(submitButton().disabled).toBe(false);
    typeReceived('1000');
    expect(text()).toContain('Cash received must be at least ₹1062.00');
    expect(submitButton().disabled).toBe(true);
    typeReceived('1062');
    expect(text()).not.toContain('Change to return');
    expect(submitButton().disabled).toBe(false);
    typeReceived('');
    expect(submitButton().disabled).toBe(true);
  });

  it('every other mode needs exactly the total; changing the mode starts again from the total', () => {
    open();
    check(2);
    typeReceived('2000');
    setMode('UPI');
    expect(el().querySelector<HTMLInputElement>('#cp-received')!.value).toBe('1500.00');
    expect(submitButton().disabled).toBe(false);
    typeReceived('2000');
    expect(text()).toContain('For UPI the amount must be exactly ₹1500.00');
    expect(submitButton().disabled).toBe(true);
    expect(text()).not.toContain('Change to return');
    typeReceived('1499.99');
    expect(submitButton().disabled).toBe(true);
    typeReceived('1500');
    expect(submitButton().disabled).toBe(false);
  });

  it('the received amount follows the total until the cashier types, and then stays what they typed', () => {
    open();
    check(2);
    expect(el().querySelector<HTMLInputElement>('#cp-received')!.value).toBe('1500.00');
    check(3);
    expect(el().querySelector<HTMLInputElement>('#cp-received')!.value).toBe('4000.00');
    typeReceived('50');
    check(4);
    expect(el().querySelector<HTMLInputElement>('#cp-received')!.value).toBe('50');
  });

  it('a FULLY WAIVED payment says so, shows no payment mode or received amount, and still records with the default mode', () => {
    open({ concessions: [con(1, 'Everything', 'PERCENTAGE', 100)] });
    check(2);
    pickConcession('1');
    expect(cmp()['waived']()).toBe(true);
    expect(text()).toContain('Fully waived');
    expect(line('Total to collect')).toContain('₹0.00');
    expect(el().querySelector('#cp-received')).toBeNull();
    expect(el().querySelector('app-select[label="Payment mode"]')).toBeNull();
    expect(submitButton().disabled).toBe(true);                        // the reason is still required
    why('waived by the principal');
    expect(submitButton().disabled).toBe(false);
    submitButton().click();
    const req = http.expectOne(URL);
    expect(req.request.body.paymentMode).toBe('Cash');
    expect(req.request.body.concessionId).toBe(1);
    req.flush(env(RECEIPT(0), 201));
    expect(recorded.length).toBe(1);
  });

  // ============================================================== submitting
  it('sends exactly the same request shape as before: amount = sum of the allocations, concession id + reason, tax type id, mode, remarks; never a discount, tax or total amount', () => {
    open({ taxes: [tax(1, 'GST 18%', 18)], concessions: [con(7, 'Sibling 10%', 'PERCENTAGE', 10)] });
    check(3);
    setRowAmount(3, '1000');
    check(4);
    pickConcession('7');
    why('  second child  ');
    pickTax('1');
    cmp()['remarksControl'].setValue('  cheque 12  ');
    typeReceived('3000');
    submitButton().click();
    const req = http.expectOne(URL);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      amount: 1800,
      taxTypeId: 1,
      concessionId: 7,
      concessionReason: 'second child',
      paymentMode: 'Cash',
      allocations: [{ feeDueId: 3, amount: 1000 }, { feeDueId: 4, amount: 800 }],
      remarks: 'cheque 12',
    });
    for (const forbidden of ['concessionAmount', 'discountAmount', 'discount', 'taxAmount', 'totalAmount', 'netFeeAmount', 'grossAmount', 'received', 'change']) expect(req.request.body).not.toHaveProperty(forbidden);
    req.flush(env(RECEIPT(1911.6), 201));
  });

  it('with no concession and no tax the body carries neither field; invalid lines are never sent', () => {
    open({ taxes: [tax(1, 'GST 18%', 18)], concessions: [con(7, 'Sibling 10%', 'PERCENTAGE', 10)] });
    check(2);
    check(3);
    setRowAmount(3, '9999');   // above what is outstanding: left out of the preview, blocks submit
    expect(submitButton().disabled).toBe(true);
    setRowAmount(3, '25');
    submitButton().click();
    const req = http.expectOne(URL);
    expect(req.request.body).toEqual({ amount: 1525, paymentMode: 'Cash', allocations: [{ feeDueId: 2, amount: 1500 }, { feeDueId: 3, amount: 25 }], remarks: null });
    req.flush(env(RECEIPT(1525), 201));
  });

  it('blocks a double submit: while saving the button is disabled and says so, and a second click sends nothing', () => {
    open();
    check(2);
    const btn = submitButton();
    btn.click();
    tick();
    expect(cmp()['saving']()).toBe(true);
    expect(submitButton().disabled).toBe(true);
    expect(submitButton().textContent).toContain('Recording');
    cmp()['submit']();                                                // even called directly
    btn.click();
    const reqs = http.match(URL);
    expect(reqs.length).toBe(1);
    reqs[0].flush(env(RECEIPT(15), 201));
    expect(recorded.length).toBe(1);
  });

  it('keeps the server\'s refusal visible in the dialog (e.g. the double-discount 409), stays open, and lets the cashier try again', () => {
    open({ concessions: [con(1, 'Sibling 10%', 'PERCENTAGE', 10)] });
    check(2);
    pickConcession('1');
    why('second child');
    submitButton().click();
    http.expectOne(URL).flush({ status: false, statusCode: 409, message: 'This student already has the concession "Sibling 10%" assigned.', data: null }, { status: 409, statusText: 'Conflict' });
    tick();
    expect(el().querySelector('.server-error')!.textContent).toContain('already has the concession');
    expect(recorded.length).toBe(0);
    expect(cmp()['saving']()).toBe(false);
    expect(submitButton().disabled).toBe(false);
  });

  it('the receipt carries the server\'s figures; a preview that differs only logs a generic note (no amounts)', () => {
    open();
    check(2);
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    submitButton().click();
    http.expectOne(URL).flush(env(RECEIPT(15.5), 201));
    expect(recorded[0].totalAmount).toBe(15.5);
    expect(warn).toHaveBeenCalledTimes(1);
    expect(String(warn.mock.calls[0][0])).not.toMatch(/\d/);
  });

  it('a matching server total logs nothing', () => {
    open();
    check(2);
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    submitButton().click();
    http.expectOne(URL).flush(env(RECEIPT(1500), 201));
    expect(warn).not.toHaveBeenCalled();
  });

  // ============================================================== accessibility
  it('labels are tied to their inputs, errors are announced, and the summary is a live region', () => {
    open();
    check(2);
    setRowAmount(2, '9999');
    expect(el().querySelector('.amt .err')!.getAttribute('role')).toBe('alert');
    expect(el().querySelector('.amt input')!.getAttribute('aria-invalid')).toBe('true');
    expect(el().querySelector('.amt input')!.getAttribute('aria-label')).toContain('Tuition Fee');
    expect(el().querySelector('label[for="cp-received"]')).not.toBeNull();
    expect(el().querySelector('.summary')!.getAttribute('aria-live')).toBe('polite');
    expect(el().querySelector('section[aria-labelledby="cp-installments"]')).not.toBeNull();
  });

  describe('late fee preview (5a: read-only)', () => {
    const RULE_ON = { enabled: true, ruleType: 'FLAT', amount: 50, percent: null, graceDays: 5, capPerDue: null, taxApplies: false, updatedAt: null, updatedBy: null, feeTypes: [{ feeTypeId: 1, name: 'Tuition Fee', isActive: true, appliesLateFee: true }] };
    const PREVIEW = { today: '2026-11-25', ruleEnabled: true, ruleType: 'FLAT', graceDays: 5, total: 50, taxName: null, taxAmount: 0, note: null,
      lines: [{ feeDueId: 3, feeType: 'Tuition Fee', dueDate: '2026-06-10', daysLate: 168, daysCharged: 163, baseAmount: 2500, computedTotal: 50, alreadyCollected: 0, amount: 50 }] };
    const wait = () => new Promise((r) => setTimeout(r, 300));

    it('asks nothing when the school charges no late fee: the payment screen works exactly as before', async () => {
      open();
      check(3);
      await wait();
      http.expectNone((r) => r.url.includes('/api/LateFee/preview'));
      expect(el().querySelector('[data-late-fee]')).toBeNull();
    });

    it('shows the server late fee for the selection, sends only allocations (never an amount of late fee) and does not change the total to collect', async () => {
      open({ lateRule: RULE_ON });
      const before = el().querySelector('.total dd')?.textContent;
      check(3);
      await wait();
      const req = http.expectOne((r) => r.method === 'POST' && r.url === `${API}/api/LateFee/preview/DPS001ST001`);
      expect(req.request.body).toEqual({ allocations: [{ feeDueId: 3, amount: 2500 }] });
      req.flush(env(PREVIEW));
      tick();
      expect(el().querySelector('[data-late-fee]')?.textContent).toContain('50');
      expect(el().querySelector('[data-late-lines]')?.textContent).toContain('168 days late');
      expect(el().querySelector('.total dd')?.textContent).not.toContain('2,550');          // the payment total is still just the fees: nothing is added yet
      expect(before).not.toBeUndefined();
    });

    it('a failed preview never blocks the payment and a cleared selection clears the line', async () => {
      open({ lateRule: RULE_ON });
      check(3);
      await wait();
      http.expectOne((r) => r.url === `${API}/api/LateFee/preview/DPS001ST001`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
      tick();
      expect(el().querySelector('[data-late-fee]')).toBeNull();
      check(3, false);
      await wait();
      http.expectNone((r) => r.url.includes('/api/LateFee/preview'));
    });
  });
});
