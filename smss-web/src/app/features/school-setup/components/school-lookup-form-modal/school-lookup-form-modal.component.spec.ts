import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { FEE_TYPE_CONFIG, SUBJECT_CONFIG } from '../../configs/school-lookups.config';
import { SchoolLookupConfig, SchoolLookupItem } from '../../models/school-lookup.model';
import { SchoolLookupFormModalComponent } from './school-lookup-form-modal.component';

const API = environment.apiUrl;

describe('SchoolLookupFormModalComponent', () => {
  let fixture: ComponentFixture<SchoolLookupFormModalComponent>;
  let http: HttpTestingController;
  // protected members
  const cmp = () => fixture.componentInstance as unknown as { form: any; submit(): void };

  function open(config: SchoolLookupConfig, item: SchoolLookupItem | null = null) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SchoolLookupFormModalComponent);
    fixture.componentRef.setInput('config', config);
    fixture.componentRef.setInput('item', item);
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('Fee Type: the code is required — nothing is sent without it', () => {
    open(FEE_TYPE_CONFIG);
    cmp().form.patchValue({ name: 'Tuition Fee', code: '' });
    cmp().submit();
    http.expectNone(`${API}/api/FeeType`);
    expect(cmp().form.controls.code.hasError('required')).toBe(true);
  });

  it('Fee Type: creates with name, code and description against /api/FeeType', () => {
    open(FEE_TYPE_CONFIG);
    cmp().form.patchValue({ name: ' Tuition Fee ', code: ' TUITION ', description: 'Monthly' });
    cmp().submit();
    const req = http.expectOne(`${API}/api/FeeType`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ feeTypeName: 'Tuition Fee', feeTypeCode: 'TUITION', description: 'Monthly' });
    req.flush({ status: true, statusCode: 201, message: 'ok', data: { feeTypeId: 1, schoolId: 'S', feeTypeName: 'Tuition Fee', feeTypeCode: 'TUITION' } });
  });

  it('Fee Type: accepts a 30 character code (the API limit), refuses 31', () => {
    open(FEE_TYPE_CONFIG);
    cmp().form.patchValue({ name: 'X', code: 'A'.repeat(30) });
    expect(cmp().form.controls.code.valid).toBe(true);
    cmp().form.patchValue({ code: 'A'.repeat(31) });
    expect(cmp().form.controls.code.hasError('maxlength')).toBe(true);
  });

  it('Fee Type: editing fills the form from the record and PUTs to its id', () => {
    open(FEE_TYPE_CONFIG, { schoolId: 'S', feeTypeId: 7, feeTypeName: 'Exam', feeTypeCode: 'EXAM', description: null });
    expect(cmp().form.getRawValue()).toEqual({ name: 'Exam', code: 'EXAM', description: '' });
    cmp().submit();
    const req = http.expectOne(`${API}/api/FeeType/7`);
    expect(req.request.method).toBe('PUT');
    req.flush({ status: true, statusCode: 200, message: 'ok', data: { feeTypeId: 7, schoolId: 'S' } });
  });

  it('Subject: the code stays optional', () => {
    open(SUBJECT_CONFIG);
    cmp().form.patchValue({ name: 'Maths', code: '' });
    expect(cmp().form.controls.code.valid).toBe(true);
  });
});
