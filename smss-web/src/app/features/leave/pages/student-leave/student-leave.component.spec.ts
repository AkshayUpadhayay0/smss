import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { StudentLeaveComponent } from './student-leave.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const TYPES = [{ leaveTypeId: 6, typeName: 'Sick Leave', typeCode: 'STU_SICK', appliesToEmployee: false, appliesToStudent: true }];
const MY = (...ids: number[]) => ({
  today: '2026-10-22', academicYearId: 9, academicYearName: '2026-27',
  sections: ids.map((id) => ({ classId: 21, className: 'Class 1', sectionId: id, sectionName: id === 22 ? 'A' : 'B', rosterCount: 2, markedToday: false })),
});
const req = (id: number, status: string, studentId = 'S001ST001', studentName = 'Kiran Kid') => ({
  leaveRequestId: id, leaveTypeId: 6, leaveTypeName: 'Sick Leave', fromDate: '2026-10-23', toDate: '2026-10-24', days: 2, reason: 'Fever', status, appliedBy: 7, appliedByName: 'Anita Staff',
  appliedAt: '2026-10-22T04:00:00Z', studentId, studentName, sectionId: 22, className: 'Class 1', sectionName: 'A',
});
const ROSTER = { sectionId: 22, students: [{ studentId: 'S001ST001', fullName: 'Kiran Kid', onRoster: true }, { studentId: 'S001ST002', fullName: 'Karan Kid', onRoster: true }, { studentId: 'S001ST009', fullName: 'Left Kid', onRoster: false }] };

describe('StudentLeaveComponent', () => {
  let fixture: ComponentFixture<StudentLeaveComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const queue = (section: number, status: string | null) => (r: { url: string; params: { get(k: string): string | null } }) =>
    r.url === `${API}/api/StudentLeave` && r.params.get('sectionId') === String(section) && r.params.get('status') === status;

  function open(role: 'Teacher' | 'School Admin' = 'Teacher', my = MY(22), list: unknown[] = [req(1, 'Pending'), req(2, 'Pending', 'S001ST002', 'Karan Kid')]) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: [role] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentLeaveComponent);
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/api/LeaveType` && r.params.get('appliesTo') === 'student').flush(env(TYPES));
    http.expectOne(`${API}/api/Attendance/my-sections`).flush(env(my));
    if (my.sections.length > 0) http.expectOne(queue(my.sections[0].sectionId, 'Pending')).flush(env(list));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('opens on the class teacher\'s first section with its Pending requests (class and student shown)', () => {
    open();
    expect(cmp()['sectionControl'].value).toBe('22');
    expect(cmp()['rows']().map((r: { person: string; group: string }) => [r.person, r.group])).toEqual([['Kiran Kid', 'Class 1 – A'], ['Karan Kid', 'Class 1 – A']]);
    expect(el().textContent).toContain('2 requests · 2 pending');
  });

  it('a teacher who is no section\'s class teacher sees an explanation instead of a queue', () => {
    open('Teacher', MY());
    expect(el().textContent).toContain('not the class teacher of any section');
    expect(el().querySelector('app-table')).toBeNull();
  });

  it('switching section or status asks the server for that queue', () => {
    open('School Admin', MY(22, 23));
    cmp()['sectionControl'].setValue('23');
    http.expectOne(queue(23, 'Pending')).flush(env([]));
    cmp()['statusControl'].setValue('Approved');
    http.expectOne(queue(23, 'Approved')).flush(env([]));
    fixture.detectChanges();
    expect(el().textContent).toContain('No leave requests match');
  });

  it('Record Leave lists the section\'s ACTIVE roster only, and posts the chosen student', () => {
    open();
    cmp()['openRecord']();
    http.expectOne(`${API}/api/Attendance/section/22`).flush(env(ROSTER));
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-leave-apply-modal').componentInstance as Record<string, any>;
    expect(modal['people']().map((o: { value: string }) => o.value)).toEqual(['S001ST001', 'S001ST002']);
    modal['form'].patchValue({ personId: 'S001ST002', leaveTypeId: '6', fromDate: '2026-11-03', toDate: '2026-11-04', reason: 'Wedding' });
    modal['submit']();
    const post = http.expectOne(`${API}/api/StudentLeave`);
    expect(post.request.body).toEqual({ studentId: 'S001ST002', leaveTypeId: 6, fromDate: '2026-11-03', toDate: '2026-11-04', reason: 'Wedding' });
    post.flush(env(req(9, 'Pending', 'S001ST002', 'Karan Kid'), 201));
    http.expectOne(queue(22, 'Pending')).flush(env([]));
  });

  it('Approve confirms, then sends Approve for the student request and reloads the queue', async () => {
    open();
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    const done = cmp()['approve'](cmp()['rows']()[0]);
    await Promise.resolve();
    const put = http.expectOne(`${API}/api/StudentLeave/1/decide`);
    expect(put.request.body).toEqual({ decision: 'Approve' });
    put.flush(env(req(1, 'Approved')));
    await done;
    http.expectOne(queue(22, 'Pending')).flush(env([req(2, 'Pending', 'S001ST002', 'Karan Kid')]));
    expect(cmp()['rows']()).toHaveLength(1);
  });

  it('Reject needs a reason and sends it; Cancel confirms then posts to cancel', async () => {
    open();
    cmp()['rejecting'].set(cmp()['rows']()[0]);
    fixture.detectChanges();
    const modal = fixture.debugElement.query((d) => d.name === 'app-leave-reject-modal').componentInstance as Record<string, any>;
    modal['submit']();
    http.expectNone(`${API}/api/StudentLeave/1/decide`);
    modal['reason'].setValue('No documents');
    modal['submit']();
    const put = http.expectOne(`${API}/api/StudentLeave/1/decide`);
    expect(put.request.body).toEqual({ decision: 'Reject', decisionReason: 'No documents' });
    put.flush(env(req(1, 'Rejected')));
    http.expectOne(queue(22, 'Pending')).flush(env([]));

    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    const done = cmp()['cancel']({ leaveRequestId: 2, person: 'Karan Kid', leaveTypeName: 'Sick Leave', dates: 'x' });
    await Promise.resolve();
    http.expectOne(`${API}/api/StudentLeave/2/cancel`).flush(env(req(2, 'Cancelled')));
    await done;
    http.expectOne(queue(22, 'Pending')).flush(env([]));
  });

  it('only Pending rows have action buttons', () => {
    open('Teacher', MY(22), [req(1, 'Pending'), req(2, 'Approved')]);
    expect([...el().querySelectorAll('button')].filter((b) => b.textContent?.includes('Approve'))).toHaveLength(1);
  });
});
