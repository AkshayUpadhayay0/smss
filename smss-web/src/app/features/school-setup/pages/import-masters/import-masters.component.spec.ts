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
  admissionTypes: [item('Existing Student')],
  documentTypes: [item('BIRTH_CERT', false, { name: 'Birth Certificate', appliesTo: 'STUDENT', isRequired: true })],
};

describe('ImportMastersComponent', () => {
  let fixture: ComponentFixture<ImportMastersComponent>;
  let http: HttpTestingController;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>; // protected members
  const el = () => fixture.nativeElement as HTMLElement;
  const choice = (name: string) => [...el().querySelectorAll<HTMLLabelElement>('label.choice')].find((l) => l.textContent?.includes(name))!;
  const box = (name: string) => choice(name).querySelector<HTMLInputElement>('input')!;
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
    for (const t of ['Classes', 'Subjects', 'Employee Designations', 'Employee Departments', 'Admission Types', 'Document Types']) expect(text).toContain(t);
    expect(choice('UKG').textContent).toContain('Already added');
    expect(box('UKG').disabled).toBe(true);
    expect(choice('Birth Certificate').textContent).toContain('Students · Required');
  });

  it('cannot import until something is ticked', () => {
    expect(importButton().disabled).toBe(true);
    box('LKG').click();
    fixture.detectChanges();
    expect(importButton().disabled).toBe(false);
  });

  it('"Select all" picks only what can still be added', () => {
    cmp()['toggleAll'](cmp()['groups'][0]);
    fixture.detectChanges();
    expect(box('Nursery').checked && box('LKG').checked && box('Class 1').checked).toBe(true);
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
      classes: ['LKG', 'Class 1'], subjects: ['English'], designations: [], departments: [], admissionTypes: [], documentTypes: [],
    });
    const zero = { created: 0, skipped: 0 };
    req.flush(env({ classes: { created: 2, skipped: 0 }, sections: { created: 2, skipped: 0 }, subjects: { created: 1, skipped: 0 },
      designations: zero, departments: zero, admissionTypes: zero, documentTypes: zero, totalCreated: 5 }));
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
});
