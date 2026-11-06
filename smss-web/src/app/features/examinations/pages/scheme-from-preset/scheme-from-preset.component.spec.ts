import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { SchemeFromPresetComponent } from './scheme-from-preset.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const PRESETS = [
  { code: 'term-wise-100', name: 'Term-wise, out of 100', description: 'PT, Half-Yearly, Annual', evaluationMode: 'MARKS', templateKey: 'term-wise', scalePresetCode: 'cbse-ix-absolute', scaleName: 'CBSE-style', showRank: true, entryMode: 'MARKS',
    terms: [{ name: 'Term 1', exams: [{ name: 'PT', maxMarks: 20, weight: 20 }, { name: 'Half-Yearly', maxMarks: 80, weight: 80 }] }, { name: 'Term 2', exams: [{ name: 'Annual', maxMarks: 100, weight: 100 }] }], skills: [] },
  { code: 'pre-primary-skill-grid', name: 'Pre-primary skill grid', description: 'Skills', evaluationMode: 'GRADES', templateKey: 'skill-grid', scalePresetCode: 'abc-skills', scaleName: 'ABC', showRank: false, entryMode: 'GRADE', terms: [{ name: 'Term 1', exams: [{ name: 'Assessment', maxMarks: 0, weight: 0 }] }], skills: ['Counting'] },
];
const TEMPLATES = [
  { key: 'term-wise', name: 'Term-wise', description: '', modes: ['MARKS'], orientation: 'portrait', implemented: false },
  { key: 'skill-grid', name: 'Skill grid', description: '', modes: ['GRADES'], orientation: 'portrait', implemented: false },
];
const YEARS = [{ academicYearId: 7, schoolId: 'S', yearName: '2026-27', startDate: '2026-04-01', endDate: '2027-03-31', isCurrent: true, statusName: 'Active', createdAt: '', updatedAt: '' }];
const CLASSES = [
  { classId: 1, schoolId: 'S', className: 'Class 1', sequenceOrder: 1, statusName: 'Active', createdAt: '', updatedAt: '' },
  { classId: 2, schoolId: 'S', className: 'Class 2', sequenceOrder: 2, statusName: 'Active', createdAt: '', updatedAt: '' },
];

describe('SchemeFromPresetComponent (three-step wizard)', () => {
  let fixture: ComponentFixture<SchemeFromPresetComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const button = (label: string) => [...el().querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;
  const click = (label: string) => { button(label).click(); fixture.detectChanges(); };
  const tick = (i: number) => { const box = el().querySelectorAll<HTMLInputElement>('.class-list input')[i]; box.checked = true; box.dispatchEvent(new Event('change')); fixture.detectChanges(); };

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SchemeFromPresetComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/ReportScheme/presets`).flush(env(PRESETS));
    http.expectOne(`${API}/api/ReportScheme/templates`).flush(env(TEMPLATES));
    http.expectOne((r) => r.url.includes('/api/AcademicYear')).flush(env(YEARS));
    http.expectOne((r) => r.url.includes('/api/Class')).flush(env(CLASSES));
    fixture.detectChanges();
  });
  afterEach(() => { http.verify(); clearSessions(); });

  it('cannot go past step 2 until a class is picked', () => {
    click('Next');
    expect((button('Next') as HTMLButtonElement).disabled).toBe(true);
    tick(0);
    expect((button('Next') as HTMLButtonElement).disabled).toBe(false);
  });

  it('creates a scheme in three steps: preset, classes (picked), review; sends an even split of the year as term dates', () => {
    click('Next');
    tick(1);
    click('Next');
    expect(el().textContent).toContain('Term dates');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    click('Create scheme');
    const req = http.expectOne(`${API}/api/ReportScheme/from-preset`);
    expect(req.request.method).toBe('POST');
    const b = req.request.body;
    expect(b.presetCode).toBe('term-wise-100');
    expect(b.academicYearId).toBe(7);
    expect(b.classIds).toEqual([2]);
    expect(b.termDates).toEqual([{ startDate: '2026-04-01', endDate: '2026-09-29' }, { startDate: '2026-09-30', endDate: '2027-03-31' }]);
    expect(b.templateKey).toBe('term-wise');
    expect(b.subjectPassPercent).toBe(33);
    req.flush(env({ schemeId: 99 }));
    expect(navigate).toHaveBeenCalledWith(['/examinations/schemes', 99]);
  });

  it('a grades-only preset sends no pass marks or rounding', () => {
    el().querySelectorAll<HTMLButtonElement>('.preset')[1].click();
    fixture.detectChanges();
    click('Next');
    tick(0);
    click('Next');
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    click('Create scheme');
    const req = http.expectOne(`${API}/api/ReportScheme/from-preset`);
    expect(req.request.body.presetCode).toBe('pre-primary-skill-grid');
    expect(req.request.body.subjectPassPercent).toBeUndefined();
    expect(req.request.body.percentageRounding).toBeUndefined();
    req.flush(env({ schemeId: 5 }));
  });
});
