import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
import { EmployeeLoginDialogComponent } from './employee-login-dialog.component';

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

describe('EmployeeLoginDialogComponent', () => {
  let fixture: ComponentFixture<EmployeeLoginDialogComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EmployeeLoginDialogComponent);
    fixture.componentRef.setInput('employeeId', 'DPS001EM001');
    fixture.componentRef.setInput('employeeName', 'Asha Verma');
    fixture.componentRef.setInput('roles', LOOKUPS.assignableRoles);
    fixture.detectChanges();
  });
  afterEach(() => { http.verify(); clearSessions(); });

  it('lists exactly the roles it was given (nothing hardcoded)', () => {
    const names = [...el().querySelectorAll('.role .name')].map((n) => n.textContent?.trim());
    expect(names).toEqual(['Principal', 'Teacher', 'Accountant']);
  });

  it('will not create a login with no role chosen', () => {
    cmp()['create']();
    fixture.detectChanges();
    http.expectNone(`${API}/api/Employee/DPS001EM001/create-login`);
    expect(el().textContent).toContain('Choose at least one role');
  });

  it('posts the chosen roles, then shows the credentials once with the roles and the email note', () => {
    const boxes = el().querySelectorAll<HTMLInputElement>('.role input');
    boxes[1].click();
    boxes[2].click();
    fixture.detectChanges();
    cmp()['create']();
    const req = http.expectOne(`${API}/api/Employee/DPS001EM001/create-login`);
    expect(req.request.body).toEqual({ roleIds: [4, 5] });
    req.flush(env({ employeeId: 'DPS001EM001', displayName: 'Asha Verma', orgUserId: 'DPS001EM001', temporaryPassword: 'Ab3$kLm9#xQw', roles: ['Teacher', 'Accountant'], emailSent: false, emailSentTo: null }, 201));
    fixture.detectChanges();
    const text = el().textContent ?? '';
    expect(text).toContain('Copy these credentials now');
    expect(text).toContain('Ab3$kLm9#xQw');
    expect(text).toContain('Teacher, Accountant');
    expect(text).toContain('No email was sent');
  });

  it('unticking a role removes it from the request', () => {
    const boxes = el().querySelectorAll<HTMLInputElement>('.role input');
    boxes[0].click();
    boxes[1].click();
    boxes[0].click();
    cmp()['create']();
    expect(http.expectOne(`${API}/api/Employee/DPS001EM001/create-login`).request.body).toEqual({ roleIds: [4] });
  });
});
