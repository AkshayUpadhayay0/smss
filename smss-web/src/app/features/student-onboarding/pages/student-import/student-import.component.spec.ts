import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
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
  return { summary: { total: rows.length, ok: rows.length - errors - warnings, warnings, errors, canImport: fileErrors.length === 0 && rows.length > 0 && errors === 0, maxRows: 500 }, fileErrors, academicYearName: '2026-27', rows };
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
  });
  afterEach(() => { http.verify(); clearSessions(); vi.restoreAllMocks(); });

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

  it('a file with an error opens on the Errors filter, blocks the import, and offers the error report', () => {
    check(PREVIEW([row(2, 'OK'), row(3, 'ERROR', ['Roll number 5 is already taken in Class 3 A.']), row(4, 'WARNING', ['Looks like a student already in the school'])]));
    expect(text()).toContain('Nothing can be imported until every error is fixed');
    expect(el().querySelectorAll('tbody tr').length).toBe(1);        // the Errors filter
    expect(text()).toContain('Roll number 5 is already taken');
    expect(button('Import 3 students')!.disabled).toBe(true);
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
});
