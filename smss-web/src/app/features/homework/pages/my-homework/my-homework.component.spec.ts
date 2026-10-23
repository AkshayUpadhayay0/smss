import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { todayYmd } from '../../utils/homework-format';
import { MyHomeworkComponent } from './my-homework.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });
const offset = (days: number) => todayYmd(new Date(new Date().getFullYear(), new Date().getMonth(), new Date().getDate() + days));
const hw = (id: number, title: string, dueDays: number, extra = {}) => ({
  homeworkId: id, academicYearId: 6, academicYearName: '2026-27', classId: 1, className: 'Class 1', sectionId: 2, sectionName: 'A', subjectId: 7, subjectName: 'Maths', employeeId: 'S001EM001',
  teacherName: 'Anita Staff', title, description: 'Do the exercises', assignedDate: offset(-3), dueDate: offset(dueDays), hasAttachment: false, attachmentFileName: null, statusName: 'Active', ...extra,
});

describe('MyHomeworkComponent', () => {
  let fixture: ComponentFixture<MyHomeworkComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(list: unknown) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyHomeworkComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Homework/student/my`).flush(env(list));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows the section\'s homework soonest due first, each with an overdue / due today / upcoming mark computed from today', () => {
    open([hw(1, 'Later', 5), hw(2, 'Yesterday', -1), hw(3, 'Today', 0), hw(4, 'Tomorrow', 1)]);
    const titles = [...el().querySelectorAll('.card .title')].map((t) => t.textContent?.trim());
    expect(titles).toEqual(['Yesterday', 'Today', 'Tomorrow', 'Later']);
    const states = [...el().querySelectorAll('.card')].map((c) => ['overdue', 'today', 'upcoming'].find((s) => c.classList.contains(s)));
    expect(states).toEqual(['overdue', 'today', 'upcoming', 'upcoming']);
    expect([...el().querySelectorAll('.chip')].map((c) => c.textContent?.trim())).toEqual(['Overdue by 1 day', 'Due today', 'Due in 1 day', 'Due in 5 days']);
    expect(el().textContent).toContain('1 overdue · 3 upcoming');
  });

  it('is read-only: no submit, upload or edit controls anywhere', () => {
    open([hw(1, 'Read this', 2)]);
    expect(el().querySelectorAll('input, textarea, select, form')).toHaveLength(0);
    expect([...el().querySelectorAll('button')].map((b) => b.textContent?.trim().toLowerCase()).filter((t) => /submit|upload|edit|delete|turn in/.test(t ?? ''))).toEqual([]);
  });

  it('an attachment gets a download button that fetches it through the authenticated endpoint and saves it under its file name', () => {
    const createUrl = vi.fn(() => 'blob:x');
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: createUrl, revokeObjectURL: vi.fn() }));
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    open([hw(1, 'With file', 2, { hasAttachment: true, attachmentFileName: 'homework-1.pdf' }), hw(2, 'No file', 3)]);
    const buttons = [...el().querySelectorAll('button')].filter((b) => b.textContent?.includes('Download attachment'));
    expect(buttons).toHaveLength(1);
    buttons[0].click();
    const req = http.expectOne(`${API}/api/Homework/attachment/1`);
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['%PDF-1.4']));
    expect(createUrl).toHaveBeenCalled();
    expect(click).toHaveBeenCalled();
    click.mockRestore();
    vi.unstubAllGlobals();
  });

  it('says so when there is no homework, and when it cannot be loaded', () => {
    open([]);
    expect(el().textContent).toContain('No homework has been set for your class');
  });

  it('shows an error note when the request fails', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Student', roles: ['Student'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyHomeworkComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Homework/student/my`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(el().textContent).toContain('could not be loaded');
  });
});
