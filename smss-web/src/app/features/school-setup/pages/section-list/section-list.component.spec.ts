import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { SectionListComponent } from './section-list.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

const sec = (sectionId: number, classId: number, className: string, sectionName: string, statusName = 'Active') => ({
  sectionId, schoolId: 'S', classId, className, classSequenceOrder: classId, sectionName, maxStrength: 40, statusName, createdAt: '', updatedAt: '',
});
const SECTIONS = [sec(1, 10, 'Class 1', 'A'), sec(2, 10, 'Class 1', 'B'), sec(3, 11, 'Class 2', 'A', 'Inactive')];
const YEARS = [
  { academicYearId: 5, yearName: '2025-26', isCurrent: false, statusName: 'Active' },
  { academicYearId: 6, yearName: '2026-27', isCurrent: true, statusName: 'Active' },
];
const TEACHERS = [
  { employeeId: 'S001EM001', fullName: 'Anita Verma', designationName: 'Teacher' },
  { employeeId: 'S001EM002', fullName: 'Bharat Rao', designationName: 'Teacher' },
];
const ct = (sectionId: number, classId: number, employeeId: string | null, name: string | null, sectionName = 'A') => ({
  classId, className: 'Class 1', sectionId, sectionName, assignmentId: employeeId ? 100 + sectionId : null, employeeId, employeeName: name, hasClassTeacher: !!employeeId,
});
const LIST = (...sections: ReturnType<typeof ct>[]) => ({ academicYearId: 6, academicYearName: '2026-27', sections, unassignedCount: sections.filter((s) => !s.hasClassTeacher).length });

describe('SectionListComponent: class teachers', () => {
  let fixture: ComponentFixture<SectionListComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const row = (id: number) => cmp()['pageRows']().find((r: { sectionId: number }) => r.sectionId === id);

  function open(list = LIST(ct(1, 10, 'S001EM001', 'Anita Verma'), ct(2, 10, null, null, 'B'))) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SectionListComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    http.expectOne(`${API}/api/AcademicYear`).flush(env(YEARS));
    answer(list);
  }
  function answer(list: unknown) {
    const l = http.expectOne((r) => r.url === `${API}/api/ClassTeacher`);
    expect(l.request.params.get('academicYearId')).toBe('6');
    l.flush(env(list));
    http.expectOne(`${API}/api/ClassTeacher/eligible-teachers`).flush(env(TEACHERS));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('loads the CURRENT year\'s class teachers and shows each active section\'s teacher; an inactive section gets no dropdown', () => {
    open();
    expect(cmp()['yearControl'].value).toBe('6');
    expect(row(1).classTeacherId).toBe('S001EM001');
    expect(row(1).classTeacher).toBe('Anita Verma');
    expect(row(3).classTeacherId).toBe('');                       // inactive: never a teacher
    expect(el().querySelectorAll('app-select.teacher-select')).toHaveLength(2);   // sections 1 and 2 only
  });

  it('flags the sections without a class teacher, per row and in a banner', () => {
    open();
    const flags = [...el().querySelectorAll('.teacher-cell .flag')].map((f) => f.textContent?.trim());
    expect(flags).toEqual(['Missing']);                           // only section 2
    expect(el().textContent).toContain('1 active section has no class teacher yet for this year');
  });

  it('no banner when every active section has a class teacher', () => {
    open(LIST(ct(1, 10, 'S001EM001', 'Anita Verma'), ct(2, 10, 'S001EM002', 'Bharat Rao', 'B')));
    expect(el().textContent).not.toContain('no class teacher yet');
    expect(el().querySelector('.flag')).toBeNull();
  });

  it('the dropdown offers exactly the eligible teachers the server returned', () => {
    open();
    expect(cmp()['teacherOptions'](row(2)).map((o: { value: string }) => o.value)).toEqual(['S001EM001', 'S001EM002']);
  });

  it('a teacher who is no longer eligible still shows against their section (disabled option), so the admin can see and replace them', () => {
    open(LIST(ct(1, 10, 'S001EM009', 'Farid Khan'), ct(2, 10, null, null, 'B')));
    const options = cmp()['teacherOptions'](row(1));
    expect(options[0]).toMatchObject({ value: 'S001EM009', disabled: true });
    expect(options[0].label).toContain('no longer eligible');
  });

  it('choosing a teacher assigns for the chosen year / class / section in ONE call, then refreshes', () => {
    open();
    cmp()['onTeacherChange'](row(2), 'S001EM002');
    const req = http.expectOne(`${API}/api/ClassTeacher/assign`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ academicYearId: 6, classId: 10, sectionId: 2, employeeId: 'S001EM002' });
    req.flush(env({ assignmentId: 7, employeeName: 'Bharat Rao' }));
    answer(LIST(ct(1, 10, 'S001EM001', 'Anita Verma'), ct(2, 10, 'S001EM002', 'Bharat Rao', 'B')));
    expect(row(2).classTeacher).toBe('Bharat Rao');
    expect(el().textContent).not.toContain('no class teacher yet');
  });

  it('reassigning is the same single call (the server keeps the history)', () => {
    open();
    cmp()['onTeacherChange'](row(1), 'S001EM002');
    const req = http.expectOne(`${API}/api/ClassTeacher/assign`);
    expect(req.request.body).toMatchObject({ sectionId: 1, employeeId: 'S001EM002' });
    req.flush(env({ assignmentId: 8, employeeName: 'Bharat Rao' }));
    answer(LIST(ct(1, 10, 'S001EM002', 'Bharat Rao'), ct(2, 10, null, null, 'B')));
    expect(row(1).classTeacher).toBe('Bharat Rao');
  });

  it('choosing "Not assigned" removes the class teacher', () => {
    open();
    cmp()['onTeacherChange'](row(1), '');
    const req = http.expectOne(`${API}/api/ClassTeacher/unassign`);
    expect(req.request.body).toEqual({ academicYearId: 6, sectionId: 1 });
    req.flush(env({ assignmentId: 101 }));
    answer(LIST(ct(1, 10, null, null), ct(2, 10, null, null, 'B')));
    expect(el().textContent).toContain('2 active sections have no class teacher yet');
  });

  it('a failed save puts the dropdown back (it is re-created) and does not change the saved teacher', () => {
    open();
    const before = cmp()['dropdownVersion']();
    cmp()['onTeacherChange'](row(2), 'S001EM001');
    http.expectOne(`${API}/api/ClassTeacher/assign`).flush({ status: false, statusCode: 400, message: 'no', data: null }, { status: 400, statusText: 'Bad Request' });
    expect(cmp()['dropdownVersion']()).toBeGreaterThan(before);
    expect(row(2).classTeacherId).toBe('');
  });

  it('picking the teacher who is already assigned sends nothing', () => {
    open();
    cmp()['onTeacherChange'](row(1), 'S001EM001');
    http.expectNone(`${API}/api/ClassTeacher/assign`);
  });

  it('changing the academic year reloads that year\'s class teachers', () => {
    open();
    cmp()['yearControl'].setValue('5');
    const l = http.expectOne((r) => r.url === `${API}/api/ClassTeacher`);
    expect(l.request.params.get('academicYearId')).toBe('5');
    l.flush(env({ ...LIST(ct(1, 10, null, null), ct(2, 10, null, null, 'B')), academicYearId: 5 }));
    http.expectOne(`${API}/api/ClassTeacher/eligible-teachers`).flush(env(TEACHERS));
    fixture.detectChanges();
    expect(row(1).classTeacherId).toBe('');
    expect(el().textContent).toContain('2 active sections have no class teacher yet');
  });

  it('if the class teachers cannot be loaded the sections still list, with a retry', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SectionListComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    http.expectOne(`${API}/api/AcademicYear`).flush(env(YEARS));
    http.expectOne((r) => r.url === `${API}/api/ClassTeacher`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    // (the teachers request travels with the list: when the list fails it is abandoned, not answered)
    http.match(`${API}/api/ClassTeacher/eligible-teachers`);
    fixture.detectChanges();
    expect(cmp()['pageRows']().length).toBe(3);
    expect(el().textContent).toContain('Class teachers could not be loaded');
  });
});
