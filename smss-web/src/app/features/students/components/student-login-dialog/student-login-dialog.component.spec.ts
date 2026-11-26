import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { StudentLoginCreated } from '../../models/student.model';
import { StudentLoginDialogComponent } from './student-login-dialog.component';
import { environment } from '../../../../../environments/environment';

const API = environment.apiUrl;
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

  it('posts to the school-scoped create-login endpoint and shows the Login ID and what happened to the parent link, never a password', () => {
    button('Create login').click();
    const req = http.expectOne(`${API}/api/Student/DPS261013001/DPS001ST007/create-login`);
    expect(req.request.method).toBe('POST');
    req.flush(
      env({ studentId: 'DPS001ST007', displayName: 'Abhinav Kumar', orgUserId: 'DPS001ST007', linkSent: true, message: 'The login was created and a link to set the password was sent to the parent.', deliveries: [{ deliveryId: 1, channel: 'WHATSAPP', status: 'SENT', templateCode: 'parent_student_login_ready', purpose: 'STUDENT_LOGIN', senderKind: 'CLOUD', createdAt: '2026-11-26T00:00:00Z' }] }),
    );
    fixture.detectChanges();

    const text = el().textContent ?? '';
    expect(text).toContain('Login ID');
    expect(text).toContain('DPS001ST007');
    expect(text).toContain('a link to set the password was sent to the parent');
    expect(text).toContain('WhatsApp: sent');
    expect(text).not.toMatch(/temporary password|ABHI0807|copy/i);

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
    expect(button('Create login')).toBeTruthy();
  });
});
