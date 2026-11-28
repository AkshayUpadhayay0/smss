import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { YearContextService } from '../../../../core/services/year-context.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ImportPreview, ImportRow, fileProblem, filterRows } from '../../models/student-import.model';
import { StudentImportComponent } from './student-import.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const row = (n: number, status: ImportRow['status'], messages: string[] = []): ImportRow =>
  ({ rowNumber: n, status, messages, admissionNumber: '', fullName: `Student ${n}`, gender: 'Male', dateOfBirth: '01/02/2016', admissionDate: n === 2 ? '15/06/2024' : '', className: 'Class 3', sectionName: 'A', rollNumber: String(n), guardian: 'Dad', guardianMobile: '9876543210' });
const PREVIEW = (rows: ImportRow[], fileErrors: string[] = []): ImportPreview => {
  const errors = rows.filter((r) => r.status === 'ERROR').length;
  const warnings = rows.filter((r) => r.status === 'WARNING').length;
  return { summary: { total: rows.length, ok: rows.length - errors - warnings, warnings, errors, importable: rows.length - errors, canImport: fileErrors.length === 0 && rows.length - errors > 0, maxRows: 500 }, fileErrors, academicYearName: '2026-27', rows };
};
const xlsx = (name = 'students.xlsx', size = 2000) => new File([new Uint8Array(size)], name);

describe('student import: pure helpers', () => {
  it('refuses a file that is not .xlsx, is empty, or is over 1 MB, in plain words', () => {
    expect(fileProblem({ name: 'a.xlsm', size: 10 })).toContain('Only .xlsx');
    expect(fileProblem({ name: 'a.csv', size: 10 })).toContain('Only .xlsx');
    expect(fileProblem({ name: 'a.XLSX', size: 10 })).toBeNull();
    expect(fileProblem({ name: 'a.xlsx', size: 0 })).toContain('empty');
    expect(fileProblem({ name: 'a.xlsx', size: 1024 * 1024 + 1 })).toContain('1 MB');
  });
  it('filters rows by status', () => {
    const rows = [row(2, 'OK'), row(3, 'ERROR'), row(4, 'WARNING')];
    expect(filterRows(rows, 'all').length).toBe(3);
    expect(filterRows(rows, 'errors').map((r) => r.rowNumber)).toEqual([3]);
    expect(filterRows(rows, 'warnings').map((r) => r.rowNumber)).toEqual([4]);
  });
});

describe('StudentImportComponent', () => {
  let fixture: ComponentFixture<StudentImportComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;
  const cmp = () => fixture.componentInstance as unknown as { file: { set(f: File | null): void } };

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentImportComponent);
    fixture.detectChanges();
    http.match(`${API}/api/AcademicYear`).forEach((r) => r.flush(env([])));   // the viewing-year banner reads the school's years (School Admin)
  });
  afterEach(() => { http.verify(); clearSessions(); vi.restoreAllMocks(); });

  it('says the admission type is Existing Student, set automatically and not a choice (no type field in the template or the file)', () => {
    expect(el().querySelector('[data-test="import-type"]')?.textContent).toContain('Existing Student');
    expect(el().querySelector('[data-test="import-type"]')?.textContent).toContain('cannot be changed');
    expect(el().querySelector('select, app-select')).toBeNull();
  });

  function pick(file: File) {
    const input = el().querySelector<HTMLInputElement>('input[type=file]')!;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }
  function check(p: ImportPreview) {
    pick(xlsx());
    button('Check file')!.click();
    const req = http.expectOne(`${API}/api/StudentImport/preview`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    req.flush(env(p));
    fixture.detectChanges();
  }

  it('the preview shows the admission date from the file, and says the academic year start is used when it was left blank', () => {
    check(PREVIEW([row(2, 'OK'), row(3, 'OK')]));
    const cells = [...el().querySelectorAll('td[data-label="Admitted"]')].map((c) => c.textContent?.trim());
    expect(cells).toEqual(['15/06/2024', 'Year start']);
  });

  it('starts on the upload step: instructions, a template button, and Check file disabled until a file is chosen', () => {
    expect(text()).toContain('Download the template');
    expect(text()).toContain('Up to 500 students and 1 MB');
    expect(button('Download template')).toBeTruthy();
    expect(button('Check file')!.disabled).toBe(true);
  });

  it('downloads the template from the API', () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    button('Download template')!.click();
    const req = http.expectOne(`${API}/api/StudentImport/template`);
    expect(req.request.method).toBe('GET');
    req.flush(new Blob(['x']));
  });

  it('rejects a wrong file type in the browser, with a calm message, and sends nothing', () => {
    pick(xlsx('students.xlsm'));
    expect(text()).toContain('Only .xlsx files can be imported');
    expect(button('Check file')!.disabled).toBe(true);
  });

  it('a clean file shows the summary, every row as Ready, and enables Import N students', () => {
    check(PREVIEW([row(2, 'OK'), row(3, 'OK')]));
    expect(text()).toContain('Every row is ready');
    expect(button('Import 2 students')!.disabled).toBe(false);
    expect(button('Download error report')).toBeUndefined();
  });

  it('shows the "maybe siblings" hint count and the row hint, without turning the rows into warnings', () => {
    const rows = [{ ...row(2, 'OK'), guardianHint: 'A guardian with this number already exists in the school (sibling?).' }, row(3, 'OK')];
    const preview = PREVIEW(rows);
    preview.summary.guardianHints = 1;
    check(preview);
    expect(el().querySelector('[data-banner="guardian-hints"]')!.textContent).toContain('1 row has a parent number that already exists');
    expect(el().textContent).toContain('(sibling?)');
    expect(el().querySelectorAll('[data-hint="guardian"]').length).toBe(1);
    expect(preview.summary.warnings).toBe(0);
  });

  it('a file with an error opens on the Errors filter, skips the error row but still offers the valid ones, and offers the error report', () => {
    check(PREVIEW([row(2, 'OK'), row(3, 'ERROR', ['Roll number 5 is already taken in Class 3 A.']), row(4, 'WARNING', ['Looks like a student already in the school'])]));
    expect(text()).toContain('will be skipped');
    expect(el().querySelectorAll('tbody tr').length).toBe(1);        // the Errors filter
    expect(text()).toContain('Roll number 5 is already taken');
    expect(button('Import 2 students')!.disabled).toBe(false);
    expect(button('Download error report')).toBeTruthy();
    [...el().querySelectorAll<HTMLButtonElement>('.chip')].find((c) => c.textContent?.includes('All'))!.click();
    fixture.detectChanges();
    expect(el().querySelectorAll('tbody tr').length).toBe(3);
    [...el().querySelectorAll<HTMLButtonElement>('.chip')].find((c) => c.textContent?.includes('Warnings'))!.click();
    fixture.detectChanges();
    expect(el().querySelectorAll('tbody tr').length).toBe(1);
    expect(text()).toContain('Looks like a student already in the school');
  });

  it('file-level problems replace the table and offer to choose another file', () => {
    check(PREVIEW([], ["Add an 'Existing Student' Admission Type before importing students."]));
    expect(text()).toContain('The file cannot be imported yet');
    expect(text()).toContain("Add an 'Existing Student' Admission Type");
    expect(el().querySelector('table')).toBeNull();
    expect(button('Choose another file')).toBeTruthy();
  });

  it('downloads the error report by sending the file again', () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    check(PREVIEW([row(2, 'ERROR', ['bad'])]));
    button('Download error report')!.click();
    const req = http.expectOne(`${API}/api/StudentImport/error-report`);
    expect(req.request.body instanceof FormData).toBe(true);
    req.flush(new Blob(['x']));
  });

  it('asks for confirmation, then commits the file and shows the result', async () => {
    check(PREVIEW([row(2, 'OK'), row(3, 'WARNING', ['w'])]));
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    button('Import 2 students')!.click();
    await fixture.whenStable();
    expect(confirm).toHaveBeenCalled();
    expect(confirm.mock.calls[0][0].message).toContain('2 students');
    expect(confirm.mock.calls[0][0].message).toContain('1 row has a warning');
    const req = http.expectOne(`${API}/api/StudentImport/commit`);
    expect(req.request.method).toBe('POST');
    req.flush({ status: true, statusCode: 201, message: 'ok', data: { imported: 2, academicYearName: '2026-27', warningRows: 1, firstStudentId: 'S1', lastStudentId: 'S2', byClass: [{ className: 'Class 3', sectionName: 'A', students: 2 }], notes: ['No logins were created.'] } });
    fixture.detectChanges();
    expect(text()).toContain('Import complete');
    expect(text()).toContain('2 students were added to 2026-27');
    expect(text()).toContain('No logins were created.');
  });

  it('does not commit when the confirmation is declined', async () => {
    check(PREVIEW([row(2, 'OK')]));
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    button('Import 1 student')!.click();
    await fixture.whenStable();
    http.expectNone(`${API}/api/StudentImport/commit`);
  });

  it('if the server refuses the commit, nothing is shown as imported and the preview is refreshed', async () => {
    check(PREVIEW([row(2, 'OK')]));
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    button('Import 1 student')!.click();
    await fixture.whenStable();
    http.expectOne(`${API}/api/StudentImport/commit`).flush({ status: false, statusCode: 422, message: 'The file has 1 row(s) with errors.', data: null }, { status: 422, statusText: 'Unprocessable' });
    const again = http.expectOne(`${API}/api/StudentImport/preview`);
    again.flush(env(PREVIEW([row(2, 'ERROR', ['Admission number X already belongs to a student in this school.'])])));
    fixture.detectChanges();
    expect(text()).toContain('nothing was imported');
    expect(text()).not.toContain('Import complete');
    expect(text()).toContain('already belongs to a student');
  });

  const withFees = (p: ImportPreview, extra: Partial<ImportPreview>): ImportPreview => ({ ...p, ...extra });
  const tick = (sel: string) => { (el().querySelector(sel) as HTMLInputElement).click(); fixture.detectChanges(); };

  it('names the classes that have no fee structure and says those students get no fees', () => {
    check(withFees(PREVIEW([row(2, 'OK')]), { classesWithoutFeeStructure: [{ className: 'Class 3', students: 1 }] }));
    const banner = el().querySelector('[data-banner="no-structure"]')?.textContent ?? '';
    expect(banner).toContain('Class 3');
    expect(banner).toContain('no fees');
  });

  it('offers charging from the year start only once the year has begun; the tick needs a confirmation and a reason, and nothing is back-dated without it', async () => {
    check(PREVIEW([row(2, 'OK')]));
    expect(el().querySelector('[data-from-start]')).toBeNull();          // year not started: no offer
    fixture.destroy();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentImportComponent);
    fixture.detectChanges();
    http.match(`${API}/api/AcademicYear`).forEach((r) => r.flush(env([])));
    check(withFees(PREVIEW([row(2, 'OK')]), { yearStarted: true, yearStart: '2026-04-01' }));
    expect(button('Import 1 student')!.disabled).toBe(false);             // no tick: nothing back-dated, ready
    tick('[data-from-start]');
    expect(button('Import 1 student')!.disabled).toBe(true);              // needs the confirmation and a reason
    tick('[data-confirm-backdating]');
    expect(button('Import 1 student')!.disabled).toBe(true);
    const reason = el().querySelector('[data-reason]') as HTMLTextAreaElement;
    reason.value = 'Students were already studying'; reason.dispatchEvent(new Event('input')); fixture.detectChanges();
    expect(button('Import 1 student')!.disabled).toBe(false);
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    button('Import 1 student')!.click();
    await fixture.whenStable();
    const req = http.expectOne(`${API}/api/StudentImport/commit`);
    const form = req.request.body as FormData;
    expect([form.get('chargeFromYearStart'), form.get('confirmBackdating'), form.get('reason')]).toEqual(['true', 'true', 'Students were already studying']);
    req.flush({ status: true, statusCode: 201, message: 'ok', data: { imported: 1, academicYearName: '2026-27', warningRows: 0, firstStudentId: 'S1', lastStudentId: 'S1', byClass: [], notes: [], feesAssigned: 0, feesNoStructure: 0, feesBackdated: true, feesFailed: [{ studentId: 'S1', studentName: 'A', className: 'Class 3', reason: 'The student was added, but the fee structure could not be assigned automatically.' }] } });
    fixture.detectChanges();
    expect(text()).toContain('Fees not assigned');
    expect(text()).toContain('could not be assigned automatically');
    expect(text()).toContain('1 failed');
  });

  it('without the tick the commit says so explicitly (no back-dating fields)', async () => {
    check(withFees(PREVIEW([row(2, 'OK')]), { yearStarted: true, yearStart: '2026-04-01' }));
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    button('Import 1 student')!.click();
    await fixture.whenStable();
    const req = http.expectOne(`${API}/api/StudentImport/commit`);
    const form = req.request.body as FormData;
    expect(form.get('chargeFromYearStart')).toBe('false');
    expect(form.get('reason')).toBeNull();
    req.flush({ status: true, statusCode: 201, message: 'ok', data: { imported: 1, academicYearName: '2026-27', warningRows: 0, firstStudentId: 'S1', lastStudentId: 'S1', byClass: [], notes: [], feesAssigned: 1, feesNoStructure: 0, feesBackdated: false, feesFailed: [] } });
    fixture.detectChanges();
    expect(text()).toContain('1 assigned');
  });

  it('shows the data-entry banner when the header is viewing another year: the import always goes into the current year', () => {
    const yr = (id: number, name: string, isCurrent: boolean) => ({ academicYearId: id, schoolId: 'S', yearName: name, startDate: '2026-04-01', endDate: '2027-03-31', isCurrent, statusName: 'Active', state: isCurrent ? 'CURRENT' : 'PAST', isClosed: false, usage: {}, createdAt: '', updatedAt: '' });
    TestBed.inject(YearContextService).refresh();
    http.match(`${API}/api/AcademicYear`).forEach((r) => r.flush(env([yr(1, '2025-26', false), yr(2, '2026-27', true)])));
    TestBed.inject(YearContextService).select('1');
    fixture.detectChanges();
    expect(el().querySelector('[data-viewing-year-banner]')?.textContent).toContain('goes into');
  });
});
