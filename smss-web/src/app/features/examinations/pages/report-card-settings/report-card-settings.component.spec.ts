import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ToastService } from '../../../../core/services/toast.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { ReportCardSettings } from '../../models/report-card-settings.model';
import { ReportCardSettingsComponent } from './report-card-settings.component';

const API = 'https://localhost:7037';
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const PNG = 'iVBORw0KGgo=';

const DESIGNS = [
  { code: 'term-spread', name: 'Term Spread', description: 'Terms side by side', modes: ['MARKS'], orientation: 'Landscape', available: true, supportsPhoto: false },
  { code: 'classic-card', name: 'Classic Card', description: 'One page per term set', modes: ['MARKS'], orientation: 'Portrait', available: true, supportsPhoto: false },
  { code: 'skill-grid', name: 'Skill Grid', description: 'Skills and grades', modes: ['GRADES'], orientation: 'Landscape', available: true, supportsPhoto: true },
  { code: 'playful-report', name: 'Playful Report', description: 'Descriptive', modes: ['GRADES'], orientation: 'Portrait', available: true, supportsPhoto: true },
  { code: 'board-style', name: 'Board Style', description: 'Scholastic and co-scholastic', modes: ['MARKS', 'GRADES'], orientation: 'Portrait', available: true, supportsPhoto: true },
  { code: 'minimal-bw', name: 'Minimal', description: 'Ink-saving', modes: ['MARKS', 'GRADES'], orientation: 'Portrait', available: true, supportsPhoto: false },
];
const SCHEMES = [
  { schemeId: 7, schemeName: 'Term-wise Class 3', evaluationMode: 'MARKS', templateKey: 'term-spread', classNames: ['Class 3'] },
  { schemeId: 8, schemeName: 'Nursery grades', evaluationMode: 'GRADES', templateKey: 'skill-grid', classNames: ['Nursery'] },
];
const DEFAULTS: ReportCardSettings = {
  primaryColor: '#1F3A5F', secondaryColor: '#C8963E', headerLine: null, recognitionText: null, udiseCode: null, footerText: null, rulesText: null, hasSignature: false,
  showCover: false, showRules: false, showAttendance: true, showGradeLegend: true, showPhoto: false, isDefault: true, updatedAt: null, schoolName: 'Little Angels School', hasLogo: true,
  designs: DESIGNS, schemes: SCHEMES, defaultPrimaryColor: '#1F3A5F', defaultSecondaryColor: '#C8963E',
};

describe('ReportCardSettingsComponent', () => {
  let fixture: ComponentFixture<ReportCardSettingsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const previewRequests = () => http.match((r) => r.url === `${API}/api/ReportCardSettings/preview`);
  const answerPreviews = (reqs: TestRequest[]) => reqs.forEach((r) => r.flush(env({ templateKey: r.request.body.templateKey, isSample: true, pageCount: 1, pages: [PNG] })));
  const settle = () => {
    vi.advanceTimersByTime(400); // the screen waits for the school to stop editing before it re-renders
    fixture.detectChanges();
  };

  function open(settings: ReportCardSettings = DEFAULTS, roles: string[] = ['School Admin']) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'sch2026006', roles }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReportCardSettingsComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/ReportCardSettings`).flush(env(settings));
    fixture.detectChanges();
    settle();
    const first = previewRequests();
    answerPreviews(first);
    fixture.detectChanges();
    return first;
  }

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    http.verify();
    vi.useRealTimers();
    vi.restoreAllMocks();
    clearSessions();
  });

  it('shows all six designs as live previews (one render each, nothing "coming soon") with the kind of scheme each one is for', () => {
    const first = open();
    expect(first.map((r) => r.request.body.templateKey).sort()).toEqual(['board-style', 'classic-card', 'minimal-bw', 'playful-report', 'skill-grid', 'term-spread']);
    expect(el().querySelectorAll('.tpl')).toHaveLength(6);
    expect(el().querySelectorAll('.tpl .face img')).toHaveLength(6);
    expect(el().textContent).not.toContain('Coming soon');
    expect([...el().querySelectorAll<HTMLButtonElement>('.tpl')].every((b) => !b.disabled)).toBe(true);
    const text = (name: string) => [...el().querySelectorAll<HTMLButtonElement>('.tpl')].find((b) => b.textContent?.includes(name))!.textContent ?? '';
    expect(text('Skill Grid')).toContain('Grades'); expect(text('Term Spread')).toContain('Marks'); expect(text('Board Style')).toContain('Marks + Grades');
  });

  it('asks for a GRADES-only design without a mode (the server draws its own sample) and a both-mode design in the chosen sample kind', () => {
    const first = open();
    const body = (code: string) => first.find((r) => r.request.body.templateKey === code)!.request.body;
    expect(body('skill-grid').mode).toBeUndefined();
    expect(body('classic-card').mode).toBeUndefined();
    expect(body('board-style').mode).toBe('MARKS');
    cmp()['choose']('board-style'); fixture.detectChanges();
    const grades = [...el().querySelectorAll<HTMLButtonElement>('.terms-pick button')].find((b) => b.textContent?.includes('Grades sample'))!;
    grades.click(); settle();
    const again = previewRequests();
    expect(again.find((r) => r.request.body.templateKey === 'board-style')!.request.body.mode).toBe('GRADES');
    answerPreviews(again); fixture.detectChanges();
  });

  it('the photo switch is shown only when the design being looked at has a photo slot, and saving sends it', () => {
    open();
    const switches = () => [...el().querySelectorAll('.switch .text')].map((s) => s.textContent?.trim());
    cmp()['choose']('classic-card'); fixture.detectChanges();
    expect(switches()).not.toContain('Student photo');
    cmp()['choose']('skill-grid'); fixture.detectChanges();
    expect(switches()).toContain('Student photo');
    cmp()['choose']('minimal-bw'); fixture.detectChanges();
    expect(switches()).not.toContain('Student photo');
    cmp()['choose']('playful-report'); fixture.detectChanges();
    cmp()['form'].patchValue({ showPhoto: true }); settle();
    answerPreviews(previewRequests().filter((r) => r.request.body.settings.showPhoto === true));
    const saveBtn = [...el().querySelectorAll<HTMLButtonElement>('app-page-header button')].find((b) => b.textContent?.includes('Save settings'))!;
    saveBtn.click();
    const put = http.expectOne(`${API}/api/ReportCardSettings`);
    expect(put.request.body.showPhoto).toBe(true);
    put.flush(env({ ...DEFAULTS, showPhoto: true, isDefault: false }));
  });

  it('re-renders with the NEW colour (unsaved) after an edit, once the school stops dragging, and sends the settings as they are on screen', () => {
    open();
    cmp()['setColor']('primaryColor', '#0B6E4F');
    cmp()['setColor']('primaryColor', '#0B6E50');
    settle();
    const again = previewRequests();
    expect(again).toHaveLength(6); // one render per design (six), not one per keystroke
    expect(again[0].request.body.settings.primaryColor).toBe('#0B6E50');
    answerPreviews(again);
    fixture.detectChanges();
    expect(el().querySelector('.unsaved')?.textContent).toContain('Unsaved');
  });

  it('does not render while a field is invalid', () => {
    open();
    cmp()['form'].controls.headerLine.setValue('x'.repeat(151));
    settle();
    expect(previewRequests()).toHaveLength(0);
    expect(el().textContent).toContain('Too long');
  });

  it('saves the settings (trimmed, blank as null) and then no longer shows unsaved changes', () => {
    open();
    cmp()['form'].patchValue({ headerLine: '  Affiliated to the Board  ', footerText: '   ', showCover: true });
    settle();
    answerPreviews(previewRequests());
    const saveBtn = [...el().querySelectorAll<HTMLButtonElement>('app-page-header button')].find((b) => b.textContent?.includes('Save settings'))!;
    expect(saveBtn.disabled).toBe(false);
    saveBtn.click();
    const put = http.expectOne(`${API}/api/ReportCardSettings`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toMatchObject({ headerLine: 'Affiliated to the Board', footerText: null, showCover: true, primaryColor: '#1F3A5F' });
    const toast = vi.spyOn(TestBed.inject(ToastService), 'success');
    put.flush(env({ ...DEFAULTS, headerLine: 'Affiliated to the Board', showCover: true, isDefault: false }));
    fixture.detectChanges();
    expect(toast).toHaveBeenCalled();
    expect(el().querySelector('.unsaved')).toBeNull();
  });

  it('lets the Principal look but not change anything: no Save, no signature buttons, fields and scheme choices disabled', () => {
    open(DEFAULTS, ['Principal']);
    expect(el().textContent).toContain('Only the School Admin can change them');
    expect([...el().querySelectorAll('app-page-header button')].some((b) => b.textContent?.includes('Save'))).toBe(false);
    expect(el().querySelector('.file-btn')).toBeNull();
    expect(cmp()['form'].disabled).toBe(true);
    expect([...el().querySelectorAll<HTMLSelectElement>('.schemes select')].every((s) => s.disabled)).toBe(true);
  });

  it('offers for each scheme only the designs that fit its mode: a wrong-mode design is listed but disabled with the reason', () => {
    open();
    const [marks, grades] = [...el().querySelectorAll<HTMLSelectElement>('.schemes select')];
    const opt = (s: HTMLSelectElement, code: string) => [...s.options].find((o) => o.value === code)!;
    expect(opt(marks, 'term-spread').disabled).toBe(false);
    expect(opt(marks, 'classic-card').disabled).toBe(false);
    expect(opt(marks, 'skill-grid').disabled).toBe(true);
    expect(opt(marks, 'skill-grid').textContent).toContain('not for this scheme');
    expect(opt(marks, 'playful-report').disabled).toBe(true);
    expect(opt(marks, 'board-style').disabled).toBe(false);
    expect(opt(marks, 'minimal-bw').disabled).toBe(false);
    expect(opt(grades, 'board-style').disabled).toBe(false);
    expect(opt(grades, 'minimal-bw').disabled).toBe(false);
    expect(opt(grades, 'classic-card').disabled).toBe(true);
    expect(opt(grades, 'classic-card').textContent).toContain('not for this scheme');
  });

  it('saves a scheme design straight away; a refused change puts the old choice back', () => {
    open();
    const select = el().querySelectorAll<HTMLSelectElement>('.schemes select')[0];
    select.value = 'classic-card';
    select.dispatchEvent(new Event('change'));
    const put = http.expectOne(`${API}/api/ReportCardSettings/scheme-design`);
    expect(put.request.body).toEqual({ schemeId: 7, templateKey: 'classic-card' });
    put.flush(env({}));
    fixture.detectChanges();
    expect(cmp()['schemes']()[0].templateKey).toBe('classic-card');

    select.value = 'term-spread';
    select.dispatchEvent(new Event('change'));
    http.expectOne(`${API}/api/ReportCardSettings/scheme-design`).flush({ status: false, statusCode: 409, message: 'No.', data: null }, { status: 409, statusText: 'Conflict' });
    expect(select.value).toBe('classic-card');
  });

  it('refuses a signature that is not an image or is over 2 MB without calling the server', () => {
    open();
    const toast = vi.spyOn(TestBed.inject(ToastService), 'error');
    const pick = (file: File) => {
      const input = el().querySelector<HTMLInputElement>('.file-btn input')!;
      Object.defineProperty(input, 'files', { value: [file], configurable: true });
      input.dispatchEvent(new Event('change'));
    };
    pick(new File(['x'], 'sig.pdf', { type: 'application/pdf' }));
    pick(new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'big.png', { type: 'image/png' }));
    expect(toast).toHaveBeenCalledTimes(2);
    http.expectNone(`${API}/api/ReportCardSettings/signature`);
  });

  it('shows a calm retry when the settings cannot be loaded', () => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'sch2026006', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReportCardSettingsComponent);
    fixture.detectChanges();
    expect(el().textContent).toContain('Loading');
    http.expectOne(`${API}/api/ReportCardSettings`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(el().textContent).toContain('could not be loaded');
    (el().querySelector('.note button') as HTMLButtonElement).click();
    http.expectOne(`${API}/api/ReportCardSettings`).flush(env(DEFAULTS));
  });
});
