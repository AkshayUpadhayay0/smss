import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { LeaveRejectModalComponent } from '../../components/leave-reject-modal/leave-reject-modal.component';
import { LeaveRequestsComponent } from './leave-requests.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const TYPES = [{ leaveTypeId: 1, typeName: 'Casual Leave', typeCode: 'EMP_CASUAL', appliesToEmployee: true, appliesToStudent: false }];
const req = (id: number, status: string, employeeId = 'S001EM001', employeeName = 'Anita Staff') => ({
  leaveRequestId: id, leaveTypeId: 1, leaveTypeName: 'Casual Leave', fromDate: '2026-10-26', toDate: '2026-10-28', days: 3, reason: 'Family function', status, appliedBy: 7, appliedByName: employeeName,
  appliedAt: '2026-10-20T04:00:00Z', employeeId, employeeName,
});
const ALL = [req(1, 'Pending'), req(2, 'Approved'), req(3, 'Pending', 'S001EM002', 'Bharat Staff')];
const PENDING = [ALL[0], ALL[2]];

describe('LeaveRequestsComponent', () => {
  let fixture: ComponentFixture<LeaveRequestsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const button = (label: string) => [...el().querySelectorAll('button')].filter((b) => b.textContent?.includes(label));
  const pending = (r: { url: string; params: { get(k: string): string | null } }) => r.url === `${API}/api/EmployeeLeave` && r.params.get('status') === 'Pending' && !r.params.get('employeeId');
  const unfiltered = (r: { url: string; params: { keys(): string[] } }) => r.url === `${API}/api/EmployeeLeave` && r.params.keys().length === 0;

  function open(role: 'School Admin' | 'Principal') {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: [role] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LeaveRequestsComponent);
    fixture.detectChanges();
    http.expectOne(pending).flush(env(PENDING));
    http.expectOne(unfiltered).flush(env(ALL));
    if (role === 'School Admin') http.expectOne((r) => r.url === `${API}/api/Employee`).flush(env({ items: [{ employeeId: 'S001EM001', fullName: 'Anita Staff' }, { employeeId: 'S001EM003', fullName: 'Chitra Staff' }], total: 2, page: 1, pageSize: 100 }));
    http.expectOne((r) => r.url === `${API}/api/LeaveType`).flush(env(TYPES));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('opens on the Pending requests and offers everyone who has a request in the employee filter', () => {
    open('School Admin');
    expect(cmp()['statusControl'].value).toBe('Pending');
    expect(cmp()['rows']().map((r: { leaveRequestId: number }) => r.leaveRequestId)).toEqual([1, 3]);
    expect(cmp()['employeeOptions']().map((o: { label: string }) => o.label)).toEqual(['All employees', 'Anita Staff (S001EM001)', 'Bharat Staff (S001EM002)']);
    expect(el().textContent).toContain('2 requests · 2 pending');
  });

  it('changing the status or employee filter asks the server (both filters together)', () => {
    open('School Admin');
    cmp()['statusControl'].setValue('Approved');
    http.expectOne((r) => unfilteredWith(r, 'Approved', null)).flush(env([ALL[1]]));
    cmp()['employeeControl'].setValue('S001EM001');
    http.expectOne((r) => unfilteredWith(r, 'Approved', 'S001EM001')).flush(env([ALL[1]]));
    expect(cmp()['rows']()).toHaveLength(1);
    cmp()['statusControl'].setValue('');
    http.expectOne((r) => unfilteredWith(r, null, 'S001EM001')).flush(env([ALL[0], ALL[1]]));
  });

  function unfilteredWith(r: { url: string; params: { get(k: string): string | null } }, status: string | null, employeeId: string | null) {
    return r.url === `${API}/api/EmployeeLeave` && r.params.get('status') === status && r.params.get('employeeId') === employeeId;
  }

  it('only Pending rows have Approve / Reject; a School Admin also gets Cancel', () => {
    open('School Admin');
    expect(button('Approve')).toHaveLength(2);
    expect(button('Reject')).toHaveLength(2);
    expect(button('Cancel').filter((b) => b.textContent?.trim() === 'Cancel')).toHaveLength(2);
    expect(button('Record Leave')).toHaveLength(1);
  });

  it('a Principal can approve / reject but not cancel or record leave for others', () => {
    open('Principal');
    expect(button('Approve')).toHaveLength(2);
    expect(button('Cancel').filter((b) => b.textContent?.trim() === 'Cancel')).toHaveLength(0);
    expect(button('Record Leave')).toHaveLength(0);
  });

  it('Approve asks first, then sends the decision and reloads', async () => {
    open('School Admin');
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    const done = cmp()['approve'](cmp()['rows']()[0]);
    await Promise.resolve();
    const put = http.expectOne(`${API}/api/EmployeeLeave/1/decide`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ decision: 'Approve' });
    put.flush(env({ ...ALL[0], status: 'Approved' }));
    await done;
    http.expectOne(pending).flush(env([ALL[2]]));
    expect(cmp()['rows']()).toHaveLength(1);
  });

  it('Reject opens a dialog that REQUIRES a reason; with one it sends Reject + the trimmed reason', () => {
    open('School Admin');
    cmp()['rejecting'].set(cmp()['rows']()[0]);
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-leave-reject-modal').componentInstance as LeaveRejectModalComponent & Record<string, any>;
    modal['submit']();                                    // empty reason: nothing is emitted
    http.expectNone(`${API}/api/EmployeeLeave/1/decide`);
    expect(modal['error']()).toBe('A reason is required');
    modal['reason'].setValue('   ');
    modal['submit']();
    http.expectNone(`${API}/api/EmployeeLeave/1/decide`);
    modal['reason'].setValue('  Exam week  ');
    modal['submit']();
    const put = http.expectOne(`${API}/api/EmployeeLeave/1/decide`);
    expect(put.request.body).toEqual({ decision: 'Reject', decisionReason: 'Exam week' });
    put.flush(env({ ...ALL[0], status: 'Rejected' }));
    http.expectOne(pending).flush(env([ALL[2]]));
    fixture.detectChanges();
    expect(cmp()['rejecting']()).toBeNull();
  });

  it('when the server refuses (e.g. 409 overlap) the table is reloaded so it shows the truth', async () => {
    open('School Admin');
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    const done = cmp()['approve'](cmp()['rows']()[0]);
    await Promise.resolve();
    http.expectOne(`${API}/api/EmployeeLeave/1/decide`).flush({ status: false, statusCode: 409, message: 'overlaps', data: null }, { status: 409, statusText: 'Conflict' });
    await done;
    http.expectOne(pending).flush(env(PENDING));
  });

  it('Record Leave (School Admin) offers the active employees and sends the chosen employeeId', () => {
    open('School Admin');
    cmp()['recording'].set(true);
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-leave-apply-modal').componentInstance as Record<string, any>;
    expect(modal['people']().map((o: { value: string }) => o.value)).toEqual(['S001EM001', 'S001EM003']);
    modal['form'].patchValue({ personId: 'S001EM003', leaveTypeId: '1', fromDate: '2026-11-02', toDate: '2026-11-02', reason: 'Wedding' });
    modal['submit']();
    const post = http.expectOne(`${API}/api/EmployeeLeave`);
    expect(post.request.body).toEqual({ employeeId: 'S001EM003', leaveTypeId: 1, fromDate: '2026-11-02', toDate: '2026-11-02', reason: 'Wedding' });
    post.flush(env(req(9, 'Pending', 'S001EM003', 'Chitra Staff'), 201));
    http.expectOne(pending).flush(env(PENDING));
  });
});
