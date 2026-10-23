import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
import { Router } from '@angular/router';
import { EmployeeFormComponent } from './employee-form.component';

const LOOKUPS = {
  designations: [{ id: 1, name: 'Teacher' }, { id: 2, name: 'Principal' }],
  departments: [{ id: 5, name: 'Science' }],
  employmentStatuses: [{ id: 1, name: 'Active' }, { id: 2, name: 'On Leave' }, { id: 3, name: 'Resigned' }],
  genders: [{ id: 1, name: 'Female' }],
  bloodGroups: [{ id: 1, name: 'O+' }],
  documentTypes: [{ id: 9, name: 'Aadhaar', code: 'AADH', isRequired: true }],
  assignableRoles: [{ roleId: 3, roleName: 'Principal', description: 'Runs the school' }, { roleId: 4, roleName: 'Teacher' }, { roleId: 5, roleName: 'Accountant' }],
};
const EMP = {
  employeeId: 'DPS001EM001', fullName: 'Asha Verma', firstName: 'Asha', middleName: null, lastName: 'Verma', employeeType: 'Teaching' as const,
  designationId: 1, designationName: 'Teacher', departmentId: 5, departmentName: 'Science', employmentStatusId: 1, employmentStatusName: 'Active',
  genderId: 1, genderName: 'Female', bloodGroupId: 1, bloodGroupName: 'O+', dateOfBirth: '1988-05-10', email: 'asha@school.test', mobileNumber: '9876543210',
  alternateMobile: null, address: '12 Park Road', dateOfJoining: '2020-04-01', dateOfLeaving: null, qualification: 'M.Sc', photoUrl: null,
  statusName: 'Active', loginId: null, loginRoles: [] as string[], createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-01T00:00:00Z',
};

describe('EmployeeFormComponent', () => {
  let fixture: ComponentFixture<EmployeeFormComponent>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(employeeId?: string) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(EmployeeFormComponent);
    if (employeeId) fixture.componentRef.setInput('employeeId', employeeId);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Employee/lookups`).flush(env(LOOKUPS));
    if (employeeId) http.expectOne(`${API}/api/Employee/${employeeId}`).flush(env(EMP));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  const fill = () => cmp()['form'].patchValue({ firstName: ' Asha ', lastName: 'Verma', dateOfBirth: '1988-05-10', employeeType: 'Teaching', designationId: '1', departmentId: '5',
    dateOfJoining: '2020-04-01', mobileNumber: '9876543210', address: '12 Park Road', email: '', qualification: 'M.Sc' });

  it('a new employee starts with employment status Active preselected', () => {
    open();
    expect(cmp()['form'].controls.employmentStatusId.value).toBe('1');
    expect(el().textContent).toContain('Add Employee');
  });

  it('will not submit an incomplete form (nothing is sent), and shows what is missing', () => {
    open();
    cmp()['submit']();
    fixture.detectChanges();
    http.expectNone(`${API}/api/Employee`);
    expect(cmp()['err']('firstName')).toBeTruthy();
    expect(cmp()['err']('mobileNumber')).toBeTruthy();
    expect(cmp()['err']('designationId')).toBeTruthy();
  });

  it('rejects a bad mobile, a future date of birth and a leaving date before joining', () => {
    open();
    fill();
    cmp()['form'].patchValue({ mobileNumber: '12345', dateOfBirth: '2999-01-01' });
    cmp()['submit']();
    http.expectNone(`${API}/api/Employee`);
    expect(cmp()['err']('mobileNumber')).toBeTruthy();
    expect(cmp()['err']('dateOfBirth')).toContain('past');

    cmp()['form'].patchValue({ mobileNumber: '9876543210', dateOfBirth: '1988-05-10', dateOfLeaving: '2019-01-01' });
    cmp()['submit']();
    http.expectNone(`${API}/api/Employee`);
    expect(cmp()['err']('dateOfLeaving')).toContain('before');
  });

  it('create POSTs numbers for ids, trims, sends blanks as null, then opens the new employee', () => {
    open();
    fill();
    cmp()['submit']();
    const req = http.expectOne(`${API}/api/Employee`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toMatchObject({
      designationId: 1, departmentId: 5, employmentStatusId: 1, employeeType: 'Teaching', firstName: 'Asha', middleName: null, lastName: 'Verma',
      genderId: null, bloodGroupId: null, email: null, alternateMobile: null, dateOfLeaving: null, mobileNumber: '9876543210', qualification: 'M.Sc',
    });
    req.flush(env(EMP, 201));
    expect(navigate).toHaveBeenCalledWith(['/employees', 'DPS001EM001']);
  });

  it('a photo picked for a new employee is uploaded after the save; if that fails the employee is still saved', () => {
    open();
    fill();
    cmp()['onPhotoChanged']({ file: new File(['x'], 'me.png'), remove: false });
    cmp()['submit']();
    http.expectOne(`${API}/api/Employee`).flush(env(EMP, 201));
    http.expectOne(`${API}/api/Employee/DPS001EM001/photo`).flush({ status: false, statusCode: 400, message: 'bad', data: null }, { status: 400, statusText: 'Bad Request' });
    expect(navigate).toHaveBeenCalledWith(['/employees', 'DPS001EM001']);
  });

  it('edit loads the employee into the form and PUTs the changes', () => {
    open('DPS001EM001');
    const f = cmp()['form'].getRawValue();
    expect(f).toMatchObject({ firstName: 'Asha', designationId: '1', employmentStatusId: '1', email: 'asha@school.test', dateOfJoining: '2020-04-01' });
    expect(el().textContent).toContain('Edit Employee');
    cmp()['form'].patchValue({ employmentStatusId: '3', dateOfLeaving: '2026-09-30' });
    cmp()['submit']();
    const req = http.expectOne(`${API}/api/Employee/DPS001EM001`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toMatchObject({ employmentStatusId: 3, dateOfLeaving: '2026-09-30' });
    req.flush(env({ ...EMP, employmentStatusId: 3 }));
  });
});
