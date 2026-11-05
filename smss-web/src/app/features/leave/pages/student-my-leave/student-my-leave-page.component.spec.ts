import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { StudentMyLeavePageComponent } from './student-my-leave-page.component';

const API = environment.apiUrl;

describe('StudentMyLeavePageComponent', () => {
  afterEach(() => clearSessions());

  it('is the "My Leave" header around the shared own-leave component', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    const http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(StudentMyLeavePageComponent);
    f.detectChanges();
    http.expectOne(`${API}/api/StudentLeave/my`).flush({ status: true, statusCode: 200, message: 'ok', data: [] });
    http.expectOne((r) => r.url === `${API}/api/LeaveType`).flush({ status: true, statusCode: 200, message: 'ok', data: [] });
    f.detectChanges();
    const t = (f.nativeElement as HTMLElement).textContent ?? '';
    expect(t).toContain('My Leave');
    expect(f.nativeElement.querySelector('app-student-my-leave')).not.toBeNull();
    http.verify();
  });
});
