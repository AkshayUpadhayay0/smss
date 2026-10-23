import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MarkAttendanceComponent } from './mark-attendance.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

const STATUSES = [
  { attendanceStatusId: 1, statusCode: 'PRESENT', statusName: 'Present' },
  { attendanceStatusId: 2, statusCode: 'ABSENT', statusName: 'Absent' },
];
const MY = (...ids: number[]) => ({
  today: '2026-10-22', academicYearId: 9, academicYearName: '2026-27',
  sections: ids.map((id) => ({ classId: 21, className: 'Class 1', sectionId: id, sectionName: id === 22 ? 'A' : 'B', rosterCount: 3, markedToday: false })),
});
const stu = (id: string, name: string, roll: string, statusId: number | null = null, extra = {}) => ({
  studentId: id, fullName: name, rollNumber: roll, attendanceStatusId: statusId, statusCode: statusId === 1 ? 'PRESENT' : statusId === 2 ? 'ABSENT' : null,
  statusName: statusId === 1 ? 'Present' : statusId === 2 ? 'Absent' : null, isCorrected: false, onRoster: true, ...extra,
});
const SECTION = (students: ReturnType<typeof stu>[], over = {}) => ({
  sectionId: 22, classId: 21, className: 'Class 1', sectionName: 'A', academicYearId: 9, academicYearName: '2026-27', date: '2026-10-22', isToday: true,
  rosterCount: students.length, markedCount: students.filter((s) => s.attendanceStatusId != null).length, isMarked: students.some((s) => s.attendanceStatusId != null),
  canSubmit: true, statuses: STATUSES, students, ...over,
});

describe('MarkAttendanceComponent', () => {
  let fixture: ComponentFixture<MarkAttendanceComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const rows = () => [...el().querySelectorAll<HTMLButtonElement>('button.student')];
  const chips = () => rows().map((r) => r.querySelector('.chip')?.textContent?.trim());
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;

  function open(my = MY(22), section: unknown = SECTION([stu('S1', 'Asha', '1'), stu('S2', 'Bela', '2'), stu('S3', 'Chitra', '3')])) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['Teacher'], userType: 'Employee' }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MarkAttendanceComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Attendance/my-sections`).flush(env(my));
    if (my.sections.length) http.expectOne(`${API}/api/Attendance/section/${my.sections[0].sectionId}`).flush(env(section));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('a teacher with one section lands straight in it, today\'s date shown, EVERYONE defaulting to Present', () => {
    open();
    expect(cmp()['sectionControl'].value).toBe('22');
    expect(el().textContent).toContain('Today, 22 Oct 2026');
    expect(rows().map((r) => r.querySelector('.name')?.textContent?.trim())).toEqual(['Asha', 'Bela', 'Chitra']);
    expect(chips()).toEqual(['Present', 'Present', 'Present']);
    expect(el().textContent).toContain('3 Present');
    expect(button('Submit attendance')?.disabled).toBe(false);
  });

  it('one tap flips a student to Absent and another tap back; the counts follow', () => {
    open();
    rows()[1].click();
    fixture.detectChanges();
    expect(chips()).toEqual(['Present', 'Absent', 'Present']);
    expect(el().textContent).toContain('2 Present');
    expect(el().textContent).toContain('1 Absent');
    rows()[1].click();
    fixture.detectChanges();
    expect(chips()).toEqual(['Present', 'Present', 'Present']);
  });

  it('submit sends the WHOLE section for the server\'s date in one request, then reloads the saved state', () => {
    open();
    rows()[2].click();
    fixture.detectChanges();
    button('Submit attendance')!.click();
    const req = http.expectOne(`${API}/api/Attendance/submit`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      sectionId: 22, date: '2026-10-22',
      records: [{ studentId: 'S1', attendanceStatusId: 1 }, { studentId: 'S2', attendanceStatusId: 1 }, { studentId: 'S3', attendanceStatusId: 2 }],
    });
    req.flush(env({ sectionId: 22, academicYearId: 9, date: '2026-10-22', total: 3, present: 2, absent: 1, created: 3, updated: 0 }));
    http.expectOne(`${API}/api/Attendance/section/22`).flush(env(SECTION([stu('S1', 'Asha', '1', 1), stu('S2', 'Bela', '2', 1), stu('S3', 'Chitra', '3', 2)])));
    fixture.detectChanges();
    expect(el().textContent).toContain('Already marked today');
    expect(button('Update attendance')).toBeDefined();
  });

  it('a day that was already marked shows the SAVED marks (not the Present default) and says so; changing one shows "unsaved changes"', () => {
    open(MY(22), SECTION([stu('S1', 'Asha', '1', 1), stu('S2', 'Bela', '2', 2), stu('S3', 'Chitra', '3', 1)]));
    expect(chips()).toEqual(['Present', 'Absent', 'Present']);
    expect(el().textContent).toContain('Already marked today');
    expect(el().textContent).not.toContain('unsaved changes');
    rows()[0].click();
    fixture.detectChanges();
    expect(el().textContent).toContain('unsaved changes');
    expect(button('Update attendance')?.disabled).toBe(false);
  });

  it('"Mark all present" resets every student', () => {
    open(MY(22), SECTION([stu('S1', 'Asha', '1', 2), stu('S2', 'Bela', '2', 2)]));
    button('Mark all present')!.click();
    fixture.detectChanges();
    expect(chips()).toEqual(['Present', 'Present']);
  });

  it('the server rejecting the submission keeps the teacher\'s choices on screen', () => {
    open();
    rows()[0].click();
    fixture.detectChanges();
    button('Submit attendance')!.click();
    http.expectOne(`${API}/api/Attendance/submit`).flush({ status: false, statusCode: 400, message: 'Attendance is missing for 1 student(s)', data: null }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(chips()[0]).toBe('Absent');
    expect(button('Submit attendance')?.disabled).toBe(false);
  });

  it('when the caller may not submit (canSubmit false) the rows cannot be tapped and there is nothing to submit', () => {
    open(MY(22), SECTION([stu('S1', 'Asha', '1', 1)], { canSubmit: false }));
    expect(rows()[0].disabled).toBe(true);
    expect(button('Submit attendance') ?? button('Update attendance'))?.toBeDefined();
    expect((button('Update attendance') ?? button('Submit attendance'))!.disabled).toBe(true);
  });

  it('a student who has left the roster (onRoster false) is not listed or submitted', () => {
    open(MY(22), SECTION([stu('S1', 'Asha', '1', 1), stu('S9', 'Gone', '', 2, { onRoster: false })]));
    expect(rows()).toHaveLength(1);
    button('Update attendance')!.click();
    expect(http.expectOne(`${API}/api/Attendance/submit`).request.body.records).toEqual([{ studentId: 'S1', attendanceStatusId: 1 }]);
  });

  it('with several sections the teacher picks one; switching loads that roster', () => {
    open(MY(22, 23));
    cmp()['sectionControl'].setValue('23');
    http.expectOne(`${API}/api/Attendance/section/23`).flush(env(SECTION([stu('S7', 'Dev', '1')], { sectionId: 23, sectionName: 'B' })));
    fixture.detectChanges();
    expect(rows().map((r) => r.querySelector('.name')?.textContent?.trim())).toEqual(['Dev']);
  });

  it('a teacher who is nobody\'s class teacher gets an explanation instead of a form', () => {
    open(MY());
    expect(el().textContent).toContain('You are not the class teacher of any section');
    expect(el().querySelector('button.student')).toBeNull();
  });

  it('a section with no active students says so', () => {
    open(MY(22), SECTION([]));
    expect(el().textContent).toContain('no active students');
  });

  it('a future status (e.g. Late) joins the tap cycle without code changes: Present -> Absent -> Late -> Present', () => {
    const three = { statuses: [...STATUSES, { attendanceStatusId: 3, statusCode: 'LATE', statusName: 'Late' }] };
    open(MY(22), SECTION([stu('S1', 'Asha', '1')], three));
    const seen: (string | undefined)[] = [chips()[0]];
    for (let i = 0; i < 3; i++) {
      rows()[0].click();
      fixture.detectChanges();
      seen.push(chips()[0]);
    }
    expect(seen).toEqual(['Present', 'Absent', 'Late', 'Present']);
  });
});
