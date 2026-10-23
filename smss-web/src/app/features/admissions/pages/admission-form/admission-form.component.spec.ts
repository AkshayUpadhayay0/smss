import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ToastService } from '../../../../core/services/toast.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { AdmissionFormComponent } from './admission-form.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

const YEARS = [{ academicYearId: 5, yearName: '2025-26', isCurrent: false, statusName: 'Active' }, { academicYearId: 6, yearName: '2026-27', isCurrent: true, statusName: 'Active' }];
const CLASSES = [{ classId: 3, className: 'Class 1', statusName: 'Active' }, { classId: 4, className: 'Class 2', statusName: 'Active' }];
const TYPES = [{ admissionTypeId: 1, typeName: 'New Admission', statusName: 'Active' }];
const DETAIL = (statusName: string) => ({
  admissionId: 7, applicationNumber: 'ADM20260007', applicantFullName: 'Anil Kumar Applicant', statusName, applicationDate: '2026-10-20', academicYearId: 5, appliedClassId: 4, admissionTypeId: 1,
  applicantFirstName: 'Anil', applicantMiddleName: 'Kumar', applicantLastName: 'Applicant', dateOfBirth: '2018-05-06T00:00:00', genderId: 2, aadharNumberMasked: 'XXXX XXXX 7777', email: 'a@example.com',
  mobileNumber: '9123400000', countryId: null, guardianName: 'Gita Applicant', guardianRelationship: 'MOTHER', guardianMobileNumber: '9123400001', guardianEmail: null, previousSchool: 'Little Steps',
});

describe('AdmissionFormComponent', () => {
  let fixture: ComponentFixture<AdmissionFormComponent>;
  let http: HttpTestingController;
  let router: Router;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  /** Answers every outstanding request (lookups first, then whatever they trigger) from the canned data; anything unknown is an empty list. */
  function answer(extra: Record<string, unknown> = {}) {
    const canned: Record<string, unknown> = { '/api/AcademicYear': YEARS, '/api/Classes': CLASSES, '/api/AdmissionTypes': TYPES, ...extra };
    for (let round = 0; round < 8; round++) {
      fixture.detectChanges();
      const pending = http.match(() => true);
      if (pending.length === 0) break;
      for (const req of pending) {
        const key = Object.keys(canned).find((k) => req.request.url.includes(k));
        req.flush(env(key ? canned[key] : []));
      }
    }
  }

  function open(admissionId?: string) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Admission Staff'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(AdmissionFormComponent);
    if (admissionId) fixture.componentRef.setInput('admissionId', admissionId);
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('a new application defaults to the current academic year and posts the previous school with the rest', () => {
    open();
    answer();
    expect(cmp()['editing']()).toBe(false);
    expect(cmp()['form'].controls.academicYearId.value).toBe('6');
    cmp()['form'].patchValue({ applicantFirstName: 'Anil', applicantLastName: 'Applicant', dateOfBirth: '2018-05-06', appliedClassId: '3', admissionTypeId: '1', previousSchool: ' Little Steps ', guardianName: 'Gita', guardianRelationship: 'MOTHER', guardianMobileNumber: '9123400001' });
    cmp()['submit']();
    const post = http.expectOne(`${API}/api/Admission/S/submit`);
    expect(post.request.body).toMatchObject({ applicantFirstName: 'Anil', appliedClassId: 3, academicYearId: 6, previousSchool: 'Little Steps' });
    post.flush(env({ ...DETAIL('APPLICATION_SUBMITTED'), admissionId: 12, applicationNumber: 'ADM20260012' }, 201));
    expect(router.navigate).toHaveBeenCalledWith(['/admissions', 12]);
  });

  it('edit mode loads the application into the form (its own academic year, not the current one) and the Aadhaar box stays empty with the masked number as a hint', () => {
    open('7');
    answer({ '/api/Admission/S/7': DETAIL('UNDER_REVIEW') });
    expect(cmp()['editing']()).toBe(true);
    expect(cmp()['form'].getRawValue()).toMatchObject({
      applicantFirstName: 'Anil', applicantMiddleName: 'Kumar', applicantLastName: 'Applicant', dateOfBirth: '2018-05-06', genderId: '2', academicYearId: '5', appliedClassId: '4', admissionTypeId: '1',
      previousSchool: 'Little Steps', guardianName: 'Gita Applicant', guardianRelationship: 'MOTHER', aadharNumber: '',
    });
    expect(cmp()['storedAadhaarMasked']()).toBe('XXXX XXXX 7777');
  });

  it('saving an edit PUTs to the application, sends no Aadhaar when the box is empty (the server keeps the stored one), and returns to the application', () => {
    open('7');
    answer({ '/api/Admission/S/7': DETAIL('APPLICATION_SUBMITTED') });
    cmp()['form'].patchValue({ applicantLastName: 'Changed', previousSchool: '' });
    cmp()['submit']();
    const put = http.expectOne(`${API}/api/Admission/S/7`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toMatchObject({ applicantLastName: 'Changed', aadharNumber: null, previousSchool: null, academicYearId: 5, appliedClassId: 4 });
    put.flush(env(DETAIL('APPLICATION_SUBMITTED')));
    expect(router.navigate).toHaveBeenCalledWith(['/admissions', 7]);
  });

  it('an application that has been decided cannot be edited: the user is told and sent back to it, the form stays empty', () => {
    open('7');
    const error = vi.spyOn(TestBed.inject(ToastService), 'error');
    answer({ '/api/Admission/S/7': DETAIL('APPROVED') });
    expect(error).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/admissions', 7]);
    expect(cmp()['form'].controls.applicantFirstName.value).toBe('');
  });

  it('a refused save (409 after a decision elsewhere) keeps the form open', () => {
    open('7');
    answer({ '/api/Admission/S/7': DETAIL('UNDER_REVIEW') });
    cmp()['submit']();
    http.expectOne(`${API}/api/Admission/S/7`).flush({ status: false, statusCode: 409, message: 'can no longer be edited', data: null }, { status: 409, statusText: 'Conflict' });
    expect(router.navigate).not.toHaveBeenCalled();
    expect(cmp()['saving']()).toBe(false);
  });
});
