import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { AssessmentListComponent } from './assessment-list.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

const YEARS = [{ academicYearId: 5, yearName: '2025-26', isCurrent: false, statusName: 'Active' }, { academicYearId: 6, yearName: '2026-27', isCurrent: true, statusName: 'Active' }];
const TYPES = [{ assessmentTypeId: 1, typeName: 'Class Test', typeCode: 'CLASS_TEST' }, { assessmentTypeId: 2, typeName: 'Unit Test', typeCode: 'UNIT_TEST' }];
const entry = (sectionId: number, className: string, sectionName: string, subjectId: number, subjectName: string) =>
  ({ timetableEntryId: 1, academicYearId: 6, classId: 1, className, sectionId, sectionName, dayOfWeek: 1, periodId: 1, periodNumber: 1, subjectId, subjectName, employeeId: 'S001EM001', employeeName: 'Anita' });
const GRID = (...entries: ReturnType<typeof entry>[]) => ({
  academicYearId: 6, academicYearName: '2026-27', scope: 'teacher', days: [], entryCount: entries.length,
  rows: [{ period: { periodId: 1, periodNumber: 1, startTime: '08:00:00', endTime: '08:45:00' }, cells: entries.map((e, i) => ({ dayOfWeek: i + 1, entry: e })) }],
});
const asm = (id: number, title: string, sectionId = 2, subjectName = 'Maths', extra = {}) => ({
  assessmentId: id, academicYearId: 6, academicYearName: '2026-27', classId: 1, className: 'Class 1', sectionId, sectionName: sectionId === 2 ? 'A' : 'B', subjectId: subjectName === 'Maths' ? 7 : 8, subjectName,
  assessmentTypeId: 1, assessmentTypeName: 'Class Test', employeeId: 'S001EM001', teacherName: 'Anita Staff', title, testDate: '2026-10-20', maxMarks: 20, studentCount: 3, absentCount: 1, statusName: 'Active', ...extra,
});
const ROSTER = [{ studentId: 'S1', fullName: 'Asha Kid', rollNumber: '1' }, { studentId: 'S2', fullName: 'Bela Kid', rollNumber: '2' }, { studentId: 'S3', fullName: 'Chitra Kid', rollNumber: '3' }];
const DETAIL = (extra = {}) => ({
  ...asm(9, 'Fractions quiz'),
  marks: [
    { studentId: 'S1', fullName: 'Asha Kid', rollNumber: '1', obtainedMarks: 18, isAbsent: false, onRoster: true },
    { studentId: 'S2', fullName: 'Bela Kid', rollNumber: '2', obtainedMarks: null, isAbsent: true, onRoster: true },
    { studentId: 'S3', fullName: 'Chitra Kid', rollNumber: '3', obtainedMarks: 12.5, isAbsent: false, onRoster: true },
    { studentId: 'S9', fullName: 'Left Kid', rollNumber: '9', obtainedMarks: 4, isAbsent: false, onRoster: false },
  ], ...extra,
});

describe('AssessmentListComponent', () => {
  let fixture: ComponentFixture<AssessmentListComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const modal = () => fixture.debugElement.query((d) => d.name === 'app-assessment-form-modal')?.componentInstance as Record<string, any>;
  const withParams = (url: string, params: Record<string, string>) => (r: { url: string; params: { get(k: string): string | null; keys(): string[] } }) =>
    r.url === `${API}${url}` && Object.entries(params).every(([k, v]) => r.params.get(k) === v) && r.params.keys().length === Object.keys(params).length;

  /** Answers every outstanding request (and whatever it triggers) from the canned data, the most specific URL first; unknown ones get an empty list. */
  function answer(extra: Record<string, unknown> = {}) {
    const canned: Record<string, unknown> = { '/api/Assessment/types': TYPES, '/api/AcademicYear': YEARS, '/api/Assessment/roster': ROSTER, ...extra };
    for (let round = 0; round < 8; round++) {
      fixture.detectChanges();
      const pending = http.match(() => true);
      if (pending.length === 0) break;
      for (const req of pending) {
        const key = Object.keys(canned).filter((k) => req.request.url.includes(k)).sort((a, b) => b.length - a.length)[0];
        req.flush(env(key ? canned[key] : []));
      }
    }
  }

  function open(role: 'Teacher' | 'School Admin', canned: Record<string, unknown> = {}) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: [role] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AssessmentListComponent);
    fixture.detectChanges();
    answer(role === 'Teacher'
      ? { '/api/Timetable/my': GRID(entry(2, 'Class 1', 'A', 7, 'Maths'), entry(3, 'Class 1', 'B', 8, 'English')), '/api/Assessment/my': [asm(1, 'Fractions quiz'), asm(2, 'Essay test', 3, 'English')], ...canned }
      : { '/api/Sections': [{ sectionId: 2, classId: 1, className: 'Class 1', classSequenceOrder: 1, sectionName: 'A', statusName: 'Active' }, { sectionId: 3, classId: 1, className: 'Class 1', classSequenceOrder: 1, sectionName: 'B', statusName: 'Active' }],
          '/api/Subjects': [{ subjectId: 7, subjectName: 'Maths', statusName: 'Active' }, { subjectId: 8, subjectName: 'English', statusName: 'Active' }], '/api/Assessment': [asm(1, 'Fractions quiz')], ...canned });
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  // ------------------------------------------------------------------ teacher
  it('a Teacher: current year, THEIR tests (/my), and only the sections and subjects of their own timetable', () => {
    open('Teacher');
    expect(cmp()['yearControl'].value).toBe('6');
    expect(cmp()['rows']().map((r: { title: string }) => r.title)).toEqual(['Fractions quiz', 'Essay test']);
    expect(cmp()['rows']()[0]).toMatchObject({ group: 'Class 1 – A', date: '20 Oct 2026', maxMarks: 20, entered: '3 (1 absent)', typeName: 'Class Test' });
    expect(cmp()['sectionOptions']().map((o: { label: string }) => o.label)).toEqual(['All sections', 'Class 1 – A', 'Class 1 – B']);
    expect(cmp()['subjectOptions']().map((o: { label: string }) => o.label)).toEqual(['All subjects', 'Maths', 'English']);
  });

  it('choosing a section narrows the subjects and asks the server for that section', () => {
    open('Teacher');
    cmp()['sectionControl'].setValue('3');
    http.expectOne(withParams('/api/Assessment/my', { academicYearId: '6', sectionId: '3' })).flush(env([asm(2, 'Essay test', 3, 'English')]));
    expect(cmp()['subjectOptions']().map((o: { label: string }) => o.label)).toEqual(['All subjects', 'English']);
  });

  it('Record Test: the roster appears once section and subject are chosen; each student has a marks box and an Absent toggle that switches the box off and empties it', () => {
    open('Teacher');
    cmp()['modal'].set({ assessment: null });
    fixture.detectChanges();
    const m = modal();
    m['form'].controls.sectionId.setValue('2');
    expect(m['subjectOptions']().map((o: { label: string }) => o.label)).toEqual(['Maths']);
    m['form'].controls.subjectId.setValue('7');
    answer();
    expect(m['rows'].length).toBe(3);
    expect(fixture.nativeElement.ownerDocument.body.textContent).toContain('Asha Kid');
    const row = m['rows'].at(1);
    row.controls.marks.setValue('11');
    row.controls.absent.setValue(true);
    expect(row.controls.marks.disabled).toBe(true);
    expect(row.controls.marks.value).toBe('');
    row.controls.absent.setValue(false);
    expect(row.controls.marks.enabled).toBe(true);
  });

  it('saving posts the whole roster (a mark or an absence for every student) as the teacher - no employeeId - and reloads', () => {
    open('Teacher');
    cmp()['modal'].set({ assessment: null });
    fixture.detectChanges();
    const m = modal();
    m['form'].controls.sectionId.setValue('2');
    m['form'].controls.subjectId.setValue('7');
    answer();
    m['form'].patchValue({ assessmentTypeId: '2', title: '  Fractions quiz  ', testDate: '2026-10-21', maxMarks: '20' });
    m['rows'].at(0).controls.marks.setValue('18');
    m['rows'].at(1).controls.absent.setValue(true);
    m['rows'].at(2).controls.marks.setValue('12.5');
    m['submit']();
    const post = http.expectOne(`${API}/api/Assessment`);
    expect(post.request.body).toEqual({
      academicYearId: 6, sectionId: 2, subjectId: 7, assessmentTypeId: 2, title: 'Fractions quiz', testDate: '2026-10-21', maxMarks: 20,
      marks: [{ studentId: 'S1', isAbsent: false, obtainedMarks: 18 }, { studentId: 'S2', isAbsent: true, obtainedMarks: null }, { studentId: 'S3', isAbsent: false, obtainedMarks: 12.5 }],
    });
    post.flush(env(DETAIL(), 201));
    answer({ '/api/Assessment/my': [] });
    expect(cmp()['modal']()).toBeNull();
  });

  it('nothing is sent while a student has no mark and is not absent, or a mark is above the maximum, negative, or has three decimals', () => {
    open('Teacher');
    cmp()['modal'].set({ assessment: null });
    fixture.detectChanges();
    const m = modal();
    m['form'].controls.sectionId.setValue('2');
    m['form'].controls.subjectId.setValue('7');
    answer();
    m['form'].patchValue({ assessmentTypeId: '1', title: 'T', testDate: '2026-10-21', maxMarks: '20' });
    m['rows'].at(0).controls.marks.setValue('18');
    m['rows'].at(1).controls.absent.setValue(true);
    m['submit']();                                               // student 3 has nothing
    expect(m['rowErrors']()).toBe(1);
    for (const bad of ['21', '-1', '5.123', 'abc']) {
      m['rows'].at(2).controls.marks.setValue(bad);
      m['submit']();
      expect(m['rowErrors']()).toBe(1);
    }
    http.expectNone(`${API}/api/Assessment`);
    m['rows'].at(2).controls.marks.setValue('20');               // exactly the maximum is fine
    expect(m['rowErrors']()).toBe(0);
  });

  it('the form refuses a future test date, a zero maximum and a blank title', () => {
    open('Teacher');
    cmp()['modal'].set({ assessment: null });
    fixture.detectChanges();
    const m = modal();
    m['form'].controls.sectionId.setValue('2');
    m['form'].controls.subjectId.setValue('7');
    answer();
    m['form'].patchValue({ assessmentTypeId: '1', title: '   ', testDate: '2999-01-01', maxMarks: '0' });
    m['submit']();
    expect(m['error']('title')).toBe('This field is required');
    expect(m['error']('testDate')).toContain('future');
    expect(m['error']('maxMarks')).toContain('0.01');
    http.expectNone(`${API}/api/Assessment`);
  });

  it('a refusal from the server (403: not your subject) keeps the dialog open with the typed marks', () => {
    open('Teacher');
    cmp()['modal'].set({ assessment: null });
    fixture.detectChanges();
    const m = modal();
    m['form'].controls.sectionId.setValue('2');
    m['form'].controls.subjectId.setValue('7');
    answer();
    m['form'].patchValue({ assessmentTypeId: '1', title: 'T', testDate: '2026-10-21', maxMarks: '20' });
    m['rows'].controls.forEach((r: any) => r.controls.marks.setValue('10'));
    m['submit']();
    http.expectOne(`${API}/api/Assessment`).flush({ status: false, statusCode: 403, message: "You don't currently teach Maths", data: null }, { status: 403, statusText: 'Forbidden' });
    expect(cmp()['modal']()).not.toBeNull();
    expect(m['rows'].at(0).controls.marks.value).toBe('10');
  });

  it('Edit loads the test with its marks, starts every student on their saved result (absent stays absent), notes the student who left, and PUTs the whole roster', () => {
    open('Teacher');
    cmp()['openEdit'](cmp()['rows']()[0]);
    http.expectOne(`${API}/api/Assessment/1`).flush(env(DETAIL()));
    answer();
    const m = modal();
    expect(m['form'].getRawValue()).toMatchObject({ title: 'Fractions quiz', testDate: '2026-10-20', maxMarks: '20' });
    expect(m['rows'].getRawValue()).toEqual([{ studentId: 'S1', marks: '18', absent: false }, { studentId: 'S2', marks: '', absent: true }, { studentId: 'S3', marks: '12.5', absent: false }]);
    expect(m['leftStudents']().map((s: { fullName: string }) => s.fullName)).toEqual(['Left Kid']);
    m['form'].patchValue({ title: 'Fractions quiz (retest)', maxMarks: '25' });
    m['rows'].at(0).controls.marks.setValue('22');
    m['submit']();
    const put = http.expectOne(`${API}/api/Assessment/9`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({
      title: 'Fractions quiz (retest)', testDate: '2026-10-20', maxMarks: 25,
      marks: [{ studentId: 'S1', isAbsent: false, obtainedMarks: 22 }, { studentId: 'S2', isAbsent: true, obtainedMarks: null }, { studentId: 'S3', isAbsent: false, obtainedMarks: 12.5 }],
    });
    put.flush(env(DETAIL()));
    answer();
  });

  it('a student who joined the section after the test was recorded appears EMPTY in the edit form and blocks saving until they are marked', () => {
    open('Teacher');
    cmp()['openEdit'](cmp()['rows']()[0]);
    http.expectOne(`${API}/api/Assessment/1`).flush(env(DETAIL()));
    answer({ '/api/Assessment/roster': [...ROSTER, { studentId: 'S4', fullName: 'Dev Kid', rollNumber: '4' }] });
    const m = modal();
    expect(m['rows'].length).toBe(4);
    m['submit']();
    expect(m['rowErrors']()).toBe(1);
    http.expectNone(`${API}/api/Assessment/9`);
    m['rows'].at(3).controls.absent.setValue(true);
    expect(m['rowErrors']()).toBe(0);
  });

  it('Delete confirms first, then deletes and removes the row; declining sends nothing', async () => {
    open('Teacher');
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    await cmp()['remove'](cmp()['rows']()[0]);
    http.expectNone((r) => r.method === 'DELETE');
    const done = cmp()['remove'](cmp()['rows']()[0]);
    await Promise.resolve();
    const del = http.expectOne(`${API}/api/Assessment/1`);
    expect(del.request.method).toBe('DELETE');
    del.flush(env(asm(1, 'Fractions quiz', 2, 'Maths', { statusName: 'Inactive' })));
    await done;
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(cmp()['rows']().map((r: { assessmentId: number }) => r.assessmentId)).toEqual([2]);
  });

  // ------------------------------------------------------------------ school admin
  it('a School Admin: lists the WHOLE school (not /my) and can filter by any active section and subject', () => {
    open('School Admin');
    expect(cmp()['sectionOptions']().map((o: { label: string }) => o.label)).toEqual(['All sections', 'Class 1 – A', 'Class 1 – B']);
    expect(cmp()['subjectOptions']().map((o: { label: string }) => o.label)).toEqual(['All subjects', 'Maths', 'English']);
    cmp()['subjectControl'].setValue('8');
    http.expectOne(withParams('/api/Assessment', { academicYearId: '6', subjectId: '8' })).flush(env([]));
  });

  it('a School Admin records a test for any section: picks section, then its class\'s subjects and the owning teacher, and sends employeeId', () => {
    open('School Admin');
    cmp()['modal'].set({ assessment: null });
    fixture.detectChanges();
    answer({ '/api/Timetable/lookups': { subjects: [{ subjectId: 8, subjectName: 'English' }], teachers: [{ employeeId: 'S001EM002', fullName: 'Bharat Staff', designationName: 'Teacher' }] } });
    const m = modal();
    m['form'].controls.sectionId.setValue('3');
    answer({ '/api/Timetable/lookups': { subjects: [{ subjectId: 8, subjectName: 'English' }], teachers: [{ employeeId: 'S001EM002', fullName: 'Bharat Staff', designationName: 'Teacher' }] } });
    expect(m['subjectOptions']().map((o: { label: string }) => o.label)).toEqual(['English']);
    expect(m['teacherOptions']().map((o: { value: string }) => o.value)).toEqual(['S001EM002']);
    m['form'].controls.subjectId.setValue('8');
    answer();
    m['form'].patchValue({ assessmentTypeId: '1', title: 'Essay', testDate: '2026-10-21', maxMarks: '10' });
    m['rows'].controls.forEach((r: any) => r.controls.marks.setValue('5'));
    m['submit']();
    expect(m['error']('employeeId')).toBe('This field is required');   // the admin must name the owner
    http.expectNone(`${API}/api/Assessment`);
    m['form'].patchValue({ employeeId: 'S001EM002' });
    m['submit']();
    const post = http.expectOne(`${API}/api/Assessment`);
    expect(post.request.body).toMatchObject({ academicYearId: 6, sectionId: 3, subjectId: 8, employeeId: 'S001EM002', title: 'Essay' });
    post.flush(env(DETAIL(), 201));
    answer();
  });
});
