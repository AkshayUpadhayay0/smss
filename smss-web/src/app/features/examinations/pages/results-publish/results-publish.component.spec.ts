import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MarksContextRow } from '../../models/marks.model';
import { Publication, ResultPreview, ResultStudent } from '../../models/results.model';
import { ResultsPublishComponent } from './results-publish.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const row = (over: Partial<MarksContextRow> = {}): MarksContextRow => ({
  schemeId: 1, schemeName: 'Class 3 scheme', evaluationMode: 'MARKS', termId: 5, termName: 'Term I', examId: 9, examName: 'PT 1', examSubjectId: 11, subjectId: 2, subjectName: 'English',
  classId: 3, className: 'Class 3', sectionId: 43, sectionName: 'A', entryMode: 'MARKS', maxMarks: 10, skillCount: 0, status: 'ACCEPTED', locked: true, canEnter: true, canReview: true, ...over,
});
const student = (id: string, name: string, over: Partial<ResultStudent> = {}): ResultStudent => ({ studentId: id, name, rollNumber: '1', admissionNumber: 'A' + id, result: 'PASS', obtained: 70, outOf: 100, percent: 70, grade: 'B', rank: 1, failedSubjects: [], ...over });
const pub = (over: Partial<Publication> = {}): Publication => ({ publicationId: 7, schemeId: 1, schemeName: 'Class 3 scheme', sectionId: 43, className: 'Class 3', sectionName: 'A', scope: 'TERM', termId: 5, termName: 'Term I', version: 1, status: 'PUBLISHED', publishedAt: '2026-11-06T10:00:00Z', studentCount: 2, excludedCount: 0, ...over });
const PV = (over: Partial<ResultPreview> = {}): ResultPreview => ({
  schemeId: 1, schemeName: 'Class 3 scheme', sectionId: 43, className: 'Class 3', sectionName: 'A', scope: 'TERM', termId: 5, termName: 'Term I', mode: 'MARKS', templateKey: 'term-spread', templateAvailable: true, templateMessage: null,
  showRank: true, unlockedTerms: [], canPublish: true, canDownloadDraft: true, attendanceSource: 'MANUAL', unmarkedDates: [], incompleteCount: 0, current: null,
  students: [student('S1', 'Asha Rawat'), student('S2', 'Bhanu Negi', { result: 'FAIL', percent: 30, grade: 'E', rank: null, failedSubjects: ['English'] })], ...over,
});

describe('ResultsPublishComponent', () => {
  let fixture: ComponentFixture<ResultsPublishComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;

  function open(role: string, pv: ResultPreview = PV(), versions: Publication[] = []) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: [role] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ResultsPublishComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/ReportMarks/context`).flush(env({ academicYearId: 40, academicYearName: '2026-27', rows: [row(), row({ termId: 6, termName: 'Term II', examSubjectId: 12 })] }));
    fixture.detectChanges();
    const p = http.expectOne((x) => x.url === `${API}/api/ReportResult/preview`);
    expect(p.request.params.get('scope')).toBe('TERM');
    expect(p.request.params.get('termId')).toBe('5');
    p.flush(env(pv));
    http.expectOne((x) => x.url === `${API}/api/ReportResult/publications`).flush(env(versions));
    fixture.detectChanges();
  }
  afterEach(() => { http.verify(); clearSessions(); vi.restoreAllMocks(); });

  it('shows the live result table: total, percentage, grade, result pill and rank', () => {
    open('School Admin');
    expect(text()).toContain('Asha Rawat');
    expect(text()).toContain('70 / 100');
    expect(text()).toContain('70.00%');
    expect(text()).toContain('Passed');
    expect(text()).toContain('Failed');
    expect(text()).toContain('English');            // the failed subject is named
    expect([...el().querySelectorAll('thead th')].map((h) => h.textContent?.trim())).toContain('Rank');
  });

  it('the scope picker offers each term and the whole year, and changing it asks for a new preview', () => {
    open('School Admin');
    const opts = [...el().querySelectorAll<HTMLOptionElement>('select[aria-label="Results for"] option')].map((o) => o.textContent?.trim());
    expect(opts).toEqual(['Term I', 'Term II', 'Whole year']);
    const sel = el().querySelector<HTMLSelectElement>('select[aria-label="Results for"]')!;
    sel.value = 'YEAR'; sel.dispatchEvent(new Event('change'));
    const r = http.expectOne((x) => x.url === `${API}/api/ReportResult/preview`);
    expect(r.request.params.get('scope')).toBe('YEAR');
    expect(r.request.params.has('termId')).toBe(false);
    r.flush(env(PV({ scope: 'YEAR', termId: null, termName: null })));
    http.expectOne((x) => x.url === `${API}/api/ReportResult/publications`).flush(env([]));
  });

  it('warnings: an unlocked term, an unavailable design, incomplete students and unmarked attendance days are all shown, and Publish is disabled when it cannot work', () => {
    open('School Admin', PV({ templateAvailable: false, templateMessage: 'This design is not available yet, choose Term Spread.', unlockedTerms: ['Term II'], canPublish: false, unmarkedDates: ['2026-05-04', '2026-05-05'],
      students: [student('S1', 'Asha Rawat'), student('S3', 'Late Joiner', { result: 'INCOMPLETE', obtained: null, outOf: null, percent: null, grade: null, rank: null })] }));
    expect(text()).toContain('This design is not available yet, choose Term Spread.');
    expect(text()).toContain('Lock the term first.');
    expect(text()).toContain('Term II');
    expect(text()).toContain('1 student left out');
    expect(text()).toContain('Late Joiner');
    expect(text()).toContain('Attendance was not marked on 2 school days');
    expect(button('Publish')).toBeUndefined();      // no Publish button when the server says the set cannot be published
  });

  it('the Principal reads: no Publish, no draft downloads, but the versions list is there', () => {
    open('Principal', PV({ canPublish: false, canDownloadDraft: false }), [pub()]);
    expect(button('Publish')).toBeUndefined();
    expect(button('Draft PDF')).toBeUndefined();
    expect(text()).toContain('Only a School Admin can publish.');
    expect(text()).toContain('v1');
    expect(text()).toContain('Current');
  });

  it('Publish confirms first (naming whoever is left out), then sends the request with the confirmation flags and refreshes', async () => {
    open('School Admin', PV({ students: [student('S1', 'Asha Rawat'), student('S3', 'Late Joiner', { result: 'INCOMPLETE', obtained: null, outOf: null, percent: null, grade: null, rank: null })] }));
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    button('Publish')!.click();
    await fixture.whenStable();
    expect(confirm.mock.calls[0][0].message).toContain('Late Joiner');
    const req = http.expectOne(`${API}/api/ReportResult/publish`);
    expect(req.request.body).toEqual({ schemeId: 1, sectionId: 43, scope: 'TERM', termId: 5, note: null, confirmExcluded: true, acknowledgeAttendance: false });
    req.flush(env({ published: true, needsConfirmation: false, excluded: [], unmarkedDates: [], publication: pub() }));
    http.expectOne((x) => x.url === `${API}/api/ReportResult/preview`).flush(env(PV()));
    http.expectOne((x) => x.url === `${API}/api/ReportResult/publications`).flush(env([pub()]));
  });

  it('declining the confirmation publishes nothing', async () => {
    open('School Admin');
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    button('Publish')!.click();
    await fixture.whenStable();
    http.expectNone(`${API}/api/ReportResult/publish`);
  });

  it('a server 409 that asks for confirmation (the data changed since the preview) asks again and resends with the flag', async () => {
    open('School Admin');
    const confirm = vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    button('Publish')!.click();
    await fixture.whenStable();
    const first = http.expectOne(`${API}/api/ReportResult/publish`);
    expect(first.request.body.confirmExcluded).toBe(false);
    first.flush({ status: false, statusCode: 409, message: '1 student will be left out: Late Joiner. Publish anyway?', data: { needsConfirmation: true, reason: 'EXCLUDED', excluded: [{ studentId: 'S3', name: 'Late Joiner', rollNumber: '' }], unmarkedDates: [] } }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    expect(confirm).toHaveBeenCalledTimes(2);
    expect(confirm.mock.calls[1][0].message).toContain('Late Joiner');
    const second = http.expectOne(`${API}/api/ReportResult/publish`);
    expect(second.request.body.confirmExcluded).toBe(true);
    second.flush(env({ published: true, needsConfirmation: false, excluded: [], unmarkedDates: [], publication: pub({ version: 2 }) }));
    http.expectOne((x) => x.url === `${API}/api/ReportResult/preview`).flush(env(PV()));
    http.expectOne((x) => x.url === `${API}/api/ReportResult/publications`).flush(env([pub({ version: 2 })]));
  });

  it('a version expands to its cards, each with a PDF download button', () => {
    open('School Admin', PV(), [pub({ version: 2 }), pub({ publicationId: 6, version: 1, status: 'SUPERSEDED' })]);
    expect(text()).toContain('Superseded');
    button('Cards')!.click(); fixture.detectChanges();
    const d = http.expectOne(`${API}/api/ReportResult/publications/7`);
    d.flush(env({ publication: pub({ version: 2 }), excluded: [{ studentId: 'S9', name: 'Left Out', rollNumber: '' }], cards: [{ studentId: 'S1', name: 'Asha Rawat', admissionNumber: 'AS1', rollNumber: '1', result: 'PASS', percent: 70 }] }));
    fixture.detectChanges();
    expect(text()).toContain('Left out of this version: Left Out.');
    expect(el().querySelector('[aria-label="Download card of Asha Rawat"]')).toBeTruthy();
  });

  it('"Download all (PDF)" posts to the bulk endpoint and saves the one file', () => {
    const created = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x'); vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    open('School Admin', PV(), [pub({ version: 2 })]);
    button('Download all')!.click(); fixture.detectChanges();
    const r = http.expectOne(`${API}/api/ReportCard/7/bulk`);
    expect(r.request.method).toBe('POST');
    expect(r.request.body).toEqual({});
    r.flush(new Blob(['%PDF'], { type: 'application/pdf' }));
    fixture.detectChanges();
    expect(created).toHaveBeenCalled();
    expect(click).toHaveBeenCalled();
    expect(button('Download all')!.disabled).toBe(false);
  });

  it('a version over the cap explains it and offers the roll ranges; choosing one downloads just that part', () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x'); vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    open('School Admin', PV(), [pub({ version: 2, studentCount: 150 })]);
    button('Download all')!.click(); fixture.detectChanges();
    const refused = { status: false, statusCode: 409, message: 'This version has 150 cards and one file can hold at most 100. Download it in parts.', data: { total: 150, maxPerRequest: 100, parts: [{ fromRoll: '1', toRoll: '100', count: 100 }, { fromRoll: '101', toRoll: '150', count: 50 }] } };
    http.expectOne(`${API}/api/ReportCard/7/bulk`).flush(new Blob([JSON.stringify(refused)], { type: 'application/json' }), { status: 409, statusText: 'Conflict' });
    return Promise.resolve().then(() => new Promise((r) => setTimeout(r))).then(() => {
      fixture.detectChanges();
      expect(text()).toContain('too big for one file');
      expect(text()).toContain('at most 100');
      button('Roll 101 to 150')!.click(); fixture.detectChanges();
      const part = http.expectOne(`${API}/api/ReportCard/7/bulk`);
      expect(part.request.body).toEqual({ fromRoll: '101', toRoll: '150' });
      part.flush(new Blob(['%PDF'], { type: 'application/pdf' }));
    });
  });

  it('any other bulk failure is one calm toast and the button works again', () => {
    open('School Admin', PV(), [pub({ version: 2 })]);
    button('Download all')!.click(); fixture.detectChanges();
    http.expectOne(`${API}/api/ReportCard/7/bulk`).flush(new Blob([JSON.stringify({ status: false, statusCode: 503, message: 'Other report card files are being prepared right now.', data: null })], { type: 'application/json' }), { status: 503, statusText: 'Unavailable' });
    return Promise.resolve().then(() => new Promise((r) => setTimeout(r))).then(() => {
      fixture.detectChanges();
      expect(button('Download all')!.disabled).toBe(false);
    });
  });

  it('the class teacher gets Draft PDF buttons but no Publish', () => {
    open('Teacher', PV({ canPublish: false, canDownloadDraft: true }));
    expect(button('Publish')).toBeUndefined();
    expect(el().querySelector('[aria-label="Draft card for Asha Rawat"]')).toBeTruthy();
  });

  it('a teacher who is not a class teacher of any section sees the plain empty message', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['Teacher'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ResultsPublishComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/ReportMarks/context`).flush(env({ academicYearId: 40, academicYearName: '2026-27', rows: [row({ canReview: false })] }));
    fixture.detectChanges();
    expect(text()).toContain('There is no section to show yet');
  });
});
