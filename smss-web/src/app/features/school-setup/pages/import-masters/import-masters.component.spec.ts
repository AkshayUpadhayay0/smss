import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { MasterImportCatalog } from '../../models/master-import.model';
import { ImportMastersComponent } from './import-masters.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const item = (name: string, alreadyAdded = false, extra = {}) => ({ key: name, name, alreadyAdded, ...extra });

const CATALOG: MasterImportCatalog = {
  defaultSectionName: 'A',
  classes: [item('Nursery'), item('LKG'), item('UKG', true), item('Class 1')],
  subjects: [item('English'), item('Hindi')],
  designations: [item('Principal')],
  departments: [item('Administration')],
  documentTypes: [item('BIRTH_CERT', false, { name: 'Birth Certificate', appliesTo: 'STUDENT', isRequired: true })],
};

describe('ImportMastersComponent', () => {
  let fixture: ComponentFixture<ImportMastersComponent>;
  let http: HttpTestingController;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const el = () => fixture.nativeElement as HTMLElement;
  const choice = (name: string) => [...el().querySelectorAll<HTMLElement>('.choice[role="checkbox"]')].find((l) => l.querySelector('.choice-name')?.textContent === name)!;
  const box = (name: string) => choice(name);
  const checked = (name: string) => choice(name).getAttribute('aria-checked') === 'true';
  const groupCard = (title: string) => el().querySelector<HTMLElement>(`section[aria-label="${title}"]`)!;
  const importButton = () => [...el().querySelectorAll('button')].find((b) => b.textContent?.includes('Import selected')) as HTMLButtonElement;

  beforeEach(() => {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'DPS261013001', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ImportMastersComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/MasterImport/catalog`).flush(env(CATALOG));
    fixture.detectChanges();
  });
  afterEach(() => {
    http.verify();
    clearSessions();
  });

  it('shows every group and marks what the school already has', () => {
    const text = el().textContent ?? '';
    for (const t of ['Classes', 'Subjects', 'Employee Designations', 'Employee Departments', 'Document Types']) expect(text).toContain(t);
    expect(choice('UKG').textContent).toContain('Already added');
    expect(box('UKG').getAttribute('aria-disabled')).toBe('true');
    expect(box('UKG').getAttribute('aria-label')).toBe('UKG, already added');
    expect(choice('Birth Certificate').textContent).toContain('Students · Required');
  });

  it('cannot import until something is ticked', () => {
    expect(importButton().disabled).toBe(true);
    box('LKG').click();
    fixture.detectChanges();
    expect(importButton().disabled).toBe(false);
  });

  it('"Select all" picks only what can still be added', () => {
    cmp()['selectAll'](cmp()['groups'][0]);
    fixture.detectChanges();
    expect(checked('Nursery') && checked('LKG') && checked('Class 1')).toBe(true);
    expect(cmp()['totalSelected']()).toBe(3); // UKG is already there
  });

  it('sends the ticked keys per group after confirmation, then shows the result and refreshes the marks', async () => {
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(true);
    box('LKG').click();
    box('Class 1').click();
    box('English').click();
    fixture.detectChanges();

    await cmp()['runImport']();
    const req = http.expectOne(`${API}/api/MasterImport/import`);
    expect(req.request.body).toEqual({
      classes: ['LKG', 'Class 1'], subjects: ['English'], designations: [], departments: [], documentTypes: [],
    });
    const zero = { created: 0, skipped: 0 };
    req.flush(env({ classes: { created: 2, skipped: 0 }, sections: { created: 2, skipped: 0 }, subjects: { created: 1, skipped: 0 },
      designations: zero, departments: zero, documentTypes: zero, totalCreated: 5 }));
    http.expectOne(`${API}/api/MasterImport/catalog`).flush(env({ ...CATALOG, classes: CATALOG.classes.map((c) => ({ ...c, alreadyAdded: c.alreadyAdded || ['LKG', 'Class 1'].includes(c.name) })) }));
    fixture.detectChanges();

    expect(el().textContent).toContain('Import complete');
    expect(el().textContent).toContain('5 items added');
    expect(choice('LKG').textContent).toContain('Already added');
    expect(cmp()['totalSelected']()).toBe(0);
  });

  it('does nothing when the confirmation is declined', async () => {
    vi.spyOn(TestBed.inject(ConfirmDialogService), 'confirm').mockResolvedValue(false);
    box('LKG').click();
    fixture.detectChanges();
    await cmp()['runImport']();
    http.expectNone(`${API}/api/MasterImport/import`);
    expect(cmp()['totalSelected']()).toBe(1);
  });

  it('reads "N selected · M available · K already added" per section and updates as tiles are picked', () => {
    expect(groupCard('Classes').querySelector('.count')?.textContent).toBe('0 selected · 3 available · 1 already added');
    box('LKG').click();
    fixture.detectChanges();
    expect(groupCard('Classes').querySelector('.count')?.textContent).toBe('1 selected · 3 available · 1 already added');
  });

  it('Select all and Clear work per section and leave other sections alone', () => {
    const buttons = (t: string) => [...groupCard(t).querySelectorAll('button')] as HTMLButtonElement[];
    const label = (t: string, text: string) => buttons(t).find((b) => b.textContent?.trim() === text)!;
    expect(label('Classes', 'Clear').disabled).toBe(true);
    box('English').click();
    label('Classes', 'Select all').click();
    fixture.detectChanges();
    expect(cmp()['selectedIn'](cmp()['groups'][0])).toBe(3);
    expect(checked('UKG')).toBe(true); // shown ticked, but only because it is already added
    expect(cmp()['selection']()['classes']).not.toContain('UKG');
    label('Classes', 'Clear').click();
    fixture.detectChanges();
    expect(cmp()['selection']()['classes']).toEqual([]);
    expect(cmp()['selection']()['subjects']).toEqual(['English']);
  });

  it('already-added tiles cannot be selected, by click or keyboard', () => {
    box('UKG').click();
    box('UKG').dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
    fixture.detectChanges();
    expect(cmp()['totalSelected']()).toBe(0);
    expect(choice('UKG').classList).toContain('disabled');
    expect(choice('UKG').querySelector('.added')?.textContent).toContain('Already added');
  });

  it('a tile is focusable and Space toggles it', () => {
    const tile = box('LKG');
    expect(tile.tabIndex).toBe(0);
    const ev = new KeyboardEvent('keydown', { key: ' ', bubbles: true, cancelable: true });
    tile.dispatchEvent(ev);
    fixture.detectChanges();
    expect(ev.defaultPrevented).toBe(true);
    expect(checked('LKG')).toBe(true);
    tile.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
    fixture.detectChanges();
    expect(checked('LKG')).toBe(false);
  });

  it('reserves room under the tiles equal to the action bar height', () => {
    const groups = el().querySelector<HTMLElement>('.groups')!;
    expect(groups.style.paddingBottom).toBe(`${cmp()['barHeight']()}px`);
    cmp()['barHeight'].set(140);
    fixture.detectChanges();
    expect(groups.style.paddingBottom).toBe('140px');
  });

  it('the bar counts the total across sections and Import is off with nothing picked', () => {
    box('LKG').click();
    box('English').click();
    box('Principal').click();
    fixture.detectChanges();
    expect(el().querySelector('.bar-text')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('3 items selected');
  });

  it('collapses a section that is entirely added into one row, and expands it', () => {
    http.expectNone(() => true);
    cmp()['catalog'].set({ ...CATALOG, subjects: [item('English', true), item('Hindi', true)] });
    fixture.detectChanges();
    expect(groupCard('Subjects').querySelector('.summary-text')?.textContent).toContain('All 2 subjects already added');
    expect(groupCard('Subjects').querySelectorAll('.choice').length).toBe(0);
    expect(([...groupCard('Subjects').querySelectorAll('button')] as HTMLButtonElement[]).find((b) => b.textContent?.trim() === 'All added')!.disabled).toBe(true);
    ([...groupCard('Subjects').querySelectorAll('button')] as HTMLButtonElement[]).find((b) => b.textContent?.trim() === 'Show')!.click();
    fixture.detectChanges();
    expect(groupCard('Subjects').querySelectorAll('.choice').length).toBe(2);
  });

  it('shows an empty message for a section with nothing, not a false zero', () => {
    cmp()['catalog'].set({ ...CATALOG, departments: [] });
    fixture.detectChanges();
    expect(groupCard('Employee Departments').textContent).toContain('Nothing is available');
  });

  it('shows a retry in each section when the catalog fails to load', () => {
    cmp()['catalog'].set(null);
    cmp()['loadFailed'].set(true);
    fixture.detectChanges();
    expect(el().querySelectorAll('[role="alert"]').length).toBe(5);
  });
});
