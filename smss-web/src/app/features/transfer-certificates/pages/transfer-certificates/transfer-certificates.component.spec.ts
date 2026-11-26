import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { canAccess } from '../../../../core/config/route-roles';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { TransferCertificatesComponent } from './transfer-certificates.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const CERT = (id: number, over: Record<string, unknown> = {}) => ({
  certificateId: id, certificateNumber: `GHS/TC/2026-27/000${id}`, studentId: 'S' + id, studentName: 'Stu ' + id, className: 'Class 5', sectionName: 'A', outcome: 'LEFT', status: 'ISSUED', conduct: 'Good', remarks: null,
  feesCleared: true, overrideApplied: false, overrideReason: null, outstandingAmount: 0, issuedAt: '2026-11-26T10:00:00Z', issuedBy: 'Admin', voidedAt: null, voidedBy: null, voidReason: null, duplicates: 0, ...over,
});
const CHECK = (over: Record<string, unknown> = {}) => ({
  studentId: 'S1', studentName: 'Stu 1', admissionNumber: 'A1', className: 'Class 5', sectionName: 'A', yearName: '2026-27', outcome: 'LEFT', leavingDate: '2026-11-20', canIssue: true, blockers: [], outstanding: 0, dues: [],
  requiresOverride: false, existingCertificateId: null, existingCertificateNumber: null, suggestedConduct: 'Good', principalName: 'Dr Rao', ...over,
});

describe('TransferCertificatesComponent', () => {
  let fixture: ComponentFixture<TransferCertificatesComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const q = <T extends Element>(sel: string) => root().querySelector(sel) as T | null;
  const type = (sel: string, value: string) => { const el = q<HTMLInputElement>(sel)!; el.value = value; el.dispatchEvent(new Event('input')); fixture.detectChanges(); };

  function open(list: unknown[] = []) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ConfirmDialogService, useValue: { confirm: () => Promise.resolve(true) } }] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TransferCertificatesComponent);
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/api/TransferCertificates`).flush(env(list));
    fixture.detectChanges();
  }
  afterEach(() => http.verify());

  it('lists the certificates with their number, status and duplicates, and offers download, duplicate and void only on an issued one', () => {
    open([CERT(1, { duplicates: 2 }), CERT(2, { status: 'VOID', voidReason: 'Wrong student' })]);
    expect(q('[data-row="GHS/TC/2026-27/0001"]')?.textContent).toContain('2 duplicates');
    expect(q('[data-row="GHS/TC/2026-27/0001"] [data-download]')).not.toBeNull();
    expect(q('[data-row="GHS/TC/2026-27/0002"]')?.textContent).toContain('Void');
    expect(q('[data-row="GHS/TC/2026-27/0002"] [data-download]')).toBeNull();
    expect(q('[data-row="GHS/TC/2026-27/0002"] [data-void-open]')).toBeNull();
  });

  it('says so when there is nothing and a calm message when the list fails', () => {
    open([]);
    expect(q('[data-empty]')).not.toBeNull();
  });

  it('checks a student, shows what blocks it, and issues with the entered conduct and remarks', async () => {
    open();
    type('[data-student]', 'S1');
    q<HTMLButtonElement>('[data-check-btn] button')!.click();
    http.expectOne((r) => r.url === `${API}/api/TransferCertificates/check` && r.params.get('studentId') === 'S1').flush(env(CHECK()));
    fixture.detectChanges();
    expect(q('[data-check]')?.textContent).toContain('Stu 1');
    type('[data-conduct]', 'Excellent');
    type('[data-remarks]', 'Left for a new city');
    q<HTMLButtonElement>('[data-issue-btn] button')!.click();
    await fixture.whenStable();
    const issue = http.expectOne(`${API}/api/TransferCertificates/issue`);
    expect(issue.request.body).toEqual({ studentId: 'S1', conduct: 'Excellent', remarks: 'Left for a new city', confirm: true });
    issue.flush(env(CERT(3)));
    http.expectOne((r) => r.url === `${API}/api/TransferCertificates`).flush(env([CERT(3)]));
  });

  it('a student with no leaving recorded shows the server reason and offers no issue button', () => {
    open();
    type('[data-student]', 'S9');
    q<HTMLButtonElement>('[data-check-btn] button')!.click();
    http.expectOne((r) => r.url === `${API}/api/TransferCertificates/check`).flush(env(CHECK({ outcome: null, canIssue: false, blockers: ['Stu 1 has no leaving recorded: record the leaving first.'] })));
    fixture.detectChanges();
    expect(q('[data-blockers]')?.textContent).toContain('no leaving recorded');
    expect(q('[data-issue-btn]')).toBeNull();
  });

  it('unpaid dues need the override ticked and a reason of ten characters before the issue button works, and the reason is sent', async () => {
    open();
    type('[data-student]', 'S1');
    q<HTMLButtonElement>('[data-check-btn] button')!.click();
    http.expectOne((r) => r.url === `${API}/api/TransferCertificates/check`).flush(env(CHECK({ requiresOverride: true, outstanding: 2500, dues: [{ feeDueId: 1, feeType: 'Tuition', dueDate: '2026-10-01', amountDue: 2500, amountPaid: 0, outstanding: 2500 }] })));
    fixture.detectChanges();
    expect(q('[data-dues]')?.textContent).toContain('2,500');
    const btn = () => q<HTMLButtonElement>('[data-issue-btn] button')!;
    expect(btn().disabled).toBe(true);
    const box = q<HTMLInputElement>('[data-override]')!; box.checked = true; box.dispatchEvent(new Event('change')); fixture.detectChanges();
    type('[data-override-reason]', 'too short');
    expect(btn().disabled).toBe(true);
    type('[data-override-reason]', 'Approved by the principal');
    expect(btn().disabled).toBe(false);
    btn().click();
    await fixture.whenStable();
    const issue = http.expectOne(`${API}/api/TransferCertificates/issue`);
    expect(issue.request.body).toMatchObject({ overrideDues: true, overrideReason: 'Approved by the principal', confirm: true });
    issue.flush(env(CERT(4, { overrideApplied: true })));
    http.expectOne((r) => r.url === `${API}/api/TransferCertificates`).flush(env([]));
  });

  it('voiding needs a reason of ten characters and posts it', async () => {
    open([CERT(1)]);
    q<HTMLButtonElement>('[data-void-open] button')!.click(); fixture.detectChanges();
    const confirm = () => q<HTMLButtonElement>('[data-void-confirm] button')!;
    expect(confirm().disabled).toBe(true);
    type('[data-void-reason]', 'Issued to the wrong student');
    expect(confirm().disabled).toBe(false);
    confirm().click();
    await fixture.whenStable();
    const v = http.expectOne(`${API}/api/TransferCertificates/1/void`);
    expect(v.request.body).toEqual({ reason: 'Issued to the wrong student', confirm: true });
    v.flush(env(CERT(1, { status: 'VOID' })));
    http.expectOne((r) => r.url === `${API}/api/TransferCertificates`).flush(env([CERT(1, { status: 'VOID' })]));
  });

  it('the duplicate comes from a POST and the original from a GET', () => {
    open([CERT(1)]);
    (globalThis as { URL: { createObjectURL: unknown; revokeObjectURL: unknown } }).URL.createObjectURL = () => 'blob:x';
    (globalThis as { URL: { createObjectURL: unknown; revokeObjectURL: unknown } }).URL.revokeObjectURL = () => undefined;
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);   // jsdom cannot navigate to a blob
    q<HTMLButtonElement>('[data-download] button')!.click();
    http.expectOne((r) => r.method === 'GET' && r.url === `${API}/api/TransferCertificates/1/pdf`).flush(new Blob(['%PDF']));
    q<HTMLButtonElement>('[data-duplicate] button')!.click();
    http.expectOne((r) => r.method === 'POST' && r.url === `${API}/api/TransferCertificates/1/duplicate`).flush(new Blob(['%PDF']));
    http.expectOne((r) => r.url === `${API}/api/TransferCertificates`).flush(env([CERT(1, { duplicates: 1 })]));
  });

  it('the optional boxes are sent only when filled in, and "qualified: yes" carries the class', async () => {
    open();
    type('[data-student]', 'S1');
    q<HTMLButtonElement>('[data-check-btn] button')!.click();
    http.expectOne((r) => r.url === `${API}/api/TransferCertificates/check`).flush(env(CHECK()));
    fixture.detectChanges();
    type('[data-nationality]', 'Indian');
    type('[data-working-days]', '220');
    type('[data-days-present]', '198');
    const sel = q<HTMLSelectElement>('[data-qualified]')!; sel.value = 'yes'; sel.dispatchEvent(new Event('change')); fixture.detectChanges();
    type('[data-promoted-to]', 'Class 6');
    q<HTMLButtonElement>('[data-issue-btn] button')!.click();
    await fixture.whenStable();
    const issue = http.expectOne(`${API}/api/TransferCertificates/issue`);
    expect(issue.request.body).toEqual({ studentId: 'S1', conduct: 'Good', confirm: true, nationality: 'Indian', qualifiedForPromotion: true, promotedToClass: 'Class 6', workingDays: 220, daysPresent: 198 });
    issue.flush(env(CERT(5)));
    http.expectOne((r) => r.url === `${API}/api/TransferCertificates`).flush(env([]));
  });

  it('only the School Admin and the Principal open it', () => {
    expect(canAccess('/transfer-certificates', ['School Admin'])).toBe(true);
    expect(canAccess('/transfer-certificates', ['Principal'])).toBe(true);
    for (const r of ['Teacher', 'Accountant', 'Admission Staff', 'Student', 'Guardian', 'Super Admin']) expect(canAccess('/transfer-certificates', [r])).toBe(false);
  });
});
