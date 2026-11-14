import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { EmployeeListComponent } from './employee-list.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const item = (name: string) => ({
  employeeId: 'E1', fullName: name, employeeType: 'Teaching', designationId: 1, designationName: 'Teacher', departmentId: 5, departmentName: 'Science',
  employmentStatusName: 'Active', mobileNumber: '9876543210', email: null, dateOfJoining: '2020-04-01', statusName: 'Active', photoUrl: null, hasLogin: false,
});

describe('EmployeeListComponent search', () => {
  afterEach(() => clearSessions());

  it('a newer search cancels the older request: a slow older answer cannot replace the newer result', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(EmployeeListComponent);
    f.detectChanges();
    http.expectOne(`${API}/api/Employee/lookups`).flush(
      env({ designations: [], departments: [], employmentStatuses: [], genders: [], bloodGroups: [], documentTypes: [], assignableRoles: [] }),
    );
    const lists = () => http.match((r) => r.url === `${API}/api/Employee`);
    lists()[0].flush(env({ items: [item('Initial row')], total: 1, page: 1, pageSize: 10 }));

    const cmp = f.componentInstance as unknown as { searchControl: { setValue: (v: string) => void }; load: () => void };
    cmp.searchControl.setValue('older');
    cmp.load();
    const [older] = lists();
    cmp.searchControl.setValue('newer');
    cmp.load();
    const reqs = lists();
    const newer = reqs[reqs.length - 1];

    expect(older.cancelled).toBe(true);
    expect(newer.request.params.get('search')).toBe('newer');
    newer.flush(env({ items: [item('Newer result')], total: 1, page: 1, pageSize: 10 }));
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).textContent).toContain('Newer result');
    http.verify();
  });
});
