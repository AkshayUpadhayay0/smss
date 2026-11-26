import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { canAccess } from '../../../../core/config/route-roles';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { LeavingComponent } from './leaving.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const LEAVER = (id: string, over: Record<string, unknown> = {}) => ({ studentId: id, studentName: 'Stu ' + id, admissionNumber: 'A' + id, className: 'Class 5', sectionName: 'A', outcome: 'LEFT', leavingDate: '2026-11-20', reason: 'Moved', outstanding: 0, overridden: false, recordedBy: 'Admin', yearName: '2026-27', ...over });
const CHECK = (over: Record<string, unknown> = {}) => ({ studentId: 'S1', studentName: 'Stu 1', admissionNumber: 'A1', className: 'Class 5', sectionName: 'A', yearName: '2026-27', enrollmentDate: '2026-04-01', leavingDate: '2026-11-26', canRecord: true, blockers: [], outstanding: 0, dues: [], notYetDue: 0, requiresOverride: false, ...over });

describe('LeavingComponent', () => {
  let fixture: ComponentFixture<LeavingComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const q = <T extends Element>(sel: string) => root().querySelector(sel) as T | null;
  const type = (sel: string, value: string) => { const el = q<HTMLInputElement>(sel)!; el.value = value; el.dispatchEvent(new Event('input')); fixture.detectChanges(); };

  function open(list: unknown[] = []) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ConfirmDialogService, useValue: { confirm: () => Promise.resolve(true) } }] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LeavingComponent);
    fixture.detectChanges();
    http.expectOne(`${API}/api/StudentLeaving/leavers`).flush(env(list));
    fixture.detectChanges();
  }
  afterEach(() => http.verify());

  it('lists the students who left with what, when, why and who recorded it, and filters by year', () => {
    open([LEAVER('S1'), LEAVER('S2', { outcome: 'TRANSFERRED_OUT', overridden: true, yearName: '2025-26' }), LEAVER('S3', { recordedBy: null })]);
    expect(q('[data-row="S1"]')?.textContent).toContain('Left');
    expect(q('[data-row="S2"]')?.textContent).toContain('Transferred out');
    expect(q('[data-row="S2"]')?.textContent).toContain('Dues overridden');
    expect(q('[data-row="S3"]')?.textContent).toContain('Promotion run');
    const sel = q<HTMLSelectElement>('[data-year]')!; sel.value = '2025-26'; sel.dispatchEvent(new Event('change')); fixture.detectChanges();
    expect(q('[data-row="S1"]')).toBeNull();
    expect(q('[data-row="S2"]')).not.toBeNull();
  });

  it('says so when nobody has left', () => {
    open([]);
    expect(q('[data-empty]')).not.toBeNull();
  });

  it('checks a student, then records the leaving with the type, date and reason', async () => {
    open();
    type('[data-student]', 'S1');
    type('[data-date]', '2026-11-20');
    q<HTMLButtonElement>('[data-check-btn] button')!.click();
    http.expectOne((r) => r.url === `${API}/api/StudentLeaving/check` && r.params.get('studentId') === 'S1' && r.params.get('leavingDate') === '2026-11-20').flush(env(CHECK()));
    fixture.detectChanges();
    const btn = () => q<HTMLButtonElement>('[data-record-btn] button')!;
    expect(btn().disabled).toBe(true);                                           // a reason is needed
    const sel = q<HTMLSelectElement>('[data-type]')!; sel.value = 'TRANSFERRED_OUT'; sel.dispatchEvent(new Event('change')); fixture.detectChanges();
    type('[data-reason]', 'Family moved to another city');
    btn().click();
    await fixture.whenStable();
    const rec = http.expectOne(`${API}/api/StudentLeaving/record`);
    expect(rec.request.body).toEqual({ studentId: 'S1', type: 'TRANSFERRED_OUT', leavingDate: '2026-11-20', reason: 'Family moved to another city', confirm: true });
    rec.flush(env({ studentId: 'S1', studentName: 'Stu 1', outcome: 'TRANSFERRED_OUT', leavingDate: '2026-11-20', outstanding: 0, overrideApplied: false }));
    http.expectOne(`${API}/api/StudentLeaving/leavers`).flush(env([LEAVER('S1', { outcome: 'TRANSFERRED_OUT' })]));
  });

  it('unpaid dues need the override ticked and a reason of ten characters, which is sent', async () => {
    open();
    type('[data-student]', 'S1');
    q<HTMLButtonElement>('[data-check-btn] button')!.click();
    http.expectOne((r) => r.url === `${API}/api/StudentLeaving/check`).flush(env(CHECK({ requiresOverride: true, outstanding: 4000, dues: [{ feeDueId: 1, feeType: 'Tuition', dueDate: '2026-10-01', amountDue: 4000, amountPaid: 0, outstanding: 4000 }] })));
    fixture.detectChanges();
    expect(q('[data-dues]')?.textContent).toContain('4,000');
    type('[data-reason]', 'Moved away');
    const btn = () => q<HTMLButtonElement>('[data-record-btn] button')!;
    expect(btn().disabled).toBe(true);
    const box = q<HTMLInputElement>('[data-override]')!; box.checked = true; box.dispatchEvent(new Event('change')); fixture.detectChanges();
    type('[data-override-reason]', 'short');
    expect(btn().disabled).toBe(true);
    type('[data-override-reason]', 'Parent will pay in December');
    expect(btn().disabled).toBe(false);
    btn().click();
    await fixture.whenStable();
    const rec = http.expectOne(`${API}/api/StudentLeaving/record`);
    expect(rec.request.body).toMatchObject({ overrideDues: true, overrideReason: 'Parent will pay in December', confirm: true });
    rec.flush(env({ studentId: 'S1', studentName: 'Stu 1', outcome: 'LEFT', leavingDate: '2026-11-26', outstanding: 4000, overrideApplied: true }));
    http.expectOne(`${API}/api/StudentLeaving/leavers`).flush(env([]));
  });

  it('a student who cannot be recorded shows the server reason and no record button', () => {
    open();
    type('[data-student]', 'S1');
    q<HTMLButtonElement>('[data-check-btn] button')!.click();
    http.expectOne((r) => r.url === `${API}/api/StudentLeaving/check`).flush(env(CHECK({ canRecord: false, blockers: ['Stu 1 has already been recorded as left.'] })));
    fixture.detectChanges();
    expect(q('[data-blockers]')?.textContent).toContain('already been recorded');
    expect(q('[data-record-btn]')).toBeNull();
  });

  it('a correction asks for ten characters, posts them, and shows why a refused one is refused', async () => {
    open();
    type('[data-fix-student]', 'S1');
    q<HTMLButtonElement>('[data-fix-check-btn] button')!.click();
    http.expectOne((r) => r.url === `${API}/api/StudentLeaving/correction/check`).flush(env({ studentId: 'S1', studentName: 'Stu 1', yearName: '2026-27', outcome: 'LEFT', leavingDate: '2026-11-20', canCorrect: true, blockers: [] }));
    fixture.detectChanges();
    const btn = () => q<HTMLButtonElement>('[data-correct-btn] button')!;
    expect(btn().disabled).toBe(true);
    type('[data-fix-reason]', 'Recorded by mistake');
    btn().click();
    await fixture.whenStable();
    const fix = http.expectOne(`${API}/api/StudentLeaving/correction`);
    expect(fix.request.body).toEqual({ studentId: 'S1', reason: 'Recorded by mistake', confirm: true });
    fix.flush(env({ studentId: 'S1', studentName: 'Stu 1', yearName: '2026-27', outcome: 'LEFT', leavingDate: null, canCorrect: true, blockers: [] }));
    http.expectOne(`${API}/api/StudentLeaving/leavers`).flush(env([]));
    // a refused one
    type('[data-fix-student]', 'S2');
    q<HTMLButtonElement>('[data-fix-check-btn] button')!.click();
    http.expectOne((r) => r.url === `${API}/api/StudentLeaving/correction/check`).flush(env({ studentId: 'S2', studentName: 'Stu 2', yearName: '2026-27', outcome: 'LEFT', leavingDate: null, canCorrect: false, blockers: ['A transfer certificate has been issued for this student: void it first.'] }));
    fixture.detectChanges();
    expect(q('[data-fix-blockers]')?.textContent).toContain('transfer certificate');
    expect(q('[data-correct-btn]')).toBeNull();
  });

  it('only the School Admin and the Principal open the leaving and section pages', () => {
    for (const path of ['/student-leaving', '/section-change']) {
      expect(canAccess(path, ['School Admin'])).toBe(true);
      expect(canAccess(path, ['Principal'])).toBe(true);
      for (const r of ['Teacher', 'Accountant', 'Admission Staff', 'Student', 'Guardian', 'Super Admin', 'Store Keeper']) expect(canAccess(path, [r])).toBe(false);
    }
  });
});
