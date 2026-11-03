import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MyProfileComponent } from './my-profile.component';

const URL = `${environment.apiUrl}/api/Employee/me`;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const ME = {
  employeeId: 'LAS001EM001', fullName: 'Sunita Rawat', firstName: 'Sunita', middleName: null, lastName: 'Rawat', employeeType: 'Teaching',
  designationId: 3, designationName: 'Pre-Primary Teacher', departmentId: 1, departmentName: 'Teaching', employmentStatusId: 1, employmentStatusName: 'Active',
  genderId: 2, genderName: 'Female', bloodGroupId: 1, bloodGroupName: 'A+', dateOfBirth: '1990-03-04', email: null, mobileNumber: '9876543210',
  alternateMobile: null, address: '12 Staff Colony', dateOfJoining: '2020-06-01', dateOfLeaving: null, qualification: 'NTT, B.Ed', photoUrl: null,
  statusName: 'Active', loginId: 'LAS001EM001', loginRoles: ['Teacher'], createdAt: '2026-11-03T00:00:00Z', updatedAt: '2026-11-03T00:00:00Z',
};

describe('MyProfileComponent', () => {
  let fixture: ComponentFixture<MyProfileComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';

  function open(respond: (req: ReturnType<HttpTestingController['expectOne']>) => void) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyProfileComponent);
    fixture.detectChanges();
    respond(http.expectOne(URL));
    fixture.detectChanges();
  }

  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('asks only for /api/Employee/me (never an id) and shows the name, designation, department, contact details and joining date', () => {
    open((r) => r.flush(env(ME)));
    expect(text()).toContain('Sunita Rawat');
    expect(text()).toContain('Pre-Primary Teacher');
    expect(text()).toContain('Teaching');
    expect(text()).toContain('9876543210');
    expect(text()).toContain('12 Staff Colony');
    expect(text()).toContain('01 Jun 2020');
    expect(el().querySelector('.photo .initials')?.textContent?.trim()).toBe('SR');
  });

  it('shows the photo when there is one', () => {
    open((r) => r.flush(env({ ...ME, photoUrl: '/uploads/x/employees/E1/photo_1.png' })));
    expect(el().querySelector('.photo img')?.getAttribute('src')).toBe(`${environment.apiUrl}/uploads/x/employees/E1/photo_1.png`);
  });

  it('is read-only: no buttons, no edit, deactivate, photo, document or login actions', () => {
    open((r) => r.flush(env(ME)));
    expect(el().querySelectorAll('button')).toHaveLength(0);
    const t = text().toLowerCase();
    for (const word of ['edit', 'deactivate', 'create login', 'upload', 'documents', 'system login']) expect(t).not.toContain(word);
  });

  it('says the profile could not be loaded when the request fails', () => {
    open((r) => r.flush(env(null), { status: 404, statusText: 'Not Found' }));
    expect(text()).toContain('Your profile could not be loaded.');
  });
});
