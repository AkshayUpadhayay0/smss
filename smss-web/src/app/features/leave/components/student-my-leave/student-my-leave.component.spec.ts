import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { LeaveApplyModalComponent } from '../leave-apply-modal/leave-apply-modal.component';
import { StudentMyLeaveComponent } from './student-my-leave.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const TYPES = [
  { leaveTypeId: 3, typeName: 'Sick Leave', typeCode: 'STU_SICK', appliesToEmployee: false, appliesToStudent: true },
  { leaveTypeId: 4, typeName: 'Personal Leave', typeCode: 'STU_PERSONAL', appliesToEmployee: false, appliesToStudent: true },
];
const req = (id: number, status: string, over = {}) => ({
  leaveRequestId: id, leaveTypeId: 3, leaveTypeName: 'Sick Leave', fromDate: '2026-11-10', toDate: '2026-11-11', days: 2, reason: 'Fever', status, appliedBy: 9, appliedByName: 'Aanya Thapa',
  appliedAt: '2026-11-04T04:00:00Z', decidedBy: null, decidedByName: null, decidedAt: null, decisionReason: null, studentId: 'LAS001ST001', studentName: 'Aanya Thapa', sectionId: 38, className: 'Nursery', sectionName: 'A', ...over,
});

describe('StudentMyLeaveComponent', () => {
  let fixture: ComponentFixture<StudentMyLeaveComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(list: unknown = [req(1, 'Approved', { decidedByName: 'Sunita Rawat' }), req(3, 'Pending'), req(2, 'Rejected', { decidedByName: 'Sunita Rawat', decisionReason: 'Exam week' })]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentMyLeaveComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/StudentLeave/my`).flush(env(list));
    http.expectOne((r) => r.url === `${API}/api/LeaveType` && r.params.get('appliesTo') === 'student').flush(env(TYPES));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('lists every status, newest first, with the decision text', () => {
    open();
    const rows = cmp()['rows']();
    expect(rows.map((r: { leaveRequestId: number }) => r.leaveRequestId)).toEqual([3, 2, 1]);
    expect(rows.map((r: { status: string }) => r.status)).toEqual(['Pending', 'Rejected', 'Approved']);
    expect(rows[1].decision).toBe('Rejected by Sunita Rawat: Exam week');
    expect(el().textContent).toContain('1 request is waiting for your class teacher');
  });

  it('only a Pending request has a Cancel button', () => {
    open();
    expect([...el().querySelectorAll('button')].filter((b) => b.textContent?.includes('Cancel request'))).toHaveLength(1);
  });

  it('empty state when there are no requests', () => {
    open([]);
    expect(el().textContent).toContain('You have not asked for any leave yet.');
  });

  it('a failed load shows one calm message and disables New Leave Request', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentMyLeaveComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/StudentLeave/my`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    http.expectOne((r) => r.url === `${API}/api/LeaveType`).flush(env(TYPES));
    fixture.detectChanges();
    expect(el().querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
    expect([...el().querySelectorAll('button')].find((b) => b.textContent?.includes('New Leave Request'))?.disabled).toBe(true);
  });

  it('New Leave Request opens the self-service dialog (student-applicable types only, no student picker)', () => {
    open();
    [...el().querySelectorAll('button')].find((b) => b.textContent?.includes('New Leave Request'))!.click();
    fixture.detectChanges();
    const modal = el().querySelector('app-leave-apply-modal') as HTMLElement | null;
    expect(modal).not.toBeNull();
    expect((modal as HTMLElement).textContent).toContain('New Leave Request');
    expect((modal as HTMLElement).textContent).not.toContain('Student');
  });

  it('the dialog posts to /StudentLeave/my with NO student id, then the list reloads', () => {
    TestBed.resetTestingModule();
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(LeaveApplyModalComponent);
    f.componentRef.setInput('kind', 'self-student');
    f.componentRef.setInput('types', TYPES);
    f.detectChanges();
    const m = f.componentInstance as unknown as Record<string, any>;
    m['form'].patchValue({ leaveTypeId: '3', fromDate: '2026-11-10', toDate: '2026-11-11', reason: ' Fever ' });
    m['submit']();
    const r = http.expectOne(`${API}/api/StudentLeave/my`);
    expect(r.request.method).toBe('POST');
    expect(r.request.body).toEqual({ leaveTypeId: 3, fromDate: '2026-11-10', toDate: '2026-11-11', reason: 'Fever' });
    r.flush(env(req(9, 'Pending')));
    fixture = undefined as never;
  });

  it('the dialog validates: reason required, max 500, to not before from', () => {
    TestBed.resetTestingModule();
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    const f = TestBed.createComponent(LeaveApplyModalComponent);
    f.componentRef.setInput('kind', 'self-student');
    f.componentRef.setInput('types', TYPES);
    f.detectChanges();
    const m = f.componentInstance as unknown as Record<string, any>;
    m['form'].patchValue({ leaveTypeId: '3', fromDate: '2026-11-10', toDate: '2026-11-09', reason: '' });
    m['submit']();
    expect(m['error']('reason')).toBe('This field is required');
    expect(m['error']('toDate')).toContain('can not be before');
    m['form'].patchValue({ toDate: '2026-11-11', reason: 'x'.repeat(501) });
    m['submit']();
    expect(m['error']('reason')).toBe('Maximum 500 characters');
    http.expectNone(`${API}/api/StudentLeave/my`);
    fixture = undefined as never;
  });

  it('cancelling a Pending request posts to /my/{id}/cancel and updates the row', async () => {
    open();
    const confirm = TestBed.inject(ConfirmDialogService);
    vi.spyOn(confirm, 'confirm').mockResolvedValue(true);
    const p = cmp()['cancel'](cmp()['rows']().find((r: { status: string }) => r.status === 'Pending'));
    await Promise.resolve();
    const r = http.expectOne(`${API}/api/StudentLeave/my/3/cancel`);
    expect(r.request.method).toBe('POST');
    r.flush(env(req(3, 'Cancelled')));
    await p;
    fixture.detectChanges();
    expect(cmp()['requests']().find((x: { leaveRequestId: number }) => x.leaveRequestId === 3).status).toBe('Cancelled');
    expect([...el().querySelectorAll('button')].filter((b) => b.textContent?.includes('Cancel request'))).toHaveLength(0);
  });
});
