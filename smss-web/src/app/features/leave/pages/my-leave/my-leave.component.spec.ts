import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { LeaveApplyModalComponent } from '../../components/leave-apply-modal/leave-apply-modal.component';
import { MyLeaveComponent } from './my-leave.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

const TYPES = [
  { leaveTypeId: 1, typeName: 'Casual Leave', typeCode: 'EMP_CASUAL', appliesToEmployee: true, appliesToStudent: false },
  { leaveTypeId: 2, typeName: 'Sick Leave', typeCode: 'EMP_SICK', appliesToEmployee: true, appliesToStudent: false },
];
const req = (id: number, status: string, over = {}) => ({
  leaveRequestId: id, leaveTypeId: 1, leaveTypeName: 'Casual Leave', fromDate: '2026-10-26', toDate: '2026-10-28', days: 3, reason: 'Family function', status, appliedBy: 7, appliedByName: 'Anita Staff',
  appliedAt: '2026-10-20T04:00:00Z', decidedBy: null, decidedByName: null, decidedAt: null, decisionReason: null, employeeId: 'S001EM001', employeeName: 'Anita Staff', ...over,
});

describe('MyLeaveComponent', () => {
  let fixture: ComponentFixture<MyLeaveComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(list: unknown = [req(1, 'Pending'), req(2, 'Approved', { decidedByName: 'Pria Staff', decisionReason: 'Enjoy' }), req(3, 'Rejected', { decidedByName: 'Pria Staff', decisionReason: 'Exam week' })]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyLeaveComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/EmployeeLeave/my`).flush(env(list));
    http.expectOne((r) => r.url === `${API}/api/LeaveType` && r.params.get('appliesTo') === 'employee').flush(env(TYPES));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('lists my requests with dates, days, status and the decision (who decided and why)', () => {
    open();
    const rows = cmp()['rows']();
    expect(rows).toHaveLength(3);
    expect(rows[0].dates).toBe('26 Oct 2026 – 28 Oct 2026');
    expect(rows[0].days).toBe(3);
    expect(rows[1].decision).toBe('Approved by Pria Staff: Enjoy');
    expect(rows[2].decision).toBe('Rejected by Pria Staff: Exam week');
    expect(el().textContent).toContain('1 request is waiting for a decision');
  });

  it('only a Pending request has a Cancel button', () => {
    open();
    const cancels = [...el().querySelectorAll('button')].filter((b) => b.textContent?.includes('Cancel request'));
    expect(cancels).toHaveLength(1);
  });

  it('Cancel asks first, then posts to cancel and shows the request as Cancelled', async () => {
    open();
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    const done = cmp()['cancel'](cmp()['rows']()[0]);
    await Promise.resolve();
    const post = http.expectOne(`${API}/api/EmployeeLeave/1/cancel`);
    expect(post.request.method).toBe('POST');
    post.flush(env(req(1, 'Cancelled')));
    await done;
    expect(confirm).toHaveBeenCalled();
    expect(cmp()['requests']().find((r: { leaveRequestId: number }) => r.leaveRequestId === 1).status).toBe('Cancelled');
  });

  it('declining the confirmation sends nothing', async () => {
    open();
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    await cmp()['cancel'](cmp()['rows']()[0]);
    http.expectNone(`${API}/api/EmployeeLeave/1/cancel`);
  });

  it('Apply opens the form; submitting posts for the caller (no employeeId), with trimmed reason, then reloads', () => {
    open();
    cmp()['applying'].set(true);
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-leave-apply-modal').componentInstance as LeaveApplyModalComponent & Record<string, any>;
    modal['form'].patchValue({ leaveTypeId: '2', fromDate: '2026-11-02', toDate: '2026-11-03', reason: '  Fever  ' });
    modal['submit']();
    const post = http.expectOne(`${API}/api/EmployeeLeave`);
    expect(post.request.body).toEqual({ leaveTypeId: 2, fromDate: '2026-11-02', toDate: '2026-11-03', reason: 'Fever' });
    post.flush(env(req(9, 'Pending'), 201));
    http.expectOne(`${API}/api/EmployeeLeave/my`).flush(env([req(9, 'Pending')]));
    fixture.detectChanges();
    expect(cmp()['applying']()).toBe(false);
    expect(cmp()['requests']()).toHaveLength(1);
  });

  it('the form refuses a to date before the from date, and a blank reason, without calling the API', () => {
    open();
    cmp()['applying'].set(true);
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-leave-apply-modal').componentInstance as Record<string, any>;
    modal['form'].patchValue({ leaveTypeId: '1', fromDate: '2026-11-05', reason: '   ' });
    modal['form'].controls.toDate.setValue('2026-11-01');
    modal['submit']();
    expect(modal['error']('toDate')).toContain('can not be before');
    expect(modal['error']('reason')).toBe('This field is required');
    http.expectNone(`${API}/api/EmployeeLeave`);
  });

  it('an account that is not an employee gets a clear message and cannot apply', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyLeaveComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/EmployeeLeave/my`).flush({ status: false, statusCode: 404, message: 'no employee', data: null }, { status: 404, statusText: 'Not Found' });
    http.expectOne((r) => r.url === `${API}/api/LeaveType`).flush(env(TYPES));
    fixture.detectChanges();
    expect(el().textContent).toContain('linked to an employee record');
    expect([...el().querySelectorAll('button')].find((b) => b.textContent?.includes('Apply for Leave'))?.disabled).toBe(true);
  });
});
