import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { FeeReceipt, PaymentSummary } from '../../models/fee-payment.model';
import { StudentFeeDues, StudentFeeItems } from '../../models/student-billing.model';
import { StudentFees } from '../../models/student-fees.model';
import { StudentFeesTabComponent } from './student-fees-tab.component';

const API = environment.apiUrl;
const URL = `${API}/api/StudentFee/DPS001ST001`;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

const base: StudentFees = {
  studentId: 'DPS001ST001', studentName: 'Abhinav Kumar', academicYearId: 1, academicYearName: '2026-27', classId: 10, className: 'Class 1',
  feeStructureId: null, structureName: null, structureIsActive: false, lines: [], concessions: [], totals: [],
  availableStructures: [
    { feeStructureId: 5, structureName: 'Class 1 Fees', classId: 10, className: 'Class 1' },
    { feeStructureId: 6, structureName: 'Class 2 Fees', classId: 11, className: 'Class 2' },
  ],
  availableConcessions: [],
};

const assigned: StudentFees = {
  ...base,
  feeStructureId: 5, structureName: 'Class 1 Fees', structureIsActive: true,
  lines: [
    { feeTypeId: 1, feeTypeName: 'Tuition', feeTypeCode: 'TUI', frequencyId: 2, frequencyName: 'Monthly', amount: 2500, discount: 250, netAmount: 2250 },
    { feeTypeId: 2, feeTypeName: 'Library', feeTypeCode: 'LIB', frequencyId: 5, frequencyName: 'Annual', amount: 600, discount: 0, netAmount: 600 },
  ],
  totals: [
    { frequencyId: 2, frequencyName: 'Monthly', gross: 2500, concession: 250, netPayable: 2250 },
    { frequencyId: 5, frequencyName: 'Annual', gross: 600, concession: 0, netPayable: 600 },
  ],
  concessions: [
    { studentFeeConcessionId: 1, concessionId: 9, concessionName: 'Sibling 10%', concessionCode: 'SIB', concessionType: 'PERCENTAGE', value: 10,
      applicableFeeTypeId: null, applicableFeeTypeName: null, remarks: 'second child', approvedByName: 'Delhi Public School', appliedAt: '2026-10-14T00:00:00Z', isApplicable: true },
  ],
  availableConcessions: [{ concessionId: 11, concessionName: 'Merit', concessionCode: 'MERIT', concessionType: 'FIXED', value: 500, applicableFeeTypeId: 1, applicableFeeTypeName: 'Tuition' }],
};

const FREQS = [
  { frequencyId: 2, frequencyName: 'Monthly', frequencyCode: 'MONTHLY', installments: 12 },
  { frequencyId: 3, frequencyName: 'Quarterly', frequencyCode: 'QUARTERLY', installments: 4 },
];
const ITEMS: StudentFeeItems = {
  studentId: 'DPS001ST001', academicYearId: 1, academicYearName: '2026-27', annualNetTotal: 30000,
  items: [
    { studentFeeItemId: 7, feeTypeId: 1, feeTypeName: 'Tuition', feeTypeCode: 'TUI', frequencyId: 2, frequencyName: 'Monthly', structureFrequencyName: 'Monthly',
      allowedFrequencies: FREQS, dueDay: 10, isOptional: false, baseAmount: 2500, concessionAmount: 0, netAmount: 2500, annualNetAmount: 30000, isApplicable: true, dueCount: 12, nextDueDate: '2026-11-10', totalDue: 30000, totalPaid: 0 },
  ],
};
const DUES: StudentFeeDues = {
  studentId: 'DPS001ST001', academicYearId: 1, academicYearName: '2026-27', totalDue: 5000, totalPaid: 2500, outstanding: 2500, overdueAmount: 0, overdueCount: 0,
  dues: [
    { feeDueId: 1, studentFeeItemId: 7, feeTypeName: 'Tuition', frequencyName: 'Monthly', dueDate: '2026-10-10', amountDue: 2500, amountPaid: 2500, outstanding: 0, status: 'Paid' },
    { feeDueId: 2, studentFeeItemId: 7, feeTypeName: 'Tuition', frequencyName: 'Monthly', dueDate: '2026-09-10', amountDue: 2500, amountPaid: 0, outstanding: 2500, status: 'Overdue' },
    { feeDueId: 3, studentFeeItemId: 7, feeTypeName: 'Tuition', frequencyName: 'Monthly', dueDate: '2026-11-10', amountDue: 2500, amountPaid: 0, outstanding: 2500, status: 'Pending' },
  ],
};

const PAYMENTS: PaymentSummary[] = [
  { paymentId: 11, receiptNumber: 'RCP-2026-0001', paymentDate: '2026-10-10T08:30:00Z', amount: 2500, paymentMode: 'Cash', collectedByName: 'Delhi Public School', dueCount: 1, status: 'Active' },
];
const RECEIPT: FeeReceipt = {
  paymentId: 12, receiptNumber: 'RCP-2026-0002', paymentDate: '2026-10-16T08:30:00Z', amount: 2500, paymentMode: 'UPI', remarks: null, collectedByName: 'Delhi Public School', status: 'Active',
  school: { name: 'Delhi Public School', address: 'Main Road' }, student: { studentId: 'DPS001ST001', name: 'Abhinav Kumar', className: 'Class 1', academicYearName: '2026-27' },
  lines: [{ feeDueId: 2, feeTypeName: 'Tuition', frequencyName: 'Monthly', dueDate: '2026-09-10', amountDue: 2500, allocatedAmount: 2500, paidToDate: 2500 }],
};

describe('StudentFeesTabComponent', () => {
  let fixture: ComponentFixture<StudentFeesTabComponent>;
  let http: HttpTestingController;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  function open(fees: StudentFees) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentFeesTabComponent);
    fixture.componentRef.setInput('studentId', 'DPS001ST001');
    fixture.detectChanges();
    http.expectOne(URL).flush(env(fees));
    if (fees.feeStructureId) flushBilling();
    else flushPayments();
    fixture.detectChanges();
  }

  function flushPayments(payments: PaymentSummary[] = PAYMENTS) {
    http.expectOne(`${API}/api/FeePayment/students/DPS001ST001`).flush(env(payments));
  }

  /** Once a structure is assigned the tab also loads the fee items and the installment schedule. */
  function flushBilling(items: StudentFeeItems = ITEMS, dues: StudentFeeDues = DUES) {
    flushPayments();
    http.expectOne(`${URL}/fee-items`).flush(env(items));
    http.expectOne(`${URL}/fee-dues`).flush(env(dues));
    fixture.detectChanges();
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('no structure: explains it and offers Assign, with only the student\'s own class by default', () => {
    open(base);
    expect(text()).toContain('No fee structure assigned');
    expect(text()).toContain('Assign Fee Structure');
    expect(cmp()['structureOptions']().map((o: { label: string }) => o.label)).toEqual(['Class 1 Fees']);
    cmp()['showOtherClasses'].set(true);
    expect(cmp()['structureOptions']().map((o: { label: string }) => o.label)).toEqual(['Class 1 — Class 1 Fees', 'Class 2 — Class 2 Fees']);
  });

  it('assigning PUTs the structure id and then shows the returned fees', () => {
    open(base);
    cmp()['openAssign']();
    cmp()['structureControl'].setValue('5');
    cmp()['submitAssign']();
    const req = http.expectOne(`${URL}/structure`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ feeStructureId: 5 });
    req.flush(env(assigned));
    flushBilling();
    expect(text()).toContain('Class 1 Fees');
    expect(cmp()['assignOpen']()).toBe(false);
  });

  it('assign needs a selection', () => {
    open(base);
    cmp()['openAssign']();
    cmp()['submitAssign']();
    http.expectNone(`${URL}/structure`);
    expect(cmp()['structureError']()).toBeTruthy();
  });

  it('shows the fee heads, concessions and net payable per frequency (never one blended total)', () => {
    open(assigned);
    expect(text()).toContain('Tuition');
    expect(text()).toContain('₹2,500.00');
    expect(text()).toContain('Sibling 10%');
    expect(text()).toContain('second child');
    expect(cmp()['totals']()).toEqual([
      { frequencyName: 'Monthly', gross: '₹2,500.00', concession: '− ₹250.00', net: '₹2,250.00' },
      { frequencyName: 'Annual', gross: '₹600.00', concession: '—', net: '₹600.00' },
    ]);
    expect(text()).toContain('not added together');
  });

  it('adds a concession with remarks, offering only the fitting ones', () => {
    open(assigned);
    expect(cmp()['concessionOptions']().map((o: { label: string }) => o.label)).toEqual(['Merit — ₹500.00 on Tuition']);
    cmp()['openConcession']();
    cmp()['concessionControl'].setValue('11');
    cmp()['remarksControl'].setValue('  scholarship  ');
    cmp()['submitConcession']();
    const req = http.expectOne(`${URL}/concessions`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ concessionId: 11, remarks: 'scholarship' });
    req.flush(env({ ...assigned, availableConcessions: [] }, 201));
    flushBilling();
    expect(cmp()['concessionOpen']()).toBe(false);
  });

  it('removing a concession asks first, then DELETEs it', async () => {
    open(assigned);
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    await cmp()['removeConcession'](assigned.concessions[0]);
    const req = http.expectOne(`${URL}/concessions/9`);
    expect(req.request.method).toBe('DELETE');
    req.flush(env({ ...assigned, concessions: [] }));
    flushBilling();
    expect(text()).toContain('No concessions applied');
  });

  it('a student with no enrollment gets an explanation, not an assign button', () => {
    open({ ...base, academicYearId: null, classId: null, className: null, academicYearName: null });
    expect(text()).toContain('not enrolled in any class');
    expect(text()).not.toContain('Assign Fee Structure');
  });

  it('shows each fee head with how often it is paid, and the installment schedule with coloured status badges', () => {
    open(assigned);
    expect(text()).toContain('Fee items');
    expect(cmp()['itemRows']()[0]).toMatchObject({ feeTypeName: 'Tuition', frequencyName: 'Monthly', applicable: 'Yes', annualNet: '₹30,000.00', dues: '12' });
    expect(cmp()['dueRows']().map((r: { status: string }) => r.status)).toEqual(['Paid', 'Overdue', 'Pending']);

    const pills = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.pill')].map((e) => [e.textContent?.trim(), e.className]);
    const tone = (label: string) => String(pills.find((p) => p[0] === label)?.[1]);
    expect(tone('Paid')).toContain('tone-green');
    expect(tone('Overdue')).toContain('tone-red');
    expect(tone('Pending')).toContain('tone-yellow');
    expect(cmp()['dueSummary']()).toMatchObject({ total: '₹5,000.00', paid: '₹2,500.00', outstanding: '₹2,500.00' });
  });

  it('changing an item\'s frequency PUTs just that change, then refreshes the schedule', () => {
    open(assigned);
    cmp()['openEdit'](cmp()['itemRows']()[0]);
    expect(cmp()['editFrequencyOptions']().map((o: { label: string }) => o.label)).toEqual(['Monthly', 'Quarterly']);
    cmp()['frequencyControl'].setValue('3');
    cmp()['submitEdit']();

    const req = http.expectOne(`${URL}/fee-items/7`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ frequencyId: 3 });   // applicability unchanged -> not sent
    req.flush(env({ ...ITEMS, items: [{ ...ITEMS.items[0], frequencyId: 3, frequencyName: 'Quarterly', dueCount: 5 }] }));
    // the schedule and the concession totals both move
    http.expectOne(URL).flush(env(assigned));
    http.expectOne(`${URL}/fee-dues`).flush(env(DUES));
    fixture.detectChanges();
    expect(cmp()['editing']()).toBeNull();
    expect(cmp()['itemRows']()[0].frequencyName).toContain('Quarterly');
  });

  it('a NON-optional fee head cannot be switched off from the dialog (no checkbox, just an explanation)', () => {
    open(assigned);
    cmp()['openEdit'](cmp()['itemRows']()[0]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.dialog input[type="checkbox"]')).toBeNull();
    expect(text()).toContain('not an optional fee');
  });

  it('switching an OPTIONAL fee head off sends isApplicable=false', () => {
    open({ ...assigned });
    http.verify();
    // same data, but Tuition is now optional
    cmp()['items'].set({ ...ITEMS, items: [{ ...ITEMS.items[0], isOptional: true }] });
    fixture.detectChanges();
    cmp()['openEdit'](cmp()['itemRows']()[0]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.dialog input[type="checkbox"]')).not.toBeNull();
    cmp()['applicableControl'].setValue(false);
    cmp()['submitEdit']();
    const req = http.expectOne(`${URL}/fee-items/7`);
    expect(req.request.body).toEqual({ isApplicable: false });
    req.flush(env({ ...ITEMS, items: [{ ...ITEMS.items[0], isOptional: true, isApplicable: false, dueCount: 0 }] }));
    http.expectOne(URL).flush(env(assigned));
    http.expectOne(`${URL}/fee-dues`).flush(env({ ...DUES, dues: [] }));
    fixture.detectChanges();
    expect(cmp()['itemRows']()[0]).toMatchObject({ applicable: 'No', annualNet: '—', dues: '—' });
  });

  it('closing the edit dialog without changing anything sends nothing', () => {
    open(assigned);
    cmp()['openEdit'](cmp()['itemRows']()[0]);
    cmp()['submitEdit']();
    http.expectNone(`${URL}/fee-items/7`);
    expect(cmp()['editing']()).toBeNull();
  });

  describe('payments', () => {
    const DUE_URL = `${URL}/fee-dues`;

    it('lists the payment history with receipt number, date, amount, mode and collector', () => {
      open(assigned);
      expect(cmp()['paymentRows']()[0]).toMatchObject({ receiptNumber: 'RCP-2026-0001', amount: '₹2,500.00', paymentMode: 'Cash', collectedByName: 'Delhi Public School' });
      expect(text()).toContain('RCP-2026-0001');
    });

    it('Collect Payment is enabled only while some installment still has a balance', () => {
      open(assigned);
      const button = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((b) => b.textContent?.includes('Collect Payment')) as HTMLButtonElement;
      expect(button().disabled).toBe(false);   // DUES has an overdue and a pending installment

      cmp()['dues'].set({ ...DUES, dues: DUES.dues.map((d) => ({ ...d, amountPaid: d.amountDue, outstanding: 0, status: 'Paid' })) });
      fixture.detectChanges();
      expect(button().disabled).toBe(true);
    });

    it('opens the dialog with the student\'s installments, and after recording shows the receipt and refreshes the schedule and history', () => {
      open(assigned);
      cmp()['openCollect']();
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).querySelector('app-collect-payment-dialog')).not.toBeNull();

      cmp()['onRecorded'](RECEIPT);
      http.expectOne(URL).flush(env(assigned));
      http.expectOne(DUE_URL).flush(env(DUES));
      http.expectOne(`${URL}/fee-items`).flush(env(ITEMS));
      http.expectOne(`${API}/api/FeePayment/students/DPS001ST001`).flush(env([{ ...PAYMENTS[0] }, { ...PAYMENTS[0], paymentId: 12, receiptNumber: 'RCP-2026-0002' }]));
      fixture.detectChanges();

      expect(cmp()['collectOpen']()).toBe(false);
      expect(cmp()['receipt']()).toEqual({ data: RECEIPT, justPaid: true });
      expect(text()).toContain('RCP-2026-0002');
      expect(cmp()['paymentRows']().length).toBe(2);
    });

    const voidButton = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((b) => b.textContent?.includes('Void Payment')) as HTMLButtonElement | undefined;

    it('shows a Void Payment button on Active rows only; a Voided row shows a red VOID status and no action', () => {
      open(assigned);
      expect(voidButton()).toBeDefined();

      cmp()['payments'].set([{ ...PAYMENTS[0], status: 'Voided', voidReason: 'Wrong student', voidedByName: 'Office' }]);
      fixture.detectChanges();
      expect(voidButton()).toBeUndefined();
      expect(text()).toContain('Voided');
      expect((fixture.nativeElement as HTMLElement).querySelector('.pill.tone-red')).not.toBeNull();
      expect(text()).toContain('Receipt');   // still viewable
    });

    it('the Void Payment button is hidden from roles that may not void (the API refuses them too)', () => {
      clearSessions();
      seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['Principal'] }) });
      TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
      http = TestBed.inject(HttpTestingController);
      fixture = TestBed.createComponent(StudentFeesTabComponent);
      fixture.componentRef.setInput('studentId', 'DPS001ST001');
      fixture.detectChanges();
      http.expectOne(URL).flush(env(assigned));
      flushBilling();
      fixture.detectChanges();
      expect(voidButton()).toBeUndefined();
      expect(text()).toContain('RCP-2026-0001');
    });

    it('voiding needs a typed reason (submit disabled until then), POSTs it, then refreshes the history and the installments without a reload', () => {
      open(assigned);
      cmp()['openVoid'](cmp()['paymentRows']()[0]);
      fixture.detectChanges();
      const confirm = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll('app-modal button')].find((b) => b.textContent?.includes('Void Payment')) as HTMLButtonElement;
      expect(confirm().disabled).toBe(true);

      cmp()['voidReasonControl'].setValue('   ');
      fixture.detectChanges();
      expect(confirm().disabled).toBe(true);   // blank is not a reason

      cmp()['voidReasonControl'].setValue('Entered against the wrong student');
      fixture.detectChanges();
      expect(confirm().disabled).toBe(false);

      cmp()['submitVoid']();
      const req = http.expectOne(`${API}/api/FeePayment/11/void`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ voidReason: 'Entered against the wrong student' });
      req.flush(env({ ...PAYMENTS[0], status: 'Voided', voidReason: 'Entered against the wrong student' }));

      // the history and the schedule are re-read straight away
      http.expectOne(`${API}/api/FeePayment/students/DPS001ST001`).flush(env([{ ...PAYMENTS[0], status: 'Voided', voidReason: 'Entered against the wrong student' }]));
      http.expectOne(URL).flush(env(assigned));
      http.expectOne(DUE_URL).flush(env({ ...DUES, dues: DUES.dues.map((d) => ({ ...d, amountPaid: 0, outstanding: d.amountDue, status: 'Overdue' })) }));
      http.expectOne(`${URL}/fee-items`).flush(env({ ...ITEMS, items: [{ ...ITEMS.items[0], totalPaid: 0 }] }));
      fixture.detectChanges();

      expect(cmp()['voiding']()).toBeNull();
      expect(cmp()['dues']().dues.every((d: { status: string }) => d.status === 'Overdue')).toBe(true);   // the Paid installment flipped back
      expect(cmp()['paymentRows']()[0].status).toBe('Voided');
      expect(voidButton()).toBeUndefined();
    });

    it('per fee item, Paid and Outstanding come from the real payments (and update after a void)', () => {
      open(assigned);
      expect(cmp()['itemRows']()[0]).toMatchObject({ paid: '₹0.00', outstanding: '₹30,000.00' });
      cmp()['items'].set({ ...ITEMS, items: [{ ...ITEMS.items[0], totalPaid: 7500 }] });
      expect(cmp()['itemRows']()[0]).toMatchObject({ paid: '₹7,500.00', outstanding: '₹22,500.00' });
      cmp()['items'].set({ ...ITEMS, items: [{ ...ITEMS.items[0], isApplicable: false, dueCount: 0, totalDue: 0, totalPaid: 0 }] });
      expect(cmp()['itemRows']()[0]).toMatchObject({ paid: '—', outstanding: '—' });
    });

    it('"Receipt" in the history fetches that receipt for reprint', () => {
      open(assigned);
      cmp()['viewReceipt'](cmp()['paymentRows']()[0]);
      const req = http.expectOne(`${API}/api/FeePayment/11/receipt`);
      expect(req.request.method).toBe('GET');
      req.flush(env(RECEIPT));
      fixture.detectChanges();
      expect(cmp()['receipt']()).toEqual({ data: RECEIPT, justPaid: false });
      expect((fixture.nativeElement as HTMLElement).querySelector('app-fee-receipt-dialog')).not.toBeNull();
    });
  });
});
