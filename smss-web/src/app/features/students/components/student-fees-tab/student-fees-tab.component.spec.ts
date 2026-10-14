import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
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
    fixture.detectChanges();
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
    expect(cmp()['concessionOpen']()).toBe(false);
  });

  it('removing a concession asks first, then DELETEs it', async () => {
    open(assigned);
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    await cmp()['removeConcession'](assigned.concessions[0]);
    const req = http.expectOne(`${URL}/concessions/9`);
    expect(req.request.method).toBe('DELETE');
    req.flush(env({ ...assigned, concessions: [] }));
    fixture.detectChanges();
    expect(text()).toContain('No concessions applied');
  });

  it('a student with no enrollment gets an explanation, not an assign button', () => {
    open({ ...base, academicYearId: null, classId: null, className: null, academicYearName: null });
    expect(text()).toContain('not enrolled in any class');
    expect(text()).not.toContain('Assign Fee Structure');
  });
});
