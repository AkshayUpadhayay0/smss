import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { YearCopyPart, YearCopyResult } from '../../models/academic-setup.model';
import { AcademicYear } from '../../models/academic-year.model';
import { PART_CHOICES, YearCopyWizardComponent, defaultSource, rowsLabel } from './year-copy-wizard.component';

const API = environment.apiUrl;
const envelope = (data: unknown) => ({ status: true, statusCode: 200, message: 'ok', data });

const year = (id: number, name: string, start: string, over: Partial<AcademicYear> = {}) =>
  ({ academicYearId: id, yearName: name, startDate: start, endDate: start.replace(/^(\d+)/, (y) => String(Number(y) + 1)), isCurrent: false, isClosed: false, statusName: 'Active', state: 'PAST', ...over }) as AcademicYear;
const Y1 = year(5, '2025-26', '2025-04-01', { isClosed: true });
const Y2 = year(6, '2026-27', '2026-04-01', { isCurrent: true });
const Y3 = year(7, '2027-28', '2027-04-01');

const part = (p: YearCopyPart['part'], over: Partial<YearCopyPart> = {}): YearCopyPart =>
  ({ part: p, label: p, created: 0, details: [], skippedExisting: 0, skipped: [], skippedWithReason: 0, notes: [], ...over });
const result = (preview: boolean, parts: YearCopyPart[]): YearCopyResult => ({ sourceYearId: 6, sourceYearName: '2026-27', targetYearId: 7, targetYearName: '2027-28', preview, parts });

describe('helpers', () => {
  it('suggests the latest year that starts before the target', () => {
    expect(defaultSource(Y3, [Y1, Y2, Y3])?.academicYearId).toBe(6);
    expect(defaultSource(Y1, [Y1, Y2, Y3])?.academicYearId).toBe(6);   // nothing earlier: any other
    expect(defaultSource(Y1, [Y1])).toBeUndefined();
  });
  it('words row counts', () => {
    expect(rowsLabel(0)).toBe('nothing new');
    expect(rowsLabel(1)).toBe('1 row');
    expect(rowsLabel(5)).toBe('5 rows');
  });
  it('lists the parts in dependency order', () => {
    expect(PART_CHOICES.map((c) => c.part)).toEqual(['CLASSES_SECTIONS', 'SUBJECTS', 'SCHEDULES', 'CLASS_TEACHERS', 'TIMETABLE']);
  });
});

describe('YearCopyWizardComponent', () => {
  let fixture: ComponentFixture<YearCopyWizardComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const button = (label: string) => Array.from(root().querySelectorAll('app-button')).find((b) => b.textContent?.trim().startsWith(label))?.querySelector('button') as HTMLButtonElement;
  const URL = (what: string) => `${API}/api/AcademicSetup/years/7/copy-from/6/${what}`;

  function open() {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(YearCopyWizardComponent);
    fixture.componentRef.setInput('target', Y3);
    fixture.componentRef.setInput('years', [Y1, Y2, Y3]);
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('starts on the latest earlier year with every part ticked, and the target is not a source', () => {
    open();
    const sel = root().querySelector('[data-source]') as HTMLSelectElement;
    expect(sel.value).toBe('6');
    expect(Array.from(sel.options).map((o) => o.value)).toEqual(['5', '6']);
    expect(root().querySelectorAll('[data-part]:checked').length).toBe(5);
  });

  it('Preview sends the ticked parts only and nothing is written until Copy now', () => {
    open();
    (root().querySelector('[data-part="TIMETABLE"]') as HTMLInputElement).click();
    (root().querySelector('[data-part="CLASS_TEACHERS"]') as HTMLInputElement).click();
    fixture.detectChanges();
    button('Preview').click();
    const req = http.expectOne(URL('preview'));
    expect(req.request.body).toEqual({ parts: ['CLASSES_SECTIONS', 'SUBJECTS', 'SCHEDULES'] });
    req.flush(envelope(result(true, [part('CLASSES_SECTIONS', { created: 5, details: [{ kind: 'classes', count: 2 }, { kind: 'sections', count: 3 }], skipped: [{ reason: 'The class is inactive', count: 1, examples: ['Class 3'] }], skippedWithReason: 1 }), part('SUBJECTS', { created: 6, skippedExisting: 2 }), part('SCHEDULES')])));
    fixture.detectChanges();
    expect(root().querySelector('[data-verdict]')?.textContent).toContain('11 rows');
    expect(root().querySelector('[data-verdict]')?.textContent).toContain('Nothing has been saved yet');
    expect(root().querySelector('[data-pv="CLASSES_SECTIONS"]')?.textContent).toContain('The class is inactive');
    expect(root().querySelector('[data-pv="SUBJECTS"]')?.textContent).toContain('2');
    http.expectNone(URL('apply'));
  });

  it('Copy now confirms the copy (confirm = true) and shows what was added', () => {
    open();
    button('Preview').click();
    http.expectOne(URL('preview')).flush(envelope(result(true, [part('SUBJECTS', { created: 4 })])));
    fixture.detectChanges();
    button('Copy now').click();
    const req = http.expectOne(URL('apply'));
    expect(req.request.body.confirm).toBe(true);
    expect(req.request.body.parts.length).toBe(5);
    req.flush(envelope(result(false, [part('SUBJECTS', { created: 4 })])));
    fixture.detectChanges();
    expect(root().querySelector('[data-done]')?.textContent).toContain('4 rows added to 2027-28');
    expect(root().querySelector('[data-done]')?.textContent).toContain('adds nothing');
  });

  it('a copy with nothing new says so', () => {
    open();
    button('Preview').click();
    http.expectOne(URL('preview')).flush(envelope(result(true, [part('SUBJECTS', { skippedExisting: 4 })])));
    fixture.detectChanges();
    expect(button('Nothing to copy')).toBeTruthy();
    button('Nothing to copy').click();
    http.expectOne(URL('apply')).flush(envelope(result(false, [part('SUBJECTS', { skippedExisting: 4 })])));
    fixture.detectChanges();
    expect(root().querySelector('[data-done]')?.textContent).toContain('Nothing new');
  });

  it('with no part ticked Preview is disabled', () => {
    open();
    for (const c of PART_CHOICES) (root().querySelector(`[data-part="${c.part}"]`) as HTMLInputElement).click();
    fixture.detectChanges();
    expect(button('Preview').disabled).toBe(true);
  });

  it('Back returns to the choices without writing', () => {
    open();
    button('Preview').click();
    http.expectOne(URL('preview')).flush(envelope(result(true, [part('SUBJECTS', { created: 1 })])));
    fixture.detectChanges();
    button('Back').click();
    fixture.detectChanges();
    expect(root().querySelector('[data-source]')).not.toBeNull();
  });
});
