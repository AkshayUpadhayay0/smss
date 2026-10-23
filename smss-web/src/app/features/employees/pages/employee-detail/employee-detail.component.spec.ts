import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { EmployeeDetailComponent } from './employee-detail.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const LOOKUPS = { designations: [], departments: [], employmentStatuses: [], genders: [], bloodGroups: [], documentTypes: [{ id: 9, name: 'Aadhaar', code: 'AADH', isRequired: false }],
  assignableRoles: [{ roleId: 4, roleName: 'Teacher' }] };
const EMP = {
  employeeId: 'DPS001EM001', fullName: 'Asha Verma', firstName: 'Asha', middleName: null, lastName: 'Verma', employeeType: 'Teaching',
  designationId: 1, designationName: 'Teacher', departmentId: 5, departmentName: 'Science', employmentStatusId: 3, employmentStatusName: 'Resigned',
  genderId: 1, genderName: 'Female', bloodGroupId: 1, bloodGroupName: 'O+', dateOfBirth: '1988-05-10', email: 'asha@school.test', mobileNumber: '9876543210',
  alternateMobile: null, address: '12 Park Road', dateOfJoining: '2020-04-01', dateOfLeaving: '2026-09-30', qualification: 'M.Sc', photoUrl: null,
  statusName: 'Active', loginId: null as string | null, loginRoles: [] as string[], createdAt: '2026-10-01T00:00:00Z', updatedAt: '2026-10-01T00:00:00Z',
};

describe('EmployeeDetailComponent', () => {
  let fixture: ComponentFixture<EmployeeDetailComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;

  function open(employee = EMP) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EmployeeDetailComponent);
    fixture.componentRef.setInput('employeeId', 'DPS001EM001');
    fixture.detectChanges();
    http.expectOne(`${API}/api/Employee/DPS001EM001`).flush(env(employee));
    http.expectOne(`${API}/api/Employee/lookups`).flush(env(LOOKUPS));
    fixture.detectChanges();
    http.expectOne(`${API}/api/Employee/DPS001EM001/documents`).flush(env([]));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); });

  it('shows the details with the employment status and the record status as separate things', () => {
    open();
    const text = el().textContent ?? '';
    for (const t of ['Asha Verma', 'DPS001EM001', 'Teacher · Science · Teaching', 'Resigned', 'Active record', '12 Park Road', 'M.Sc']) expect(text).toContain(t);
  });

  it("no login yet: offers Create Login (and opens the role dialog with the server's roles)", () => {
    open();
    expect(el().textContent).toContain('This employee has no login yet');
    button('Create Login')!.click();
    fixture.detectChanges();
    expect(el().querySelector('app-employee-login-dialog')).not.toBeNull();
    expect(el().textContent).toContain('Teacher');
  });

  it('has a login: shows its id and roles instead, with no Create Login button', () => {
    open({ ...EMP, loginId: 'DPS001EM001', loginRoles: ['Teacher', 'Accountant'] });
    expect(el().textContent).toContain('Teacher, Accountant');
    expect(button('Create Login')).toBeUndefined();
  });

  it('an Inactive record cannot get a login until it is activated', () => {
    open({ ...EMP, statusName: 'Inactive' });
    expect(button('Create Login')!.disabled).toBe(true);
    expect(el().textContent).toContain('Activate this record before creating a login');
  });

  it('Deactivate asks first, then POSTs toggle-status and shows the new state', async () => {
    open();
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    await cmp()['toggleStatus']();
    const req = http.expectOne(`${API}/api/Employee/DPS001EM001/toggle-status`);
    expect(req.request.method).toBe('POST');
    req.flush(env({ ...EMP, statusName: 'Inactive' }));
    fixture.detectChanges();
    expect(el().textContent).toContain('Inactive record');
  });

  it('declining the confirmation sends nothing', async () => {
    open();
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    await cmp()['toggleStatus']();
    http.expectNone(`${API}/api/Employee/DPS001EM001/toggle-status`);
  });
});
