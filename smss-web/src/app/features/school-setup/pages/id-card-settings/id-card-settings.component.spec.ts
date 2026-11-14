import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ToastService } from '../../../../core/services/toast.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { IdCardSettings } from '../../models/id-card-settings.model';
import { IdCardSettingsComponent } from './id-card-settings.component';
import { environment } from '../../../../../environments/environment';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });

const TEMPLATES = [
  { code: 'classic-blue', name: 'Classic Blue', description: 'Curved header', defaultPrimaryColor: '#1E5BD8', defaultSecondaryColor: '#F5A623' },
  { code: 'teal-geometric', name: 'Teal Geometric', description: 'Angular shapes', defaultPrimaryColor: '#0E9F94', defaultSecondaryColor: '#F4B400' },
  { code: 'red-curve', name: 'Bold Curve', description: 'Bold curve', defaultPrimaryColor: '#C62828', defaultSecondaryColor: '#FFB300' },
  { code: 'playful-blob', name: 'Playful Blob', description: 'Blob border', defaultPrimaryColor: '#FF7A45', defaultSecondaryColor: '#FFC93C' },
  { code: 'modern-navy', name: 'Modern Navy', description: 'Navy', defaultPrimaryColor: '#14213D', defaultSecondaryColor: '#FF7A18' },
  { code: 'formal-classic', name: 'Formal Classic', description: 'Formal', defaultPrimaryColor: '#1B2A5C', defaultSecondaryColor: '#B08D2A' },
];
const DEFAULTS: IdCardSettings = {
  templateCode: 'classic-blue', primaryColor: '#1E5BD8', secondaryColor: '#F5A623', tagline: null, website: null, email: null, termsText: null, hasSignature: false,
  showDob: true, showAddress: true, showBloodGroup: true, showMotherName: true, isDefault: true, updatedAt: null, templates: TEMPLATES,
};
const SAVED: IdCardSettings = { ...DEFAULTS, templateCode: 'red-curve', primaryColor: '#2E7D32', tagline: 'Learning with Love', isDefault: false, updatedAt: '2026-11-04T10:00:00Z' };
const PNG = 'iVBORw0KGgo=';

describe('IdCardSettingsComponent', () => {
  let fixture: ComponentFixture<IdCardSettingsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const previewRequests = () => http.match((r) => r.url === `${API}/api/IdCardSettings/preview`);
  const answerPreviews = (reqs: TestRequest[]) => reqs.forEach((r) => r.flush(env({ templateCode: r.request.body.templateCode, frontPng: PNG, backPng: PNG, isSample: true })));
  const settle = () => {
    vi.advanceTimersByTime(400); // the screen waits for the school to stop editing before it re-renders
    fixture.detectChanges();
  };

  function open(settings: IdCardSettings = DEFAULTS) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(IdCardSettingsComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/IdCardSettings`).flush(env(settings));
    fixture.detectChanges();
    settle();
    const first = previewRequests();
    answerPreviews(first);
    fixture.detectChanges();
    return first;
  }

  beforeEach(() => {
    vi.useFakeTimers();
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'sch2026006', roles: ['School Admin'] }) });
  });
  afterEach(() => {
    http.verify();
    vi.useRealTimers();
    vi.restoreAllMocks();
    clearSessions();
  });

  it('shows the six designs as live previews (front and back) and renders each one once on open', () => {
    const first = open();
    expect(first).toHaveLength(6);
    expect(first.map((r) => r.request.body.templateCode).sort()).toEqual(TEMPLATES.map((t) => t.code).sort());
    expect(el().querySelectorAll('.tpl')).toHaveLength(6);
    expect(el().querySelectorAll('.tpl .face img')).toHaveLength(12);
    expect(el().querySelector('.tpl.selected')?.textContent).toContain('Classic Blue');
    expect(el().querySelectorAll('.big-face img')).toHaveLength(2);
  });

  it('with no custom colours the previews ask for each template own defaults (null colours)', () => {
    const first = open();
    for (const r of first) expect(r.request.body).toMatchObject({ primaryColor: null, secondaryColor: null });
  });

  it('loads and shows saved settings, and starts clean (Save disabled, no unsaved badge)', () => {
    open(SAVED);
    expect(el().querySelector('.tpl.selected')?.textContent).toContain('Bold Curve');
    expect((el().querySelector('app-input[formcontrolname="tagline"] input') as HTMLInputElement).value).toBe('Learning with Love');
    expect(cmp()['dirty']()).toBe(false);
    expect(cmp()['canSave']()).toBe(false);
    expect(el().querySelector('.unsaved')).toBeNull();
  });

  it('picking another design brings that design own colours when the school never customised them', () => {
    open();
    el().querySelectorAll<HTMLButtonElement>('.tpl')[4].click(); // modern-navy
    fixture.detectChanges();
    expect(cmp()['form'].value).toMatchObject({ templateCode: 'modern-navy', primaryColor: '#14213D', secondaryColor: '#FF7A18' });
    settle();
    expect(previewRequests()).toHaveLength(6);
  });

  it('picking another design KEEPS colours the school chose, and every preview is rendered in them', () => {
    open();
    cmp()['setColor']('primaryColor', '#7B1FA2');
    settle();
    const withBrand = previewRequests();
    expect(withBrand).toHaveLength(6);
    for (const r of withBrand) expect(r.request.body).toMatchObject({ primaryColor: '#7B1FA2', secondaryColor: '#F5A623' });
    answerPreviews(withBrand);

    el().querySelectorAll<HTMLButtonElement>('.tpl')[1].click();
    fixture.detectChanges();
    expect(cmp()['form'].value).toMatchObject({ templateCode: 'teal-geometric', primaryColor: '#7B1FA2' });
    settle();
    answerPreviews(previewRequests());
  });

  it('re-renders only after the school stops editing (one burst of 6 requests, not one per keystroke)', () => {
    open();
    for (const c of ['#111111', '#222222', '#333333']) cmp()['setColor']('primaryColor', c);
    vi.advanceTimersByTime(200);
    expect(previewRequests()).toHaveLength(0);
    settle();
    const burst = previewRequests();
    expect(burst).toHaveLength(6);
    for (const r of burst) expect(r.request.body.primaryColor).toBe('#333333');
    answerPreviews(burst);
  });

  it('coming back to the tab re-renders all six previews (a logo uploaded on another screen shows without a reload); a hidden tab does not', () => {
    open();
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    document.dispatchEvent(new Event('visibilitychange'));
    settle();
    expect(previewRequests()).toHaveLength(0);
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    settle();
    const again = previewRequests();
    expect(again).toHaveLength(6);
    answerPreviews(again);
  });

  it('a failed preview shows a placeholder for that design only and the screen keeps working', () => {
    open();
    cmp()['setColor']('secondaryColor', '#00AA55');
    settle();
    const reqs = previewRequests();
    reqs[0].flush({ status: false, statusCode: 500, message: 'boom', data: null }, { status: 500, statusText: 'Server Error' });
    answerPreviews(reqs.slice(1));
    fixture.detectChanges();
    expect(el().querySelectorAll('.tpl .face img').length).toBe(10);
    expect(el().querySelectorAll('.tpl .face.loading').length).toBe(2);
  });

  it('Save sends the screen values (explicit colours) with PUT and then shows a clean state', () => {
    open(SAVED);
    cmp()['setColor']('primaryColor', '#AA00FF');
    cmp()['form'].controls.tagline.setValue('  New tagline ');
    cmp()['form'].controls.showAddress.setValue(false);
    fixture.detectChanges();
    expect(cmp()['dirty']()).toBe(true);
    expect(el().querySelector('.unsaved')).not.toBeNull();

    cmp()['save']();
    const put = http.expectOne(`${API}/api/IdCardSettings`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toMatchObject({ templateCode: 'red-curve', primaryColor: '#AA00FF', secondaryColor: '#F5A623', tagline: 'New tagline', showAddress: false });
    put.flush(env({ ...SAVED, primaryColor: '#AA00FF', tagline: 'New tagline', showAddress: false }));
    fixture.detectChanges();
    expect(cmp()['dirty']()).toBe(false);
    expect(TestBed.inject(ToastService).toasts().some((t) => t.type === 'success')).toBe(true);
    settle();
    answerPreviews(previewRequests());
  });

  it('a school that never saved can save the defaults once (creates its row)', () => {
    open();
    expect(cmp()['dirty']()).toBe(false);
    expect(cmp()['canSave']()).toBe(true);
  });

  it('blocks Save and explains: bad e-mail, website with spaces, too many terms lines, terms too long', () => {
    open(SAVED);
    const f = cmp()['form'].controls;
    f.email.setValue('nope');
    expect(cmp()['err']('email')).toMatch(/valid e-mail/);
    expect(cmp()['canSave']()).toBe(false);
    f.email.setValue('');
    f.website.setValue('not a site');
    expect(cmp()['err']('website')).toMatch(/no spaces/);
    f.website.setValue('');
    f.termsText.setValue('1\n2\n3\n4\n5\n6');
    expect(cmp()['err']('termsText')).toMatch(/5 short lines/);
    f.termsText.setValue('x'.repeat(301));
    expect(cmp()['err']('termsText')).toMatch(/300/);
    f.termsText.setValue('Carry this card.');
    f.tagline.setValue('changed');
    expect(cmp()['canSave']()).toBe(true);
    settle();
    answerPreviews(previewRequests());
  });

  it('signature: a wrong file type or an oversized file is refused on the screen with no request', () => {
    open();
    const toasts = TestBed.inject(ToastService);
    const pick = (file: File) => cmp()['onSignature']({ target: { files: [file], value: 'x' } } as unknown as Event);
    pick(new File(['x'], 'sig.gif', { type: 'image/gif' }));
    pick(new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'big.png', { type: 'image/png' }));
    expect(toasts.toasts().filter((t) => t.type === 'error')).toHaveLength(2);
    http.expectNone(`${API}/api/IdCardSettings/signature`);
  });

  it('signature: upload posts the file as multipart, marks it on file and refreshes the previews; remove clears it', () => {
    open();
    cmp()['onSignature']({ target: { files: [new File(['png'], 'sig.png', { type: 'image/png' })], value: 'x' } } as unknown as Event);
    const up = http.expectOne(`${API}/api/IdCardSettings/signature`);
    expect(up.request.method).toBe('POST');
    expect(up.request.body instanceof FormData).toBe(true);
    expect((up.request.body as FormData).get('file')).toBeInstanceOf(File);
    up.flush(env({ hasSignature: true }));
    fixture.detectChanges();
    expect(cmp()['hasSignature']()).toBe(true);
    expect(el().textContent).toContain('Signature on file');
    settle();
    answerPreviews(previewRequests());

    cmp()['removeSignature']();
    http.expectOne(`${API}/api/IdCardSettings/signature/remove`).flush(env({ hasSignature: false }));
    fixture.detectChanges();
    expect(cmp()['hasSignature']()).toBe(false);
    settle();
    answerPreviews(previewRequests());
  });

  it('signature: the server reason is shown when the upload is refused', () => {
    open();
    cmp()['onSignature']({ target: { files: [new File(['png'], 'sig.png', { type: 'image/png' })], value: 'x' } } as unknown as Event);
    http.expectOne(`${API}/api/IdCardSettings/signature`).flush({ status: false, statusCode: 400, message: 'The file is not a valid PNG, JPG or WEBP image.', data: null }, { status: 400, statusText: 'Bad Request' });
    expect(TestBed.inject(ToastService).toasts().map((t) => t.message)).toContain('The file is not a valid PNG, JPG or WEBP image.');
    expect(cmp()['hasSignature']()).toBe(false);
  });

  it('shows a retry when the settings cannot be loaded', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(IdCardSettingsComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/IdCardSettings`).flush({ status: false, statusCode: 500, message: 'x', data: null }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(el().textContent).toContain('could not be loaded');
    expect(el().textContent).toContain('Try again');
  });
});
