import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { FeeConcession, formatConcessionValue } from '../../models/fee-concession.model';
import { FeeConcessionFormModalComponent } from './fee-concession-form-modal.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const FEE_TYPES = [
  { feeTypeId: 100, feeTypeName: 'Tuition Fee', feeTypeCode: 'TUITION', isActive: true },
  { feeTypeId: 102, feeTypeName: 'Old Fee', feeTypeCode: 'OLD', isActive: false },
];

describe('formatConcessionValue', () => {
  it('formats percentages and rupee amounts', () => {
    expect(formatConcessionValue('PERCENTAGE', 15)).toBe('15%');
    expect(formatConcessionValue('PERCENTAGE', 12.5)).toBe('12.5%');
    expect(formatConcessionValue('FIXED', 2000)).toBe('₹2,000');
    expect(formatConcessionValue('FIXED', 1234567.5)).toBe('₹12,34,567.5');
  });
});

describe('FeeConcessionFormModalComponent', () => {
  let fixture: ComponentFixture<FeeConcessionFormModalComponent>;
  let http: HttpTestingController;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const el = () => fixture.nativeElement as HTMLElement;

  function open(concession: FeeConcession | null = null) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(FeeConcessionFormModalComponent);
    fixture.componentRef.setInput('concession', concession);
    fixture.detectChanges();
    http.expectOne(`${API}/api/FeeType`).flush(env(FEE_TYPES));
    fixture.detectChanges();
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  const fill = (v: Record<string, string>) => {
    cmp()['form'].patchValue(v);
    fixture.detectChanges();
  };

  it('switches the value label and limit with the type', () => {
    open();
    expect(el().textContent).toContain('Value (%)');
    fill({ type: 'FIXED' });
    expect(el().textContent).toContain('Value (₹)');
  });

  it('percentage: 0 and above 100 are refused, 100 is accepted', () => {
    open();
    fill({ name: 'Sibling', code: 'SIB', value: '0' });
    expect(cmp()['form'].controls.value.hasError('value')).toBe(true);
    fill({ value: '100.5' });
    expect(cmp()['form'].controls.value.hasError('value')).toBe(true);
    fill({ value: '100' });
    expect(cmp()['form'].controls.value.valid).toBe(true);
  });

  it('fixed: no upper limit, but must be above 0; switching type re-checks the value', () => {
    open();
    fill({ name: 'Merit', code: 'MERIT', type: 'FIXED', value: '5000' });
    expect(cmp()['form'].controls.value.hasError('value')).toBe(false);
    fill({ value: '150' });
    expect(cmp()['form'].controls.value.valid).toBe(true);
    fill({ type: 'FIXED', value: '250000' });
    expect(cmp()['form'].controls.value.valid).toBe(true);
    fill({ type: 'PERCENTAGE' });
    expect(cmp()['form'].controls.value.hasError('value')).toBe(true); // 250000 % is not allowed
    fill({ type: 'FIXED', value: '-1' });
    expect(cmp()['form'].controls.value.hasError('value')).toBe(true);
  });

  it('offers All Fee Heads and the active fee types only', () => {
    open();
    expect(cmp()['feeTypeOptions']().map((o: { label: string }) => o.label)).toEqual(['Tuition Fee']);
    expect(el().querySelector('select option')?.textContent).toContain('All Fee Heads');
  });

  it('creates with applicableFeeTypeId null for "All Fee Heads"', () => {
    open();
    fill({ name: ' Sibling Discount ', code: 'SIBLING', type: 'PERCENTAGE', value: '15', feeTypeId: '' });
    cmp()['submit']();
    const req = http.expectOne(`${API}/api/FeeConcession`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ concessionName: 'Sibling Discount', concessionCode: 'SIBLING', concessionType: 'PERCENTAGE', value: 15, applicableFeeTypeId: null });
    req.flush(env({ concessionId: 1 }, 201));
  });

  it('edit: fills the form and PUTs to the concession id with the chosen fee head', () => {
    open({
      concessionId: 9, schoolId: 'S', concessionName: 'Merit', concessionCode: 'MERIT', concessionType: 'FIXED', value: 2000,
      applicableFeeTypeId: 102, applicableFeeTypeName: 'Old Fee', isActive: true, statusName: 'Active', createdAt: '', updatedAt: '',
    });
    expect(cmp()['form'].getRawValue()).toEqual({ name: 'Merit', code: 'MERIT', type: 'FIXED', value: '2000', feeTypeId: '102' });
    // the inactive fee head it already uses stays visible
    expect(cmp()['feeTypeOptions']().map((o: { label: string }) => o.label)).toContain('Old Fee (inactive)');
    cmp()['submit']();
    const req = http.expectOne(`${API}/api/FeeConcession/9`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.applicableFeeTypeId).toBe(102);
    req.flush(env({ concessionId: 9 }));
  });

  it('sends nothing when required fields are missing', () => {
    open();
    cmp()['submit']();
    http.expectNone(`${API}/api/FeeConcession`);
    expect(cmp()['form'].invalid).toBe(true);
  });
});
