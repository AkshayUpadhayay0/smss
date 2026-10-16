import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { FeeItemSelection, FeePreview } from '../../models/student-billing.model';
import { FeeConfigurationComponent } from './fee-configuration.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const RECURRING = [
  { frequencyId: 2, frequencyName: 'Monthly', frequencyCode: 'MONTHLY', installments: 12 },
  { frequencyId: 3, frequencyName: 'Quarterly', frequencyCode: 'QUARTERLY', installments: 4 },
  { frequencyId: 5, frequencyName: 'Annual', frequencyCode: 'ANNUAL', installments: 1 },
];
const PREVIEW: FeePreview = {
  academicYearId: 1, classId: 10, hasFeeStructure: true, feeStructureId: 5, structureName: 'Class 1 Fees',
  lines: [
    { feeTypeId: 100, feeTypeName: 'Tuition', feeTypeCode: 'TUI', isOptional: false, amount: 2500, structureFrequencyId: 2, structureFrequencyName: 'Monthly', periodsPerYear: 12, dueDay: 10, allowedFrequencies: RECURRING },
    { feeTypeId: 101, feeTypeName: 'Admission', feeTypeCode: 'ADM', isOptional: false, amount: 5000, structureFrequencyId: 1, structureFrequencyName: 'One Time', periodsPerYear: 1, dueDay: null,
      allowedFrequencies: [{ frequencyId: 1, frequencyName: 'One Time', frequencyCode: 'ONE_TIME', installments: 1 }] },
    { feeTypeId: 102, feeTypeName: 'Transport', feeTypeCode: 'TRN', isOptional: true, amount: 1000, structureFrequencyId: 2, structureFrequencyName: 'Monthly', periodsPerYear: 12, dueDay: 5, allowedFrequencies: RECURRING },
  ],
};
const URL = (y: number, c: number) => `${API}/api/StudentFee/preview?academicYearId=${y}&classId=${c}`;

describe('FeeConfigurationComponent', () => {
  let fixture: ComponentFixture<FeeConfigurationComponent>;
  let http: HttpTestingController;
  let emitted: FeeItemSelection[][];
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FeeConfigurationComponent);
    emitted = [];
    fixture.componentInstance.selectionsChange.subscribe((s) => emitted.push(s));
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

  it('asks for the year and class first and calls nothing until both are chosen', () => {
    choose(1, null);
    http.expectNone(() => true);
    expect(text()).toContain('Choose the academic year and class');
  });

  it('loads the structure for the class + year and shows each fee head with amounts and the year total', () => {
    choose(1, 10);
    http.expectOne(URL(1, 10)).flush(env(PREVIEW));
    fixture.detectChanges();

    expect(text()).toContain('Class 1 Fees');
    const rows = cmp()['rows']();
    expect(rows[0]).toMatchObject({ frequencyId: 2, installments: 12, perInstallment: 2500, payable: 30000 });
    expect(rows[1]).toMatchObject({ locked: true, payable: 5000 });
    expect(cmp()['yearTotal']()).toBe(35000);   // Transport is optional and starts unticked, so it is not in the total
    expect(text()).toContain('₹35,000.00');
  });

  it('a one-time fee can only stay one-time; a monthly fee offers the recurring frequencies', () => {
    choose(1, 10);
    http.expectOne(URL(1, 10)).flush(env(PREVIEW));
    fixture.detectChanges();
    const selects = [...(fixture.nativeElement as HTMLElement).querySelectorAll('select')];
    expect([...selects[0].options].map((o) => o.textContent?.trim())).toEqual(['Monthly', 'Quarterly', 'Annual']);
    expect([...selects[1].options].map((o) => o.textContent?.trim())).toEqual(['One Time']);
    expect(selects[1].disabled).toBe(true);
  });

  it('choosing Quarterly updates the installment and emits the choices for the request', () => {
    choose(1, 10);
    http.expectOne(URL(1, 10)).flush(env(PREVIEW));
    fixture.detectChanges();
    expect(emitted.at(-1)).toEqual([
      { feeTypeId: 100, frequencyId: 2, isApplicable: true },
      { feeTypeId: 101, frequencyId: 1, isApplicable: true },
      { feeTypeId: 102, frequencyId: 2, isApplicable: false },   // optional: opt-in
    ]);

    const select = (fixture.nativeElement as HTMLElement).querySelector('select')!;
    select.value = '3';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(cmp()['rows']()[0]).toMatchObject({ frequencyId: 3, installments: 4, perInstallment: 7500, payable: 30000 });
    expect(emitted.at(-1)![0]).toEqual({ feeTypeId: 100, frequencyId: 3, isApplicable: true });
  });

  it('no fee structure for the class: a plain explanation, nothing emitted to send', () => {
    choose(1, 11);
    http.expectOne(URL(1, 11)).flush(env({ academicYearId: 1, classId: 11, hasFeeStructure: false, lines: [] }));
    fixture.detectChanges();
    expect(text()).toContain('No fee structure exists for this class');
    expect(emitted.at(-1)).toEqual([]);
  });

  it('changing the class reloads, and a stale answer for the old class is ignored', () => {
    choose(1, 10);
    const first = http.expectOne(URL(1, 10));
    choose(1, 11);
    http.expectOne(URL(1, 11)).flush(env({ academicYearId: 1, classId: 11, hasFeeStructure: false, lines: [] }));
    first.flush(env(PREVIEW));   // arrives late
    fixture.detectChanges();
    expect(cmp()['preview']()?.classId).toBe(11);
  });

  describe('optional fee heads', () => {
    const boxes = () => [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];

    it('only an OPTIONAL head gets an include checkbox; Tuition and Admission have no toggle', () => {
      choose(1, 10);
      http.expectOne(URL(1, 10)).flush(env(PREVIEW));
      fixture.detectChanges();
      expect(boxes().map((b) => b.getAttribute('aria-label'))).toEqual(['Include Transport']);
      expect(text()).toContain('Optional');
    });

    it('an optional head starts excluded, then ticking it adds it to the total and to what is sent', () => {
      choose(1, 10);
      http.expectOne(URL(1, 10)).flush(env(PREVIEW));
      fixture.detectChanges();
      const box = boxes()[0];
      expect(box.checked).toBe(false);
      expect(cmp()['yearTotal']()).toBe(35000);

      box.checked = true;
      box.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      expect(cmp()['yearTotal']()).toBe(47000);   // + 12 x 1,000
      expect(emitted.at(-1)!.find((s) => s.feeTypeId === 102)).toEqual({ feeTypeId: 102, frequencyId: 2, isApplicable: true });

      box.checked = false;
      box.dispatchEvent(new Event('change'));
      expect(cmp()['yearTotal']()).toBe(35000);
    });

    it('the non-optional heads are always sent as applied', () => {
      choose(1, 10);
      http.expectOne(URL(1, 10)).flush(env(PREVIEW));
      fixture.detectChanges();
      const sent = emitted.at(-1)!;
      expect(sent.filter((s) => s.feeTypeId !== 102).every((s) => s.isApplicable === true)).toBe(true);
    });
  });

  describe('mid-year start', () => {
    const MIDYEAR: FeePreview = {
      ...PREVIEW,
      lines: PREVIEW.lines.slice(0, 2).map((l) => ({
        ...l,
        allowedFrequencies: l.allowedFrequencies.map((f) => ({ ...f, remainingInstallments: f.installments === 12 ? 6 : f.installments === 4 ? 2 : 1 })),
      })),
    };

    it('asks the server for the schedule from the start date and charges only the remaining installments', () => {
      fixture.componentRef.setInput('startDate', '2026-10-05');
      choose(1, 10);
      http.expectOne(`${URL(1, 10)}&startDate=2026-10-05`).flush(env(MIDYEAR));
      fixture.detectChanges();
      expect(cmp()['rows']()[0]).toMatchObject({ installments: 12, remaining: 6, perInstallment: 2500, payable: 15000 });   // 6 x 2,500
      expect(cmp()['rows']()[1]).toMatchObject({ payable: 5000 });   // a one-time charge is whole
      expect(cmp()['yearTotal']()).toBe(20000);
      expect(text()).toContain('part-way through the year');
    });

    it('keeps the frequency already picked when only the start date changes', () => {
      choose(1, 10);
      http.expectOne(URL(1, 10)).flush(env(PREVIEW));
      fixture.detectChanges();
      const select = (fixture.nativeElement as HTMLElement).querySelector('select')!;
      select.value = '3';
      select.dispatchEvent(new Event('change'));
      fixture.componentRef.setInput('startDate', '2026-10-05');
      fixture.detectChanges();
      http.expectOne(`${URL(1, 10)}&startDate=2026-10-05`).flush(env(MIDYEAR));
      fixture.detectChanges();
      expect(cmp()['rows']()[0]).toMatchObject({ frequencyId: 3, remaining: 2, payable: 15000 });   // 2 quarters x 7,500
    });
  });
});
