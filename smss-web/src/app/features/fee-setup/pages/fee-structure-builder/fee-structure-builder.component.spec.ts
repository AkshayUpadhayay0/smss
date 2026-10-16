import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { FeeStructureBuilderComponent } from './fee-structure-builder.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

const YEARS = [{ academicYearId: 1, yearName: '2026-27', statusName: 'Active' }];
const CLASSES = [
  { classId: 10, className: 'Class 1', sequenceOrder: 1, statusName: 'Active' },
  { classId: 11, className: 'Class 2', sequenceOrder: 2, statusName: 'Active' },
];
const FEE_TYPES = [
  { feeTypeId: 100, feeTypeName: 'Tuition Fee', feeTypeCode: 'TUITION', isActive: true },
  { feeTypeId: 101, feeTypeName: 'Exam Fee', feeTypeCode: 'EXAM', isActive: true },
  { feeTypeId: 102, feeTypeName: 'Old Fee', feeTypeCode: 'OLD', isActive: false },
];
const FREQUENCIES = [
  { frequencyId: 1, frequencyName: 'One Time', frequencyCode: 'ONE_TIME' },
  { frequencyId: 2, frequencyName: 'Monthly', frequencyCode: 'MONTHLY' },
  { frequencyId: 5, frequencyName: 'Annual', frequencyCode: 'ANNUAL' },
];

describe('FeeStructureBuilderComponent', () => {
  let fixture: ComponentFixture<FeeStructureBuilderComponent>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const el = () => fixture.nativeElement as HTMLElement;

  function create(feeStructureId?: string) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(FeeStructureBuilderComponent);
    if (feeStructureId) fixture.componentRef.setInput('feeStructureId', feeStructureId);
    fixture.detectChanges();

    http.expectOne(`${API}/api/AcademicYear`).flush(env(YEARS));
    http.expectOne(`${API}/api/Classes`).flush(env(CLASSES));
    http.expectOne(`${API}/api/FeeType`).flush(env(FEE_TYPES));
    http.expectOne(`${API}/api/FeeStructure/frequencies`).flush(env(FREQUENCIES));
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  function fillLine(i: number, feeTypeId: string, frequencyId: string, amount: string, dueDay = '10') {
    const line = cmp()['lines'].at(i);
    line.patchValue({ feeTypeId, frequencyId });   // the frequency decides whether the due day is needed
    line.patchValue({ dueDay: line.controls.dueDay.enabled ? dueDay : '', amount });
    fixture.detectChanges();
  }

  it('starts with one empty fee head and nothing sent until it is valid', () => {
    create();
    expect(cmp()['lines'].length).toBe(1);
    cmp()['save']();
    http.expectNone(`${API}/api/FeeStructure`);
    expect(cmp()['form'].invalid).toBe(true);
  });

  it('"Add Fee Head" appends a row, and the last row cannot be removed', () => {
    create();
    cmp()['addLine']();
    expect(cmp()['lines'].length).toBe(2);
    cmp()['removeLine'](0);
    cmp()['removeLine'](0);
    expect(cmp()['lines'].length).toBe(1);
  });

  it('disables a fee type that another row already uses', () => {
    create();
    cmp()['addLine']();
    fillLine(0, '100', '2', '2500');
    const options: { value: string; disabled?: boolean }[][] = cmp()['feeTypeOptions']();
    expect(options[0].find((o) => o.value === '100')?.disabled).toBe(false); // its own pick stays selectable
    expect(options[1].find((o) => o.value === '100')?.disabled).toBe(true); // taken in row 1
    expect(options[1].find((o) => o.value === '101')?.disabled).toBe(false);
    expect(options[1].some((o) => o.value === '102')).toBe(false); // inactive fee types are not offered
  });

  it('shows subtotals per frequency, never one blended number', () => {
    create();
    cmp()['addLine']();
    cmp()['addLine']();
    fillLine(0, '100', '2', '2500');
    fillLine(1, '101', '2', '500.50');
    fillLine(2, '100', '5', '10000');
    expect(cmp()['totals']()).toEqual([
      { frequency: 'Monthly', amount: '₹3,000.50' },
      { frequency: 'Annual', amount: '₹10,000.00' },
    ]);
    expect(el().textContent).toContain('not added together');
  });

  it('suggests a structure name from the class and year until the user types one', () => {
    create();
    cmp()['form'].patchValue({ academicYearId: '1', classId: '10' });
    expect(cmp()['form'].controls.structureName.value).toBe('Class 1 Fees 2026-27');
  });

  it('saves header + all rows in ONE request and returns to the list', () => {
    create();
    cmp()['addLine']();
    cmp()['form'].patchValue({ academicYearId: '1', classId: '10', structureName: 'Class 1 Fees' });
    fillLine(0, '100', '2', '2500');
    fillLine(1, '101', '1', '800');
    cmp()['save']();

    const req = http.expectOne(`${API}/api/FeeStructure`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      academicYearId: 1,
      classId: 10,
      structureName: 'Class 1 Fees',
      details: [
        { feeTypeId: 100, frequencyId: 2, amount: 2500, dueDay: 10 },   // Monthly: due day sent
        { feeTypeId: 101, frequencyId: 1, amount: 800, dueDay: null },  // One Time: none
      ],
    });
    req.flush(env({ feeStructureId: 7 }, 201));
    expect(navigate).toHaveBeenCalledWith(['/fee-setup/fee-structures']);
  });

  it('due day: enabled and required for a Monthly row, off for One Time / Annual, 1-31 only', () => {
    create();
    cmp()['addLine']();
    const [monthly, oneTime] = [cmp()['lines'].at(0), cmp()['lines'].at(1)];
    monthly.patchValue({ frequencyId: '2' });
    oneTime.patchValue({ frequencyId: '1' });
    expect(monthly.controls.dueDay.enabled).toBe(true);
    expect(oneTime.controls.dueDay.disabled).toBe(true);

    monthly.patchValue({ dueDay: '' });
    expect(monthly.controls.dueDay.hasError('required')).toBe(true);
    monthly.patchValue({ dueDay: '32' });
    expect(monthly.controls.dueDay.invalid).toBe(true);
    monthly.patchValue({ dueDay: '0' });
    expect(monthly.controls.dueDay.invalid).toBe(true);
    monthly.patchValue({ dueDay: '31' });
    expect(monthly.controls.dueDay.valid).toBe(true);

    // switching to a single-charge frequency switches the due day off and forgets it
    monthly.patchValue({ frequencyId: '5' });
    expect(monthly.controls.dueDay.disabled).toBe(true);
    expect(monthly.controls.dueDay.value).toBe('');
  });

  it('a Monthly row without a due day blocks saving', () => {
    create();
    cmp()['form'].patchValue({ academicYearId: '1', classId: '10', structureName: 'X' });
    cmp()['lines'].at(0).patchValue({ feeTypeId: '100', frequencyId: '2', amount: '2500' });
    cmp()['save']();
    http.expectNone(`${API}/api/FeeStructure`);
    expect(cmp()['form'].invalid).toBe(true);
  });

  it('rejects a negative or 3-decimal amount before calling the API', () => {
    create();
    cmp()['form'].patchValue({ academicYearId: '1', classId: '10', structureName: 'X' });
    fillLine(0, '100', '2', '-5');
    cmp()['save']();
    expect(cmp()['form'].invalid).toBe(true);
    fillLine(0, '100', '2', '10.123');
    cmp()['save']();
    expect(cmp()['form'].invalid).toBe(true);
    http.expectNone(`${API}/api/FeeStructure`);
  });

  it('edit: loads the structure, locks year and class, and PUTs name + the full set of rows', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(FeeStructureBuilderComponent);
    fixture.componentRef.setInput('feeStructureId', '7');
    fixture.detectChanges();
    http.expectOne(`${API}/api/AcademicYear`).flush(env(YEARS));
    http.expectOne(`${API}/api/Classes`).flush(env(CLASSES));
    http.expectOne(`${API}/api/FeeType`).flush(env(FEE_TYPES));
    http.expectOne(`${API}/api/FeeStructure/frequencies`).flush(env(FREQUENCIES));
    http.expectOne(`${API}/api/FeeStructure/7`).flush(
      env({
        feeStructureId: 7, academicYearId: 1, academicYearName: '2026-27', classId: 10, className: 'Class 1', structureName: 'Class 1 Fees', isActive: true, statusName: 'Active', lineCount: 1,
        details: [{ feeStructureDetailId: 1, feeTypeId: 100, feeTypeName: 'Tuition Fee', feeTypeCode: 'TUITION', frequencyId: 2, frequencyName: 'Monthly', amount: 2500, dueDay: 10 }],
      }),
    );
    fixture.detectChanges();

    expect(cmp()['form'].controls.academicYearId.disabled).toBe(true);
    expect(cmp()['form'].controls.classId.disabled).toBe(true);
    expect(cmp()['lines'].length).toBe(1);

    fillLine(0, '100', '2', '3000');
    cmp()['save']();
    const req = http.expectOne(`${API}/api/FeeStructure/7`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ structureName: 'Class 1 Fees', details: [{ feeTypeId: 100, frequencyId: 2, amount: 3000, dueDay: 10 }] });
    req.flush(env({ feeStructureId: 7 }));
  });
});
