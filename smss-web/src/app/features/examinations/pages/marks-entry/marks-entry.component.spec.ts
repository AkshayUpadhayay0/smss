import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MarksContextRow, MarksGrid } from '../../models/marks.model';
import { MarksEntryComponent } from './marks-entry.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const row = (over: Partial<MarksContextRow> = {}): MarksContextRow => ({
  schemeId: 1, schemeName: 'Class 3 scheme', evaluationMode: 'MARKS', termId: 5, termName: 'Term 1', examId: 9, examName: 'PT 1', examSubjectId: 11, subjectId: 2, subjectName: 'English',
  classId: 3, className: 'Class 3', sectionId: 43, sectionName: 'A', entryMode: 'MARKS', maxMarks: 10, skillCount: 0, status: 'NOT_STARTED', locked: false, canEnter: true, canReview: false, ...over,
});
const GRID = (over: Partial<MarksGrid> = {}): MarksGrid => ({
  examSubjectId: 11, sectionId: 43, schemeId: 1, schemeName: 'Class 3 scheme', termName: 'Term 1', examName: 'PT 1', subjectName: 'English', className: 'Class 3', sectionName: 'A', entryMode: 'MARKS', maxMarks: 10,
  skills: [], gradeOptions: [], status: 'DRAFT', version: 3, locked: false, canEdit: true, canSubmit: true, canAccept: false, canReopen: false, readOnlyReason: null,
  counts: { students: 3, cells: 3, entered: 1, absent: 0, exempt: 0, missing: 2 },
  students: [
    { studentId: 'S1', admissionNumber: 'A1', rollNumber: '1', fullName: 'Asha Rawat', onRoster: true, cells: [{ state: 'ENTERED', marks: 8 }] },
    { studentId: 'S2', admissionNumber: 'A2', rollNumber: '2', fullName: 'Ravi Negi', onRoster: true, cells: [{}] },
    { studentId: 'S3', admissionNumber: 'A3', rollNumber: '3', fullName: 'Meena Bisht', onRoster: true, cells: [{}] },
  ], ...over,
});

describe('MarksEntryComponent', () => {
  let fixture: ComponentFixture<MarksEntryComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;
  const marksInputs = () => [...el().querySelectorAll<HTMLInputElement>('input[data-cell]')];
  const type = (i: number, v: string) => { const inp = marksInputs()[i]; inp.value = v; inp.dispatchEvent(new Event('input')); fixture.detectChanges(); };
  const gridReq = (): TestRequest => http.expectOne((r) => r.url === `${API}/api/ReportMarks/grid` && r.method === 'GET');

  function open(role: string, rows: MarksContextRow[], grid: MarksGrid | null = GRID(), query: Record<string, string> = {}) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: [role] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } }] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MarksEntryComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/ReportMarks/context`).flush(env({ academicYearId: 40, academicYearName: '2026-27', rows }));
    fixture.detectChanges();
    if (grid) { gridReq().flush(env(grid)); fixture.detectChanges(); }
  }
  afterEach(() => { http.verify(); clearSessions(); vi.restoreAllMocks(); });

  it('says so when there is nothing to enter, instead of an empty screen', () => {
    open('Teacher', [], null);
    expect(text()).toContain('nothing for you to enter marks for yet');
  });

  it('loads the first thing the user can enter and shows the live "x of y entered"', () => {
    open('Teacher', [row(), row({ examSubjectId: 12, subjectName: 'Maths', canEnter: false })]);
    expect(text()).toContain('English · PT 1 · Class 3 A');
    expect(text()).toContain('1 of 3 entered');
    type(1, '6');
    expect(text()).toContain('2 of 3 entered');
    expect(text()).toContain('1 unsaved');
  });

  it('opens the grid named in the link from the class report (examSubjectId and sectionId in the query)', () => {
    open('Teacher', [row(), row({ examSubjectId: 12, subjectName: 'Maths' })], null, { examSubjectId: '12', sectionId: '43' });
    const req = gridReq();
    expect(req.request.params.get('examSubjectId')).toBe('12');
    req.flush(env(GRID({ examSubjectId: 12, subjectName: 'Maths' })));
  });

  it('Save draft sends only what changed, with the version it read; an emptied cell is cleared', () => {
    open('Teacher', [row()]);
    type(1, '6.5'); type(0, '');
    button('Save draft')!.click();
    const req = http.expectOne(`${API}/api/ReportMarks/grid`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ examSubjectId: 11, sectionId: 43, version: 3, entries: [{ studentId: 'S1', state: 'NONE' }, { studentId: 'S2', state: 'ENTERED', marks: 6.5 }] });
    req.flush(env(GRID({ version: 4 })));
    fixture.detectChanges();
    expect(text()).not.toContain('unsaved');
  });

  it('flags marks above the maximum while typing and blocks saving them', () => {
    open('Teacher', [row()]);
    type(1, '11');
    expect(marksInputs()[1].classList.contains('bad')).toBe(true);
    expect(text()).toContain('marks must be between 0 and 10');
    expect(button('Save draft')!.disabled).toBe(true);
    type(1, '7.123');
    expect(button('Save draft')!.disabled).toBe(true);
    type(1, '7.12');
    expect(button('Save draft')!.disabled).toBe(false);
  });

  it('Absent and Exempt are toggles: they disable the marks box, count as entered, and toggle off again', () => {
    open('Teacher', [row()]);
    (el().querySelector('[aria-label="Ravi Negi absent"]') as HTMLButtonElement).click(); fixture.detectChanges();
    expect(marksInputs()[1].disabled).toBe(true);
    expect(marksInputs()[1].placeholder).toBe('Absent');
    expect(text()).toContain('2 of 3 entered');
    (el().querySelector('[aria-label="Ravi Negi absent"]') as HTMLButtonElement).click(); fixture.detectChanges();
    expect(marksInputs()[1].disabled).toBe(false);
    expect(text()).toContain('1 of 3 entered');
    (el().querySelector('[aria-label="Meena Bisht exempt"]') as HTMLButtonElement).click(); fixture.detectChanges();
    button('Save draft')!.click();
    expect(http.expectOne(`${API}/api/ReportMarks/grid`).request.body.entries).toEqual([{ studentId: 'S2', state: 'NONE' }, { studentId: 'S3', state: 'EXEMPT' }]);   // toggled off again = cleared
  });

  it('Enter moves to the same column of the next row', () => {
    open('Teacher', [row()]);
    const first = marksInputs()[0]; first.focus();
    first.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();
    expect(document.activeElement).toBe(marksInputs()[1]);
  });

  it('Submit stays off until every student has marks, Absent or Exempt, and says why', () => {
    open('Teacher', [row()]);
    expect(button('Submit for review')!.disabled).toBe(true);
    expect(text()).toContain('Submit becomes available when every student has marks');
    type(1, '5'); type(2, '6');
    expect(button('Submit for review')!.disabled).toBe(false);
  });

  it('Submit asks first, saves the unsaved marks, then submits with the NEW version', async () => {
    open('Teacher', [row()]);
    type(1, '5'); type(2, '6');
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    button('Submit for review')!.click();
    await fixture.whenStable();
    const save = http.expectOne(`${API}/api/ReportMarks/grid`);
    save.flush(env(GRID({ version: 4, counts: { students: 3, cells: 3, entered: 3, absent: 0, exempt: 0, missing: 0 } })));
    const sub = http.expectOne(`${API}/api/ReportMarks/submit`);
    expect(sub.request.body).toEqual({ examSubjectId: 11, sectionId: 43, version: 4 });
    sub.flush(env(GRID({ version: 5, status: 'SUBMITTED', canEdit: false, readOnlyReason: 'Submitted and waiting to be accepted. Reopen it (with a reason) to change anything.' })));
    fixture.detectChanges();
    expect(text()).toContain('Submitted and waiting to be accepted');
    expect(marksInputs().every((i) => i.disabled)).toBe(true);
  });

  it('shows the plain reason and no edit controls when the grid is read-only, and a Principal cannot edit', () => {
    open('Principal', [row({ canEnter: false })], GRID({ canEdit: false, canSubmit: false, readOnlyReason: 'The Principal can read marks but not enter them.' }));
    expect(text()).toContain('The Principal can read marks but not enter them.');
    expect(button('Save draft')).toBeUndefined();
    expect(button('Submit for review')).toBeUndefined();
    expect(marksInputs().every((i) => i.disabled)).toBe(true);
  });

  it('a locked grid says Locked and offers nothing', () => {
    open('Teacher', [row({ locked: true, status: 'ACCEPTED' })], GRID({ status: 'ACCEPTED', locked: true, canEdit: false, canReopen: false, readOnlyReason: 'This term is locked for the section.' }));
    expect(text()).toContain('Locked');
    expect(button('Reopen…')).toBeUndefined();
  });

  it('a stale save is shown as a banner that reloads the latest grid and KEEPS what was typed', () => {
    open('Teacher', [row()]);
    type(1, '5');
    button('Save draft')!.click();
    http.expectOne(`${API}/api/ReportMarks/grid`).flush({ status: false, statusCode: 409, message: 'Someone else changed this grid after you opened it. Reload it to see their changes, then enter yours again.', data: null }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(text()).toContain('Someone else changed this grid');
    (el().querySelector('[role="alert"] button') as HTMLButtonElement).click();
    gridReq().flush(env(GRID({ version: 4 })));
    fixture.detectChanges();
    expect(marksInputs()[1].value).toBe('5');
    expect(text()).toContain('1 unsaved');
  });

  it('reopen needs a reason of at least 3 characters and sends it', () => {
    open('Teacher', [row({ canReview: true })], GRID({ status: 'ACCEPTED', canEdit: false, canReopen: true, readOnlyReason: 'Accepted.' }));
    button('Reopen…')!.click(); fixture.detectChanges();
    const reopen = () => [...el().querySelectorAll('button')].filter((b) => b.textContent?.trim() === 'Reopen').pop() as HTMLButtonElement;
    expect(reopen().disabled).toBe(true);
    const box = el().querySelector<HTMLInputElement>('[aria-label="Reopen"] input')!; box.value = 'Wrong total'; box.dispatchEvent(new Event('input')); fixture.detectChanges();
    reopen().click();
    const req = http.expectOne(`${API}/api/ReportMarks/reopen`);
    expect(req.request.body).toEqual({ examSubjectId: 11, sectionId: 43, version: 3, reason: 'Wrong total' });
    req.flush(env(GRID({ version: 4 })));
  });

  it('a grades grid offers the scale\'s grades plus Absent and Exempt per skill cell', () => {
    const g = GRID({ entryMode: 'GRADE', maxMarks: null, skills: [{ skillId: 7, name: 'Reading' }, { skillId: 8, name: 'Writing' }], gradeOptions: [{ code: 'A', label: 'Excellent' }, { code: 'B', label: 'Good' }],
      students: [{ studentId: 'S1', admissionNumber: 'A1', rollNumber: '1', fullName: 'Asha Rawat', onRoster: true, cells: [{ skillId: 7 }, { skillId: 8 }] }], counts: { students: 1, cells: 2, entered: 0, absent: 0, exempt: 0, missing: 2 } });
    open('Teacher', [row({ entryMode: 'GRADE', maxMarks: null, skillCount: 2 })], g);
    const selects = [...el().querySelectorAll<HTMLSelectElement>('select[data-cell]')];
    expect(selects.length).toBe(2);
    expect([...selects[0].options].map((o) => o.value)).toEqual(['', 'A', 'B', 'AB', 'EX']);
    selects[0].value = 'B'; selects[0].dispatchEvent(new Event('change')); fixture.detectChanges();
    selects[1].value = 'AB'; selects[1].dispatchEvent(new Event('change')); fixture.detectChanges();
    expect(text()).toContain('2 of 2 entered');
    button('Save draft')!.click();
    expect(http.expectOne(`${API}/api/ReportMarks/grid`).request.body.entries).toEqual([{ studentId: 'S1', skillId: 7, state: 'ENTERED', gradeCode: 'B' }, { studentId: 'S1', skillId: 8, state: 'ABSENT' }]);
  });

  it('Excel: the template is requested for this grid; an upload is a dry run and only fills the grid (nothing saved)', () => {
    open('Teacher', [row()]);
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x'); vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined); vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    button('Excel template')!.click();
    const t = http.expectOne((r) => r.url === `${API}/api/ReportMarks/template?examSubjectId=11&sectionId=43`);
    t.flush(new Blob(['x']));
    const input = el().querySelector<HTMLInputElement>('input[type=file]')!;
    Object.defineProperty(input, 'files', { value: [new File([new Uint8Array(10)], 'marks.xlsx')], configurable: true });
    input.dispatchEvent(new Event('change'));
    const up = http.expectOne((r) => r.url.startsWith(`${API}/api/ReportMarks/upload`));
    expect(up.request.url).toContain('apply=false');
    expect(up.request.body instanceof FormData).toBe(true);
    up.flush(env({ applied: false, rows: 2, cells: 2, issues: [], entries: [{ studentId: 'S2', state: 'ENTERED', marks: 7 }, { studentId: 'S3', state: 'ABSENT' }] }));
    fixture.detectChanges();
    expect(text()).toContain('Excel file: 2 cells from 2 rows');
    (button('Fill the grid') as HTMLButtonElement).click(); fixture.detectChanges();
    expect(marksInputs()[1].value).toBe('7');
    expect(marksInputs()[2].placeholder).toBe('Absent');
    expect(text()).toContain('2 unsaved');
  });

  it('Excel: a file with problems lists them and cannot fill the grid', () => {
    open('Teacher', [row()]);
    const input = el().querySelector<HTMLInputElement>('input[type=file]')!;
    Object.defineProperty(input, 'files', { value: [new File([new Uint8Array(10)], 'marks.xlsx')], configurable: true });
    input.dispatchEvent(new Event('change'));
    http.expectOne((r) => r.url.startsWith(`${API}/api/ReportMarks/upload`)).flush(env({ applied: false, rows: 1, cells: 0, issues: [{ row: 3, message: 'Ravi Negi: 11 is above the maximum of 10.' }], entries: [] }));
    fixture.detectChanges();
    expect(text()).toContain('Row 3: Ravi Negi: 11 is above the maximum of 10.');
    expect((button('Fill the grid') as HTMLButtonElement).disabled).toBe(true);
  });

  it('refuses a non-.xlsx upload in the browser, before anything is sent', () => {
    open('Teacher', [row()]);
    const input = el().querySelector<HTMLInputElement>('input[type=file]')!;
    Object.defineProperty(input, 'files', { value: [new File([new Uint8Array(10)], 'marks.csv')], configurable: true });
    input.dispatchEvent(new Event('change'));
    http.expectNone((r) => r.url.startsWith(`${API}/api/ReportMarks/upload`));
  });

  it('the Remark column: typing a remark marks the page dirty and Save draft sends it together with the marks', () => {
    open('Teacher', [row()], GRID({ canEditRemarks: true }));
    expect([...el().querySelectorAll('thead th')].map((h) => h.textContent?.trim())).toContain('Remark');
    type(1, '6');
    const box = el().querySelector<HTMLInputElement>('input[data-remark="0"]')!; box.value = 'Writes neatly'; box.dispatchEvent(new Event('input')); fixture.detectChanges();
    button('Save draft')!.click();
    const req = http.expectOne(`${API}/api/ReportMarks/grid`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.remarks).toEqual([{ studentId: 'S1', remark: 'Writes neatly' }]);
    req.flush(env(GRID({ canEditRemarks: true })));
  });

  it('the To improve column is shown only for GRADES schemes, and an improvement alone saves together with the (unchanged) remark', () => {
    open('Teacher', [row()], GRID({ canEditRemarks: true }));
    expect([...el().querySelectorAll('thead th')].map((h) => h.textContent?.trim())).not.toContain('To improve');
    expect(el().querySelector('input[data-improve]')).toBeNull();
  });

  it('GRADES scheme: To improve sits next to Remark; editing it sends the improvement and the current remark through the remarks endpoint', () => {
    const g = GRID({ canEditRemarks: true, showImprovement: true });
    g.students[1].remark = 'Reads well';
    open('Teacher', [row()], g);
    const heads = [...el().querySelectorAll('thead th')].map((h) => h.textContent?.trim());
    expect(heads.indexOf('To improve')).toBe(heads.indexOf('Remark') + 1);
    const box = el().querySelector<HTMLInputElement>('input[data-improve="1"]')!; box.value = 'Practise joining letters'; box.dispatchEvent(new Event('input')); fixture.detectChanges();
    button('Save draft')!.click();
    const req = http.expectOne(`${API}/api/ReportMarks/remarks`);
    expect(req.request.body.remarks).toEqual([{ studentId: 'S2', remark: 'Reads well', improvement: 'Practise joining letters' }]);
    req.flush(env(g));
  });

  it('remarks alone save through their own endpoint (no marks to send)', () => {
    open('Teacher', [row()], GRID({ canEditRemarks: true }));
    const box = el().querySelector<HTMLInputElement>('input[data-remark="1"]')!; box.value = 'Practise tables'; box.dispatchEvent(new Event('input')); fixture.detectChanges();
    button('Save draft')!.click();
    const req = http.expectOne(`${API}/api/ReportMarks/remarks`);
    expect(req.request.body).toEqual({ examSubjectId: 11, sectionId: 43, remarks: [{ studentId: 'S2', remark: 'Practise tables' }] });
    req.flush(env(GRID({ canEditRemarks: true })));
  });

  it('on an ACCEPTED grid the marks are read-only but remarks can still be saved while the term is open', () => {
    open('Teacher', [row()], GRID({ status: 'ACCEPTED', canEdit: false, canEditRemarks: true, readOnlyReason: 'Accepted.' }));
    expect(marksInputs().every((i) => i.disabled)).toBe(true);
    const box = el().querySelector<HTMLInputElement>('input[data-remark="0"]')!; expect(box.disabled).toBe(false);
    box.value = 'Good effort'; box.dispatchEvent(new Event('input')); fixture.detectChanges();
    button('Save remarks')!.click();
    http.expectOne(`${API}/api/ReportMarks/remarks`).flush(env(GRID({ status: 'ACCEPTED', canEdit: false, canEditRemarks: true })));
  });

  it('a locked term or a reader without the right sees the remarks but cannot type in them', () => {
    open('Principal', [row({ canEnter: false })], GRID({ canEdit: false, canEditRemarks: false, students: [{ studentId: 'S1', admissionNumber: 'A1', rollNumber: '1', fullName: 'Asha Rawat', onRoster: true, remark: 'Excellent', cells: [{ state: 'ENTERED', marks: 8 }] }] }));
    const box = el().querySelector<HTMLInputElement>('input[data-remark="0"]')!;
    expect(box.value).toBe('Excellent'); expect(box.disabled).toBe(true);
    expect(button('Save remarks')).toBeUndefined();
  });

});
