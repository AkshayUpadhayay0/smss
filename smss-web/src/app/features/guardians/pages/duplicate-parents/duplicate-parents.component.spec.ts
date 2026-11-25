import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { canAccess } from '../../../../core/config/route-roles';
import { clearSessions, makeUser, seedSession } from '../../../../core/testing/auth-test-utils';
import { DuplicateReport, MergePreview } from '../../models/guardian.model';
import { DuplicateParentsComponent } from './duplicate-parents.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const rec = (id: number, name: string, oldest: boolean, over: Record<string, unknown> = {}) => ({
  guardianId: id, name, relationship: 'FATHER', mobileNumber: '9200220047', hasWhatsApp: false, hasEmail: false, hasLogin: false, createdAt: '2026-11-01T00:00:00Z', isOldest: oldest,
  students: [{ studentId: `S${id}`, studentName: `Child ${id}`, admissionNumber: `ADM${id}`, className: 'Class 3', sectionName: 'A', isPrimary: true }], ...over,
});
const REPORT: DuplicateReport = {
  groupCount: 2, guardianCount: 5,
  groups: [
    { phoneKey: '9200220047', nameMatch: 'SAME', guardians: [rec(1, 'Neel Tomar', true), rec(2, 'Neel Tomar', false)] },
    { phoneKey: '9300000001', nameMatch: 'DIFFERENT', guardians: [rec(3, 'Asha Rao', true), rec(4, 'Ravi Kumar', false), rec(5, 'Meena Das', false)] },
  ],
};
const PREVIEW: MergePreview = {
  survivor: { guardianId: 1, name: 'Neel Tomar', relationship: 'FATHER', mobileNumber: '9200220047', whatsAppNumber: null, email: null, occupation: null, hasLogin: false },
  absorbed: { guardianId: 2, name: 'Neel Tomar', relationship: 'FATHER', mobileNumber: '9200220047', whatsAppNumber: '+919200220047', email: 'n@example.com', occupation: null, hasLogin: false },
  links: [{ studentId: 'S2', studentName: 'Child 2', admissionNumber: 'ADM2', className: 'Class 3', sectionName: 'A', relationship: 'FATHER', isPrimary: true, action: 'MOVE' }],
  fieldsToCopy: { whatsapp_number: '+919200220047', email: 'n@example.com' },
  applications: [{ admissionId: 9, applicationNumber: 'ADM20260009', role: 'PRIMARY', action: 'REPOINT' }],
  guardiansToRepoint: [7], account: 'NONE', canMerge: true, blockers: [],
};

describe('DuplicateParentsComponent', () => {
  let fixture: ComponentFixture<DuplicateParentsComponent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const cmp = () => fixture.componentInstance as unknown as Record<string, any>;

  function open(report: DuplicateReport = REPORT) {
    clearSessions();
    seedSession({ user: makeUser({ schoolId: 'S', userType: 'Employee', roles: ['School Admin'] }) });
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DuplicateParentsComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/Guardians/duplicates`).flush(env(report));
    fixture.detectChanges();
  }
  afterEach(() => {
    http.match(() => true);
    clearSessions();
  });

  it('lists the groups with the name flag, the students and the oldest record pre-selected as the one to keep', () => {
    open();
    expect(el().querySelector('[data-count="groups"]')!.textContent).toContain('2 groups · 5 records');
    expect([...el().querySelectorAll('[data-badge]')].map((b) => b.textContent!.trim())).toEqual(['Same name', 'Different names']);
    expect(el().querySelector('[data-guardian="1"]')!.textContent).toContain('Keep this record (oldest)');
    expect(el().querySelector('[data-guardian="1"]')!.textContent).toContain('ADM1');
    expect((el().querySelector('[data-guardian="1"] input[type="radio"]') as HTMLInputElement).checked).toBe(true);
    // the kept record has no merge button; the others do
    expect(el().querySelector('[data-guardian="1"] [data-action="merge-into"]')).toBeNull();
    expect(el().querySelectorAll('[data-guardian="2"] [data-action="merge-into"]').length).toBe(1);
  });

  it('says so when no two records share a number', () => {
    open({ groupCount: 0, guardianCount: 0, groups: [] });
    expect(el().querySelector('[data-state="empty"]')!.textContent).toContain('No two parent records share a mobile number');
  });

  it('keeping another record changes which records offer the merge', () => {
    open();
    cmp()['keep'](REPORT.groups[1], 4);
    fixture.detectChanges();
    expect(el().querySelector('[data-guardian="4"] [data-action="merge-into"]')).toBeNull();
    expect(el().querySelector('[data-guardian="3"] [data-action="merge-into"]')).not.toBeNull();
  });

  it('Merge opens the preview of that pair (the kept record survives) and sends nothing yet', () => {
    open();
    cmp()['open'](REPORT.groups[0], 2);
    fixture.detectChanges();
    const req = http.expectOne(`${API}/api/Guardians/merge/preview`);
    expect(req.request.body).toMatchObject({ survivorId: 1, absorbedId: 2 });
    req.flush(env(PREVIEW));
    http.expectNone(`${API}/api/Guardians/merge`);
  });

  it('only School Admin and Principal may open the page', () => {
    for (const role of ['School Admin', 'Principal']) expect(canAccess('/duplicate-parents', [role])).toBe(true);
    for (const role of ['Admission Staff', 'Teacher', 'Accountant', 'Student', 'Guardian', 'Super Admin']) expect(canAccess('/duplicate-parents', [role])).toBe(false);
  });
});
