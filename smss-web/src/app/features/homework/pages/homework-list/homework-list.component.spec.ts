import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { HomeworkListComponent } from './homework-list.component';

const API = environment.apiUrl;
const env = <T>(data: T, statusCode = 200) => ({ status: true, statusCode, message: 'ok', data });

const YEARS = [
  { academicYearId: 5, yearName: '2025-26', isCurrent: false, statusName: 'Active' },
  { academicYearId: 6, yearName: '2026-27', isCurrent: true, statusName: 'Active' },
];
const entry = (sectionId: number, className: string, sectionName: string, subjectId: number, subjectName: string) =>
  ({ timetableEntryId: 1, academicYearId: 6, classId: 1, className, sectionId, sectionName, dayOfWeek: 1, periodId: 1, periodNumber: 1, subjectId, subjectName, employeeId: 'S001EM001', employeeName: 'Anita' });
const GRID = (...entries: ReturnType<typeof entry>[]) => ({
  academicYearId: 6, academicYearName: '2026-27', scope: 'teacher', days: [], entryCount: entries.length,
  rows: [{ period: { periodId: 1, periodNumber: 1, startTime: '08:00:00', endTime: '08:45:00' }, cells: entries.map((e, i) => ({ dayOfWeek: i + 1, entry: e })) }],
});
const hw = (id: number, title: string, sectionId = 2, subjectName = 'Maths', extra = {}) => ({
  homeworkId: id, academicYearId: 6, academicYearName: '2026-27', classId: 1, className: 'Class 1', sectionId, sectionName: sectionId === 2 ? 'A' : 'B', subjectId: subjectName === 'Maths' ? 7 : 8, subjectName,
  employeeId: 'S001EM001', teacherName: 'Anita Staff', title, description: 'desc', assignedDate: '2026-10-20', dueDate: '2026-10-27', hasAttachment: false, attachmentFileName: null, statusName: 'Active', ...extra,
});
const SECTIONS = [
  { sectionId: 2, classId: 1, className: 'Class 1', classSequenceOrder: 1, sectionName: 'A', statusName: 'Active' },
  { sectionId: 3, classId: 1, className: 'Class 1', classSequenceOrder: 1, sectionName: 'B', statusName: 'Active' },
  { sectionId: 4, classId: 2, className: 'Class 2', classSequenceOrder: 2, sectionName: 'A', statusName: 'Inactive' },
];

describe('HomeworkListComponent', () => {
  let fixture: ComponentFixture<HomeworkListComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;
  const modal = () => fixture.debugElement.query((d) => d.name === 'app-homework-form-modal')?.componentInstance as Record<string, any>;
  const withParams = (url: string, params: Record<string, string>) => (r: { url: string; params: { get(k: string): string | null; keys(): string[] } }) =>
    r.url === `${API}${url}` && Object.entries(params).every(([k, v]) => r.params.get(k) === v) && r.params.keys().length === Object.keys(params).length;

  function openTeacher(list: unknown[] = [hw(1, 'Chapter 3'), hw(2, 'Essay', 3, 'English')], grid = GRID(entry(2, 'Class 1', 'A', 7, 'Maths'), entry(3, 'Class 1', 'B', 8, 'English'))) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(HomeworkListComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/AcademicYear`).flush(env(YEARS));
    http.expectOne(withParams('/api/Timetable/my', { academicYearId: '6' })).flush(env(grid));
    http.expectOne(withParams('/api/Homework/my', { academicYearId: '6' })).flush(env(list));
    fixture.detectChanges();
  }
  function openAdmin() {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(HomeworkListComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/AcademicYear`).flush(env(YEARS));
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    http.expectOne(`${API}/api/Subjects`).flush(env([{ subjectId: 7, subjectName: 'Maths', statusName: 'Active' }, { subjectId: 8, subjectName: 'English', statusName: 'Active' }, { subjectId: 9, subjectName: 'Old', statusName: 'Inactive' }]));
    http.expectOne(withParams('/api/Homework', { academicYearId: '6' })).flush(env([hw(1, 'Chapter 3')]));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  // ------------------------------------------------------------------ teacher
  it('a Teacher: defaults to the current year, lists THEIR homework (/my) and offers only the sections and subjects of their own timetable', () => {
    openTeacher();
    expect(cmp()['yearControl'].value).toBe('6');
    expect(cmp()['rows']().map((r: { title: string }) => r.title)).toEqual(['Chapter 3', 'Essay']);
    expect(cmp()['rows']()[0].due).toBe('27 Oct 2026');
    expect(cmp()['sectionOptions']().map((o: { label: string }) => o.label)).toEqual(['All sections', 'Class 1 – A', 'Class 1 – B']);
    expect(cmp()['subjectOptions']().map((o: { label: string }) => o.label)).toEqual(['All subjects', 'Maths', 'English']);
  });

  it('choosing a section narrows the subjects and asks the server for that section', () => {
    openTeacher();
    cmp()['sectionControl'].setValue('3');
    http.expectOne(withParams('/api/Homework/my', { academicYearId: '6', sectionId: '3' })).flush(env([hw(2, 'Essay', 3, 'English')]));
    expect(cmp()['subjectOptions']().map((o: { label: string }) => o.label)).toEqual(['All subjects', 'English']);
    cmp()['subjectControl'].setValue('8');
    http.expectOne(withParams('/api/Homework/my', { academicYearId: '6', sectionId: '3', subjectId: '8' })).flush(env([]));
    fixture.detectChanges();
    expect(el().textContent).toContain('No homework matches');
  });

  it('changing the academic year reloads the teacher\'s timetable combos and the list', () => {
    openTeacher();
    cmp()['yearControl'].setValue('5');
    http.expectOne(withParams('/api/Timetable/my', { academicYearId: '5' })).flush(env(GRID()));
    http.expectOne(withParams('/api/Homework/my', { academicYearId: '5' })).flush(env([]));
    fixture.detectChanges();
    expect(el().textContent).toContain('Your timetable has no classes in this academic year');
  });

  it('Add Homework: sections / subjects come from the timetable; Publish posts as the teacher (no employeeId), then uploads the chosen file, then reloads', () => {
    openTeacher();
    cmp()['modal'].set({ homework: null });
    fixture.detectChanges();
    const m = modal();
    expect(m['sectionOptions']().map((o: { label: string }) => o.label)).toEqual(['Class 1 – A', 'Class 1 – B']);
    m['form'].patchValue({ sectionId: '2' });
    expect(m['subjectOptions']().map((o: { label: string }) => o.label)).toEqual(['Maths']);
    m['form'].patchValue({ subjectId: '7', title: '  Chapter 4  ', description: '', assignedDate: '2026-10-23', dueDate: '2026-10-30' });
    m['file'].set(new File(['%PDF-1.4'], 'sheet.pdf', { type: 'application/pdf' }));
    m['submit']();
    const post = http.expectOne(`${API}/api/Homework`);
    expect(post.request.body).toEqual({ academicYearId: 6, sectionId: 2, subjectId: 7, assignedDate: '2026-10-23', dueDate: '2026-10-30', title: 'Chapter 4', description: null });
    post.flush(env(hw(9, 'Chapter 4'), 201));
    const upload = http.expectOne(`${API}/api/Homework/9/attachment`);
    expect(upload.request.body instanceof FormData).toBe(true);
    upload.flush(env(hw(9, 'Chapter 4', 2, 'Maths', { hasAttachment: true, attachmentFileName: 'homework-9.pdf' })));
    http.expectOne(withParams('/api/Homework/my', { academicYearId: '6' })).flush(env([]));
    expect(cmp()['modal']()).toBeNull();
  });

  it('if only the attachment upload fails the homework is kept (warning, list reloads)', () => {
    openTeacher();
    cmp()['modal'].set({ homework: null });
    fixture.detectChanges();
    const m = modal();
    m['form'].patchValue({ sectionId: '2' });
    m['form'].patchValue({ subjectId: '7', title: 'Chapter 4', dueDate: '2026-10-30' });
    m['file'].set(new File(['x'], 'a.pdf'));
    m['submit']();
    http.expectOne(`${API}/api/Homework`).flush(env(hw(9, 'Chapter 4'), 201));
    http.expectOne(`${API}/api/Homework/9/attachment`).flush({ status: false, statusCode: 400, message: 'bad file', data: null }, { status: 400, statusText: 'Bad Request' });
    http.expectOne(withParams('/api/Homework/my', { academicYearId: '6' })).flush(env([hw(9, 'Chapter 4')]));
    expect(cmp()['modal']()).toBeNull();
  });

  it('a refusal from the server (403: not your subject) keeps the dialog open with its values', () => {
    openTeacher();
    cmp()['modal'].set({ homework: null });
    fixture.detectChanges();
    const m = modal();
    m['form'].patchValue({ sectionId: '2' });
    m['form'].patchValue({ subjectId: '7', title: 'Chapter 4', dueDate: '2026-10-30' });
    m['submit']();
    http.expectOne(`${API}/api/Homework`).flush({ status: false, statusCode: 403, message: "You don't currently teach Maths", data: null }, { status: 403, statusText: 'Forbidden' });
    expect(cmp()['modal']()).not.toBeNull();
    expect(m['form'].controls.title.value).toBe('Chapter 4');
  });

  it('the form refuses: a due date before the assigned date, a blank title, a missing section; a bad / oversized attachment', () => {
    openTeacher();
    cmp()['modal'].set({ homework: null });
    fixture.detectChanges();
    const m = modal();
    m['submit']();
    expect(m['error']('sectionId')).toBe('This field is required');
    expect(m['error']('title')).toBe('This field is required');
    m['form'].patchValue({ sectionId: '2' });
    m['form'].patchValue({ subjectId: '7', title: 'T', assignedDate: '2026-10-23' });
    m['form'].controls.dueDate.setValue('2026-10-20');
    m['submit']();
    expect(m['error']('dueDate')).toContain('can not be before');
    const pick = (file: File) => { Object.defineProperty(file, 'size', { value: file.size }); m['onFile']({ target: { files: [file] } } as unknown as Event); };
    pick(new File(['x'], 'run.exe')); expect(m['fileError']()).toContain('Only PDF');
    pick(new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'big.pdf')); expect(m['fileError']()).toContain('5 MB');
    pick(new File(['%PDF'], 'ok.pdf')); expect(m['fileError']()).toBe(''); expect(m['file']()?.name).toBe('ok.pdf');
    http.expectNone(`${API}/api/Homework`);
  });

  it('Edit starts on the saved values with the section / subject fixed, and PUTs only title, description and dates', () => {
    openTeacher();
    cmp()['openEdit'](cmp()['rows']()[0]);
    fixture.detectChanges();
    const m = modal();
    expect(m['form'].getRawValue()).toMatchObject({ title: 'Chapter 3', description: 'desc', assignedDate: '2026-10-20', dueDate: '2026-10-27' });
    expect(el().ownerDocument.body.textContent).toContain('Class 1 – A · Maths · 2026-27');
    m['form'].patchValue({ title: 'Chapter 3 (revised)', dueDate: '2026-10-29' });
    m['submit']();
    const put = http.expectOne(`${API}/api/Homework/1`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ title: 'Chapter 3 (revised)', description: 'desc', assignedDate: '2026-10-20', dueDate: '2026-10-29' });
    put.flush(env(hw(1, 'Chapter 3 (revised)')));
    http.expectOne(withParams('/api/Homework/my', { academicYearId: '6' })).flush(env([]));
  });

  it('Delete confirms first, then deletes and removes the row; declining sends nothing', async () => {
    openTeacher();
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    await cmp()['remove'](cmp()['rows']()[0]);
    http.expectNone(`${API}/api/Homework/1`);
    const done = cmp()['remove'](cmp()['rows']()[0]);
    await Promise.resolve();
    const del = http.expectOne(`${API}/api/Homework/1`);
    expect(del.request.method).toBe('DELETE');
    del.flush(env(hw(1, 'Chapter 3', 2, 'Maths', { statusName: 'Inactive' })));
    await done;
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(cmp()['rows']().map((r: { homeworkId: number }) => r.homeworkId)).toEqual([2]);
  });

  it('the Download button only appears where there is an attachment', () => {
    openTeacher([hw(1, 'With', 2, 'Maths', { hasAttachment: true, attachmentFileName: 'homework-1.pdf' }), hw(2, 'Without')]);
    expect([...el().querySelectorAll('button')].filter((b) => b.textContent?.trim() === 'Download')).toHaveLength(1);
  });

  // ------------------------------------------------------------------ school admin
  it('a School Admin: lists the WHOLE school (not /my) and can filter by any active section and subject', () => {
    openAdmin();
    expect(cmp()['sectionOptions']().map((o: { label: string }) => o.label)).toEqual(['All sections', 'Class 1 – A', 'Class 1 – B']);   // the inactive section is not offered
    expect(cmp()['subjectOptions']().map((o: { label: string }) => o.label)).toEqual(['All subjects', 'Maths', 'English']);
    cmp()['subjectControl'].setValue('8');
    http.expectOne(withParams('/api/Homework', { academicYearId: '6', subjectId: '8' })).flush(env([]));
  });

  it('a School Admin adds homework for any section: picks the section, then its class\'s subjects and the owning teacher, and sends employeeId', () => {
    openAdmin();
    cmp()['modal'].set({ homework: null });
    fixture.detectChanges();
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    const m = modal();
    m['form'].patchValue({ sectionId: '3' });
    http.expectOne(withParams('/api/Timetable/lookups', { sectionId: '3' })).flush(env({
      subjects: [{ subjectId: 8, subjectName: 'English' }], teachers: [{ employeeId: 'S001EM002', fullName: 'Bharat Staff', designationName: 'Teacher' }],
    }));
    expect(m['subjectOptions']().map((o: { label: string }) => o.label)).toEqual(['English']);
    expect(m['teacherOptions']().map((o: { value: string }) => o.value)).toEqual(['S001EM002']);
    m['submit']();
    expect(m['error']('employeeId')).toBe('This field is required');   // the admin must name the owner
    m['form'].patchValue({ subjectId: '8', employeeId: 'S001EM002', title: 'Essay', dueDate: '2026-10-30' });
    m['submit']();
    const post = http.expectOne(`${API}/api/Homework`);
    expect(post.request.body).toMatchObject({ academicYearId: 6, sectionId: 3, subjectId: 8, employeeId: 'S001EM002', title: 'Essay' });
    post.flush(env(hw(9, 'Essay', 3, 'English'), 201));
    http.expectOne(withParams('/api/Homework', { academicYearId: '6' })).flush(env([]));
  });
});
