import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { StudentLoginCreated } from '../../models/student.model';
import { StudentLoginDialogComponent } from './student-login-dialog.component';

const API = 'https://localhost:7037';
const env = <T>(data: T, statusCode = 201) => ({ status: true, statusCode, message: 'ok', data });

describe('StudentLoginDialogComponent', () => {
  let fixture: ComponentFixture<StudentLoginDialogComponent>;
  let http: HttpTestingController;
  let created: StudentLoginCreated[];
  const el = () => fixture.nativeElement as HTMLElement;
  const button = (text: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(text)) as HTMLButtonElement;

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentLoginDialogComponent);
    fixture.componentRef.setInput('studentId', 'DPS001ST007');
    fixture.componentRef.setInput('studentName', 'Abhinav Kumar');
    created = [];
    fixture.componentInstance.created.subscribe((c) => created.push(c));
    fixture.detectChanges();
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('asks for confirmation first and sends nothing until "Create login" is clicked', () => {
    expect(el().textContent).toContain('Abhinav Kumar');
    http.expectNone(() => true);
  });

  it('posts to the school-scoped create-login endpoint and shows the Login ID and password once', () => {
    button('Create login').click();
    const req = http.expectOne(`${API}/api/Student/DPS261013001/DPS001ST007/create-login`);
    expect(req.request.method).toBe('POST');
    req.flush(
      env({ studentId: 'DPS001ST007', displayName: 'Abhinav Kumar', orgUserId: 'DPS001ST007', temporaryPassword: 'ABHI0807', emailSent: false }),
    );
    fixture.detectChanges();

    const text = el().textContent ?? '';
    expect(text).toContain('Login ID');
    expect(text).toContain('DPS001ST007');
    expect(text).toContain('ABHI0807');
    expect(text).toContain('shown only once');

    button('Done').click();
    expect(created).toHaveLength(1);
    expect(created[0].orgUserId).toBe('DPS001ST007');
  });

  it('stays on the confirmation step when the server refuses (e.g. the student already has a login)', () => {
    button('Create login').click();
    http
      .expectOne(`${API}/api/Student/DPS261013001/DPS001ST007/create-login`)
      .flush({ status: false, statusCode: 409, message: 'This student already has a login.', data: null }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(el().textContent).not.toContain('ABHI0807');
    expect(button('Create login')).toBeTruthy();
  });
});
