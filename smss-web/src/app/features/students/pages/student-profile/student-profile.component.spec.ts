import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { StudentProfileComponent } from './student-profile.component';

const API = 'https://localhost:7037';
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

describe('StudentProfileComponent', () => {
  let fixture: ComponentFixture<StudentProfileComponent>;
  let http: HttpTestingController;

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentProfileComponent);
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('loads the profile by the string student id (the route param is not converted to a number)', () => {
    fixture.componentRef.setInput('studentId', 'DPS001ST001');
    fixture.detectChanges();

    const req = http.expectOne(`${API}/api/Student/DPS261013001/DPS001ST001`);
    req.flush(
      env({
        studentId: 'DPS001ST001', admissionNumber: 'ADMNO20260001', fullName: 'Abhinav Kumar', dateOfBirth: '1998-07-08',
        admissionDate: '2026-04-01', statusName: 'Active', loginId: 'DPS001ST001', guardians: [],
      }),
    );
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Abhinav Kumar');
    expect(text).toContain('DPS001ST001');
    expect(text).not.toContain('could not be loaded');
  });
});
