import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { Completeness, MarksContextRow } from '../../models/marks.model';
import { ClassReportComponent } from './class-report.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const row = (over: Partial<MarksContextRow> = {}): MarksContextRow => ({
  schemeId: 1, schemeName: 'Class 3 scheme', evaluationMode: 'MARKS', termId: 5, termName: 'Term 1', examId: 9, examName: 'PT 1', examSubjectId: 11, subjectId: 2, subjectName: 'English',
  classId: 3, className: 'Class 3', sectionId: 43, sectionName: 'A', entryMode: 'MARKS', maxMarks: 10, skillCount: 0, status: 'ACCEPTED', locked: false, canEnter: true, canReview: true, ...over,
});
const COMP = (over: Partial<Completeness> = {}, ready = true, locked = false): Completeness => ({
  schemeId: 1, schemeName: 'Class 3 scheme', evaluationMode: 'MARKS', attendanceSource: 'AUTO', sectionId: 43, classId: 3, className: 'Class 3', sectionName: 'A', rosterCount: 12, canEdit: true, canLock: true,
  terms: [{
    termId: 5, termName: 'Term 1', startDate: '2026-04-01', endDate: '2026-09-29', locked, lockedAt: locked ? '2026-11-01T00:00:00Z' : null, lastUnlockReason: null, cells: 2, accepted: ready ? 2 : 1, submitted: ready ? 0 : 1, draft: 0, notStarted: 0, readyToLock: ready,
    exams: [{ examId: 9, name: 'PT 1' }, { examId: 10, name: 'PT 2' }],
    subjects: [{ schemeSubjectId: 20, subjectName: 'English', cells: [
      { examId: 9, examSubjectId: 11, entryMode: 'MARKS', status: 'ACCEPTED', expected: 12, entered: 12, absent: 0, exempt: 0, missing: 0 },
      { examId: 10, examSubjectId: 12, entryMode: 'MARKS', status: ready ? 'ACCEPTED' : 'SUBMITTED', expected: 12, entered: 11, absent: 0, exempt: 0, missing: ready ? 0 : 1 }] }],
  }], ...over,
});

describe('ClassReportComponent', () => {
  let fixture: ComponentFixture<ClassReportComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;

  function open(role: string, rows: MarksContextRow[], comp: Completeness | null = COMP()) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: [role] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ClassReportComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/ReportMarks/context`).flush(env({ academicYearId: 40, academicYearName: '2026-27', rows }));
    fixture.detectChanges();
    if (comp) { const r = http.expectOne((x) => x.url === `${API}/api/ReportClass/completeness`); expect(r.request.params.get('sectionId')).toBe('43'); r.flush(env(comp)); fixture.detectChanges(); }
  }
  afterEach(() => { http.verify(); clearSessions(); vi.restoreAllMocks(); });

  it('a teacher who reviews no section sees a plain message, and a subject-only teacher is not offered the class report', () => {
    open('Teacher', [row({ canReview: false, canEnter: true })], null);
    expect(text()).toContain('There is no section for you to review yet');
  });

  it('shows the completeness matrix: state chips with missing counts, linking to the grid', () => {
    open('Teacher', [row()], COMP({}, false));
    expect(text()).toContain('Term 1');
    expect(text()).toContain('1 of 2 accepted');
    expect(text()).toContain('1 waiting for you');
    const chips = [...el().querySelectorAll<HTMLAnchorElement>('a.chip')];
    expect(chips.map((c) => c.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Accepted', 'Submitted · 1 missing']);
    expect(chips[1].getAttribute('href')).toContain('/examinations/marks?examSubjectId=12&sectionId=43');
  });

  it('the class teacher has no Lock button (only the School Admin locks)', () => {
    open('Teacher', [row()], COMP({ canLock: false }));
    expect(button('Lock term')).toBeUndefined();
    expect(button('Unlock')).toBeUndefined();
  });

  it('Lock term stays disabled until every exam subject of the term is accepted', async () => {
    open('School Admin', [row()], COMP({}, false));
    expect(button('Lock term')!.disabled).toBe(true);
  });

  it('Lock asks first, locks the term for the section, and refreshes', async () => {
    open('School Admin', [row()], COMP());
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    button('Lock term')!.click();
    await fixture.whenStable();
    const req = http.expectOne(`${API}/api/ReportClass/lock`);
    expect(req.request.body).toEqual({ termId: 5, sectionId: 43 });
    req.flush(env({ termId: 5, sectionId: 43, locked: true }));
    http.expectOne((x) => x.url === `${API}/api/ReportClass/completeness`).flush(env(COMP({}, true, true)));
    fixture.detectChanges();
    expect(text()).toContain('Locked');
    expect(text()).toContain('Unlocking does not make accepted marks editable');
  });

  it('declining the Lock confirmation sends nothing', async () => {
    open('School Admin', [row()], COMP());
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    button('Lock term')!.click();
    await fixture.whenStable();
    http.expectNone(`${API}/api/ReportClass/lock`);
  });

  it('Unlock needs a reason (at least 3 characters) and sends it', () => {
    open('School Admin', [row()], COMP({}, true, true));
    button('Unlock…')!.click(); fixture.detectChanges();
    const go = () => [...el().querySelectorAll('button')].filter((b) => b.textContent?.trim() === 'Unlock').pop() as HTMLButtonElement;
    expect(go().disabled).toBe(true);
    const box = el().querySelector<HTMLInputElement>('[aria-label="Unlock"] input')!; box.value = 'ab'; box.dispatchEvent(new Event('input')); fixture.detectChanges();
    expect(go().disabled).toBe(true);
    box.value = 'Parent complaint'; box.dispatchEvent(new Event('input')); fixture.detectChanges();
    go().click();
    const req = http.expectOne(`${API}/api/ReportClass/unlock`);
    expect(req.request.body).toEqual({ termId: 5, sectionId: 43, reason: 'Parent complaint' });
    req.flush(env({ termId: 5, sectionId: 43, locked: false }));
    http.expectOne((x) => x.url === `${API}/api/ReportClass/completeness`).flush(env(COMP()));
  });

  it('the Principal sees every section, read-only: no lock buttons and no edit tabs content to change', () => {
    open('Principal', [row({ canReview: false, canEnter: false })], COMP({ canEdit: false, canLock: false }));
    expect(text()).toContain('Term 1');
    expect(button('Lock term')).toBeUndefined();
  });

  it('the four tabs are there; Activity grades loads its tab for the first term', () => {
    open('Teacher', [row()]);
    expect([...el().querySelectorAll('[role=tab]')].map((t) => t.textContent?.trim())).toEqual(['Marks', 'Activity grades', 'Remarks and attendance', 'Promotion']);
    (el().querySelectorAll('[role=tab]')[1] as HTMLButtonElement).click(); fixture.detectChanges();
    const req = http.expectOne((x) => x.url === `${API}/api/ReportClass/activity-grades`);
    expect(req.request.params.get('termId')).toBe('5');
    req.flush(env({ termId: 5, termName: 'Term 1', sectionId: 43, locked: false, canEdit: true, areas: [{ areaId: 1, category: 'DISCIPLINE', name: 'Punctuality', options: [{ code: 'A', label: 'Always' }] }], students: [{ studentId: 'S1', rollNumber: '1', fullName: 'Asha', grades: {} }] }));
    fixture.detectChanges();
    expect(text()).toContain('Punctuality');
  });
});
