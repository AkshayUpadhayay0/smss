import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { canAccess } from '../../../../core/config/route-roles';
import { PromotionPreview, PromotionRow } from '../../models/promotion.model';
import { PromotionComponent, defaultYears } from './promotion.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const year = (id: number, name: string, start: string, over: Record<string, unknown> = {}) => ({ academicYearId: id, yearName: name, startDate: start, endDate: start, isCurrent: false, isClosed: false, ...over });
const YEARS = [year(1, '2025-26', '2025-04-01', { isClosed: true }), year(2, '2026-27', '2026-04-01', { isCurrent: true }), year(3, '2027-28', '2027-04-01')];
const CLASSES = [{ classId: 10, className: 'Class 1', sequenceOrder: 1 }, { classId: 11, className: 'Class 2', sequenceOrder: 2 }];
const SECTIONS = [{ sectionId: 100, classId: 10, sectionName: 'A' }, { sectionId: 101, classId: 10, sectionName: 'B' }];
const ROW = (over: Partial<PromotionRow> = {}): PromotionRow => ({
  studentId: 'S1', admissionNumber: 'AD1', studentName: 'Asha Rao', sourceEnrollmentId: 5, sourceSectionId: 100, sourceSectionName: 'A', sourceRollNumber: '1', teacherDecision: 'PROMOTED', decision: 'PROMOTE', decisionSource: 'TEACHER',
  target: { classId: 11, className: 'Class 2', sectionId: 200, sectionName: 'A' }, promoteDefault: null, repeatDefault: null, rollNumber: '1', status: 'READY', blockedByDues: false, outstanding: 0, notYetDue: 0, dues: [], issues: [], notes: [], ...over,
});
const PREVIEW = (rows: PromotionRow[], over: Partial<PromotionPreview> = {}): PromotionPreview => ({
  sourceYearId: 2, sourceYearName: '2026-27', sourceYearClosed: false, targetYearId: 3, targetYearName: '2027-28', scope: 'CLASS', classId: 10, className: 'Class 1', sectionId: null, sectionName: null, promotionDate: '2027-03-01',
  nextClassId: 11, nextClassName: 'Class 2', isLastClass: false, maxPerRun: 500, classesWithoutFeeStructure: [], needsFeeAcknowledgement: false,
  counts: { total: rows.length, ready: rows.filter((r) => r.status === 'READY').length, blocked: rows.filter((r) => r.status === 'BLOCKED').length, needsAttention: 0, noDecision: 0, alreadyEnrolled: 0, alreadyRecorded: 0 },
  targetOptions: [{ classId: 11, className: 'Class 2', hasFeeStructure: true, sections: [{ sectionId: 200, sectionName: 'A', maxStrength: 2, enrolled: 0 }, { sectionId: 201, sectionName: 'B', maxStrength: null, enrolled: 0 }] }], rows, ...over,
});

describe('defaultYears', () => {
  it('starts from the current year and goes to the first open later one', () => {
    expect(defaultYears(YEARS as never)).toEqual({ source: 2, target: 3 });
    expect(defaultYears([YEARS[0]] as never)).toEqual({ source: 1, target: null });
  });
});

describe('PromotionComponent', () => {
  let fixture: ComponentFixture<PromotionComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(PromotionComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/AcademicYear`).flush(env(YEARS));
    http.expectOne(`${API}/api/Classes`).flush(env(CLASSES));
    http.expectOne(`${API}/api/Sections`).flush(env(SECTIONS));
    fixture.detectChanges();
  }
  function preview(p: PromotionPreview) {
    cmp()['classId'].set(10);
    cmp()['runPreview']();
    http.expectOne(`${API}/api/Promotion/preview`).flush(env(p));
    fixture.detectChanges();
  }
  afterEach(() => http.verify());

  it('defaults the years, needs a class before Preview, and sends no decisions the first time', () => {
    open();
    expect(cmp()['sourceId']()).toBe(2);
    expect(cmp()['targetId']()).toBe(3);
    expect(cmp()['canPreview']()).toBe(false);
    cmp()['classId'].set(10);
    cmp()['runPreview']();
    const req = http.expectOne(`${API}/api/Promotion/preview`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ sourceYearId: 2, targetYearId: 3, classId: 10, sectionId: null, promotionDate: null, decisions: [] });
    req.flush(env(PREVIEW([])));
    fixture.detectChanges();
    expect(root().querySelector('[data-empty]')).not.toBeNull();
  });

  it('says it is a preview, shows the counts and the rows with the teacher decision prefilled', () => {
    open();
    preview(PREVIEW([
      ROW(),
      ROW({ studentId: 'S2', studentName: 'Ben', status: 'BLOCKED', blockedByDues: true, outstanding: 500, notYetDue: 300, decision: 'REPEAT', dues: [{ feeDueId: 1, feeType: 'Tuition', dueDate: '2026-10-01', amountDue: 500, amountPaid: 0, outstanding: 500 }] }),
    ]));
    expect(root().querySelector('[data-readonly]')?.textContent).toContain('Nothing has been saved');
    expect(root().querySelector('[data-counts]')?.textContent).toContain('1 ready');
    expect(root().querySelector('[data-row="S1"] [data-decision]')).not.toBeNull();
    expect(root().querySelector('[data-row="S1"]')?.textContent).toContain('from the class teacher');
    expect(root().querySelector('[data-row="S2"]')?.textContent).toContain('Blocked');
    expect(root().querySelector('[data-row="S2"]')?.textContent).toContain('300');
    expect(root().querySelector('[data-dues]')).toBeNull();
    (root().querySelector('[data-row="S2"] [data-dues-toggle]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root().querySelector('[data-dues]')?.textContent).toContain('Tuition');   // the expanded per-due list
  });

  it('warns about a target class with no fee structure, and shows issues and notes', () => {
    open();
    preview(PREVIEW([ROW({ issues: ['Class 2 A is full (2 students).'], notes: ['The teacher wrote Class 4'], status: 'NEEDS_ATTENTION' })], { classesWithoutFeeStructure: ['Class 4'], needsFeeAcknowledgement: true }));
    expect(root().querySelector('[data-fee-warning]')?.textContent).toContain('Class 4');
    expect(root().querySelector('[data-detail="S1"]')?.textContent).toContain('is full');
    expect(root().querySelector('[data-detail="S1"]')?.textContent).toContain('teacher wrote');
  });

  it('an edit asks the server again with the admin choice, and changing the class starts over', () => {
    open();
    preview(PREVIEW([ROW({ status: 'NO_DECISION', decision: null, decisionSource: 'NONE', target: null })]));
    const select = root().querySelector('[data-row="S1"] [data-decision]') as HTMLSelectElement;
    select.value = 'PASS_OUT';
    select.dispatchEvent(new Event('change'));
    const req = http.expectOne(`${API}/api/Promotion/preview`);
    expect(req.request.body.decisions).toEqual([{ studentId: 'S1', decision: 'PASS_OUT', toClassId: null, toSectionId: null, rollNumber: null }]);
    req.flush(env(PREVIEW([ROW({ decision: 'PASS_OUT', decisionSource: 'ADMIN', target: null })])));
    fixture.detectChanges();
    cmp()['pickClass']('11');
    fixture.detectChanges();
    expect(cmp()['result']()).toBeNull();
    expect(cmp()['edits']()).toEqual({});
  });

  it('a skipped row cannot be edited and a failed preview shows a calm message', () => {
    open();
    preview(PREVIEW([ROW({ status: 'ALREADY_ENROLLED', decision: null, target: null })]));
    expect(root().querySelector('[data-row="S1"] [data-decision]')).toBeNull();
    cmp()['runPreview']();
    http.expectOne(`${API}/api/Promotion/preview`).flush({ status: false, statusCode: 400, message: 'x', data: null }, { status: 400, statusText: 'Bad' });
    fixture.detectChanges();
    expect(root().querySelector('[data-failed]')).not.toBeNull();
  });

  it('only the School Admin and the Principal may open the page', () => {
    expect(canAccess('/promotion', ['School Admin'])).toBe(true);
    expect(canAccess('/promotion', ['Principal'])).toBe(true);
    for (const r of ['Super Admin', 'Teacher', 'Accountant', 'Admission Staff', 'Student', 'Guardian']) expect(canAccess('/promotion', [r])).toBe(false);
  });
});
