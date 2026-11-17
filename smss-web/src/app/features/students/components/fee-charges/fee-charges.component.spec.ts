import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { FeeItemSelection, FeePreview, InstallmentPlanOption } from '../../models/student-billing.model';
import { BackdatingSummary, FeeConfigurationComponent } from './fee-charges.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const plan = (code: string, name: string, perCycle: number, yearly: number, extra: Partial<InstallmentPlanOption> = {}): InstallmentPlanOption => ({
  code, name, installmentsPerCycle: perCycle, installments: yearly, remainingInstallments: yearly, remainingAmount: undefined, ...extra,
});

// Tuition: Half-Yearly 600 per cycle -> None = 2 x 600, Quarterly = 4 x 300, Monthly = 12 x 100. The amount is per CYCLE and never changes with the plan.
const HALF_YEARLY_PLANS: InstallmentPlanOption[] = [
  plan('NONE', 'None (one payment per cycle)', 1, 2, { remainingAmount: 1200 }),
  plan('QUARTERLY', 'Quarterly', 2, 4, { remainingAmount: 1200 }),
  plan('MONTHLY', 'Monthly', 6, 12, { remainingAmount: 1200 }),
];
const PREVIEW: FeePreview = {
  academicYearId: 1, classId: 10, hasFeeStructure: true, feeStructureId: 5, structureName: 'Class 1 Fees', chargeFrom: '2026-11-17', isBackdated: false, backdatingAllowed: true,
  lines: [
    { feeTypeId: 100, feeTypeName: 'Tuition', feeTypeCode: 'TUI', isOptional: false, amount: 600, structureFrequencyId: 4, structureFrequencyName: 'Half-Yearly', periodsPerYear: 2, dueDay: 10, effectiveFrom: '2026-04-01', allowedPlans: HALF_YEARLY_PLANS },
    { feeTypeId: 101, feeTypeName: 'Admission', feeTypeCode: 'ADM', isOptional: false, amount: 5000, structureFrequencyId: 1, structureFrequencyName: 'One Time', periodsPerYear: 1, dueDay: null, effectiveFrom: '2026-04-01',
      allowedPlans: [plan('NONE', 'None (one payment per cycle)', 1, 1, { remainingAmount: 5000 })] },
    { feeTypeId: 102, feeTypeName: 'Transport', feeTypeCode: 'TRN', isOptional: true, amount: 1000, structureFrequencyId: 2, structureFrequencyName: 'Monthly', periodsPerYear: 12, dueDay: 5, effectiveFrom: '2026-04-01',
      allowedPlans: [plan('NONE', 'None (one payment per cycle)', 1, 12, { remainingAmount: 12000 })] },
  ],
};
const URL = (y: number, c: number) => `${API}/api/StudentFee/preview?academicYearId=${y}&classId=${c}`;

describe('FeeConfigurationComponent', () => {
  let fixture: ComponentFixture<FeeConfigurationComponent>;
  let http: HttpTestingController;
  let emitted: FeeItemSelection[][];
  let backdating: BackdatingSummary[];
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FeeConfigurationComponent);
    emitted = [];
    backdating = [];
    fixture.componentInstance.selectionsChange.subscribe((s) => emitted.push(s));
    fixture.componentInstance.backdatingChange.subscribe((s) => backdating.push(s));
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  function choose(year: number | null, cls: number | null) {
    fixture.componentRef.setInput('academicYearId', year);
    fixture.componentRef.setInput('classId', cls);
    fixture.detectChanges();
  }
  const load = (preview: FeePreview = PREVIEW) => {
    choose(1, 10);
    http.expectOne(URL(1, 10)).flush(env(preview));
    fixture.detectChanges();
  };

  it('asks for the year and class first and calls nothing until both are chosen', () => {
    choose(1, null);
    http.expectNone(() => true);
    expect(text()).toContain('Choose the academic year and class');
  });

  it('shows the amount per billing cycle, not a per-student frequency', () => {
    load();
    expect(text()).toContain('Class 1 Fees');
    expect(text()).toContain('per billing cycle');
    const rows = cmp()['rows']();
    expect(rows[0]).toMatchObject({ plan: 'NONE', installments: 2, perInstallment: 600, payable: 1200 });
    expect(rows[1]).toMatchObject({ locked: true, payable: 5000 });
    expect(cmp()['yearTotal']()).toBe(6200);   // Transport is optional and starts unticked, so it is not in the total
    expect(text()).not.toContain('Pays');       // the old per-student frequency column is gone
  });

  it('a Half-Yearly fee offers None / Quarterly / Monthly; a One Time fee has no plan to choose', () => {
    load();
    const selects = [...(fixture.nativeElement as HTMLElement).querySelectorAll('select')];
    expect(selects).toHaveLength(1);
    expect([...selects[0].options].map((o) => o.textContent?.trim())).toEqual(['None (one payment per cycle)', 'Quarterly', 'Monthly']);
    expect(text()).toContain('One payment per cycle');
  });

  it('the default is None and is what gets emitted', () => {
    load();
    expect(emitted.at(-1)).toEqual([
      { feeTypeId: 100, installmentPlan: 'NONE', isApplicable: true },
      { feeTypeId: 101, installmentPlan: 'NONE', isApplicable: true },
      { feeTypeId: 102, installmentPlan: 'NONE', isApplicable: false },   // optional: opt-in
    ]);
  });

  it('choosing Monthly splits each cycle but never changes the total, and is emitted for the request', () => {
    load();
    const select = (fixture.nativeElement as HTMLElement).querySelector('select')!;
    select.value = 'MONTHLY';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(cmp()['rows']()[0]).toMatchObject({ plan: 'MONTHLY', installments: 12, remaining: 12, payable: 1200 });
    expect(cmp()['rows']()[0].perInstallment).toBeCloseTo(100, 5);   // 600 per cycle / 6 months
    expect(cmp()['yearTotal']()).toBe(6200);
    expect(emitted.at(-1)![0]).toEqual({ feeTypeId: 100, installmentPlan: 'MONTHLY', isApplicable: true });
  });

  it('no fee structure for the class: a plain explanation, nothing emitted to send', () => {
    choose(1, 11);
    http.expectOne(URL(1, 11)).flush(env({ academicYearId: 1, classId: 11, hasFeeStructure: false, chargeFrom: '2026-11-17', isBackdated: false, backdatingAllowed: true, lines: [] }));
    fixture.detectChanges();
    expect(text()).toContain('No fee structure exists for this class');
    expect(emitted.at(-1)).toEqual([]);
  });

  it('changing the class reloads, and a stale answer for the old class is ignored', () => {
    choose(1, 10);
    const first = http.expectOne(URL(1, 10));
    choose(1, 11);
    http.expectOne(URL(1, 11)).flush(env({ academicYearId: 1, classId: 11, hasFeeStructure: false, chargeFrom: '2026-11-17', isBackdated: false, backdatingAllowed: true, lines: [] }));
    first.flush(env(PREVIEW));   // arrives late
    fixture.detectChanges();
    expect(cmp()['preview']()?.classId).toBe(11);
  });

  describe('optional fee heads', () => {
    const boxes = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];

    it('only an OPTIONAL head gets an include checkbox', () => {
      load();
      expect(boxes().map((b) => b.getAttribute('aria-label'))).toEqual(['Include Transport']);
    });

    it('an optional head starts excluded, then ticking it adds it to the total and to what is sent', () => {
      load();
      const box = boxes()[0];
      expect(box.checked).toBe(false);
      expect(cmp()['yearTotal']()).toBe(6200);

      box.checked = true;
      box.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(cmp()['yearTotal']()).toBe(18200);   // + 12,000
      expect(emitted.at(-1)!.find((s) => s.feeTypeId === 102)).toEqual({ feeTypeId: 102, installmentPlan: 'NONE', isApplicable: true });

      box.checked = false;
      box.dispatchEvent(new Event('change'));
      expect(cmp()['yearTotal']()).toBe(6200);
    });
  });

  describe('charge-from date', () => {
    it('asks the server for the schedule from that date and shows only what is payable from it', () => {
      fixture.componentRef.setInput('startDate', '2026-12-01');
      choose(1, 10);
      http.expectOne(`${URL(1, 10)}&startDate=2026-12-01`).flush(
        env<FeePreview>({
          ...PREVIEW,
          chargeFrom: '2026-12-01',
          lines: [{ ...PREVIEW.lines[0], allowedPlans: [plan('NONE', 'None (one payment per cycle)', 1, 2, { remainingInstallments: 1, remainingAmount: 600 }), HALF_YEARLY_PLANS[1], HALF_YEARLY_PLANS[2]] }],
        }),
      );
      fixture.detectChanges();
      expect(cmp()['rows']()[0]).toMatchObject({ installments: 2, remaining: 1, payable: 600 });
      expect(text()).toContain('Payable from');
    });

    it('keeps the plan already picked when only the date changes', () => {
      load();
      const select = (fixture.nativeElement as HTMLElement).querySelector('select')!;
      select.value = 'QUARTERLY';
      select.dispatchEvent(new Event('change'));
      fixture.componentRef.setInput('startDate', '2026-12-01');
      fixture.detectChanges();
      http.expectOne(`${URL(1, 10)}&startDate=2026-12-01`).flush(env(PREVIEW));
      fixture.detectChanges();
      expect(cmp()['rows']()[0]).toMatchObject({ plan: 'QUARTERLY', installments: 4 });
    });
  });

  describe('an earlier charge-from date (back-dating)', () => {
    const BACK: FeePreview = {
      ...PREVIEW,
      chargeFrom: '2026-09-01',
      isBackdated: true,
      lines: [
        { ...PREVIEW.lines[0], allowedPlans: [plan('NONE', 'None (one payment per cycle)', 1, 2, { remainingAmount: 1200, backdatedInstallments: 1, backdatedAmount: 600 }), HALF_YEARLY_PLANS[1], HALF_YEARLY_PLANS[2]] },
        PREVIEW.lines[1],
      ],
    };

    it('reports how many dues already in the past it would create, and their total', () => {
      fixture.componentRef.setInput('startDate', '2026-09-01');
      choose(1, 10);
      http.expectOne(`${URL(1, 10)}&startDate=2026-09-01`).flush(env(BACK));
      fixture.detectChanges();
      expect(backdating.at(-1)).toMatchObject({ isBackdated: true, allowed: true, count: 1, amount: 600 });
      expect(text()).toContain('already past');
    });

    it('says why when a back-dated charge is not possible', () => {
      fixture.componentRef.setInput('startDate', '2025-09-01');
      choose(1, 10);
      http.expectOne(`${URL(1, 10)}&startDate=2025-09-01`).flush(env({ ...BACK, backdatingAllowed: false, backdatingMessage: 'Earlier dates are only possible in the current academic year.' }));
      fixture.detectChanges();
      expect(backdating.at(-1)).toMatchObject({ isBackdated: true, allowed: false });
      expect(text()).toContain('only possible in the current academic year');
    });

    it('a normal (today) date reports no back-dating', () => {
      load();
      expect(backdating.at(-1)).toMatchObject({ isBackdated: false, count: 0, amount: 0 });
    });
  });
});
