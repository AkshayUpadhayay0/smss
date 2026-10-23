import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
import { EmployeeListComponent } from './employee-list.component';

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

const ITEM = { employeeId: 'DPS001EM001', fullName: 'Asha Verma', employeeType: 'Teaching', designationId: 1, designationName: 'Teacher', departmentId: 5, departmentName: 'Science',
  employmentStatusName: 'Resigned', mobileNumber: '9876543210', email: null, dateOfJoining: '2020-04-01', statusName: 'Active', photoUrl: null, hasLogin: true };

describe('EmployeeListComponent', () => {
  let fixture: ComponentFixture<EmployeeListComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const listReqs = () => http.match((r) => r.url === `${API}/api/Employee`);

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(EmployeeListComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Employee/lookups`).flush(env(LOOKUPS));
  });
  afterEach(() => { http.verify(); clearSessions(); });

  function firstPage(total = 1) {
    const [req] = listReqs();
    req.flush(env({ items: [ITEM], total, page: 1, pageSize: 10 }));
    fixture.detectChanges();
    return req;
  }

  it('loads page 1 (10 per page) and shows the staff with their employment status and login', () => {
    const req = firstPage();
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('10');
    const text = el().textContent ?? '';
    for (const t of ['DPS001EM001', 'Asha Verma', 'Teacher', 'Science', 'Resigned', '1 employee', 'Yes']) expect(text).toContain(t);
    expect(cmp()['rows']()[0].login).toBe('Yes');
  });

  it('a filter reloads from the SERVER with that filter, back on page 1; clearing drops them', () => {
    firstPage(35);
    cmp()['onPage'](3);
    let [req] = listReqs();
    expect(req.request.params.get('page')).toBe('3');
    req.flush(env({ items: [ITEM], total: 35, page: 3, pageSize: 10 }));

    cmp()['designationControl'].setValue('2');
    [req] = listReqs();
    expect(req.request.params.get('designationId')).toBe('2');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(env({ items: [], total: 0, page: 1, pageSize: 10 }));
    fixture.detectChanges();
    expect(el().textContent).toContain('No employees match these filters.');

    cmp()['employmentStatusControl'].setValue('3');
    cmp()['statusControl'].setValue('Inactive');
    const reqs = listReqs();
    const last = reqs[reqs.length - 1];
    expect(last.request.params.get('employmentStatusId')).toBe('3');
    expect(last.request.params.get('status')).toBe('Inactive');
    reqs.forEach((r) => r.flush(env({ items: [], total: 0, page: 1, pageSize: 10 })));

    cmp()['clearFilters']();
    [req] = listReqs();
    for (const k of ['designationId', 'employmentStatusId', 'status']) expect(req.request.params.has(k)).toBe(false);
    req.flush(env({ items: [], total: 0, page: 1, pageSize: 10 }));
    fixture.detectChanges();
    expect(el().textContent).toContain('No employees yet');
  });

  it('page size changes go back to page 1 with the new size', () => {
    firstPage(35);
    cmp()['onPageSize'](25);
    const [req] = listReqs();
    expect(req.request.params.get('pageSize')).toBe('25');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(env({ items: [ITEM], total: 35, page: 1, pageSize: 25 }));
  });

  it('offers the designation / department / employment status choices from the server lookups', () => {
    firstPage();
    expect(cmp()['designationOptions']().map((o: { label: string }) => o.label)).toEqual(['All', 'Teacher', 'Principal']);
    expect(cmp()['employmentStatusOptions']().map((o: { label: string }) => o.label)).toEqual(['All', 'Active', 'On Leave', 'Resigned']);
  });
});
