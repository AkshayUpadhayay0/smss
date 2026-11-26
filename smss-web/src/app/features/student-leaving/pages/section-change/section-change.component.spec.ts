import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { ConfirmDialogService } from '../../../../core/services/confirm-dialog.service';
import { SectionChangeComponent } from './section-change.component';

const API = environment.apiUrl;
const env = <T>(data: T) => ({ status: true, statusCode: 200, message: 'ok', data });
const CHECK = (over: Record<string, unknown> = {}) => ({
  studentId: 'S1', studentName: 'Stu 1', admissionNumber: 'A1', className: 'Class 5', yearName: '2026-27', currentSectionId: 60, currentSectionName: 'A', currentRoll: '3', canChange: true, blockers: [],
  targets: [{ sectionId: 61, sectionName: 'B', maxStrength: 10, enrolled: 10, isFull: true, nextRoll: '11' }, { sectionId: 62, sectionName: 'C', maxStrength: null, enrolled: 4, isFull: false, nextRoll: '5' }], ...over,
});
const HIST = [{ changeId: 1, yearName: '2026-27', fromSection: 'B', toSection: 'A', fromRoll: '9', toRoll: '3', effectiveDate: '2026-10-01', reason: 'Moved back to A', changedBy: 'Admin', changedAt: '2026-10-01T10:00:00Z' }];

describe('SectionChangeComponent', () => {
  let fixture: ComponentFixture<SectionChangeComponent>;
  let http: HttpTestingController;
  const root = () => fixture.nativeElement as HTMLElement;
  const q = <T extends Element>(sel: string) => root().querySelector(sel) as T | null;
  const type = (sel: string, value: string) => { const el = q<HTMLInputElement>(sel)!; el.value = value; el.dispatchEvent(new Event('input')); fixture.detectChanges(); };

  function openAndCheck(check: unknown = CHECK()) {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: ConfirmDialogService, useValue: { confirm: () => Promise.resolve(true) } }] });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SectionChangeComponent);
    fixture.detectChanges();
    type('[data-student]', 'S1');
    q<HTMLButtonElement>('[data-check-btn] button')!.click();
    http.expectOne((r) => r.url === `${API}/api/SectionChanges/check` && r.params.get('studentId') === 'S1').flush(env(check));
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/api/SectionChanges/history`).flush(env(HIST));
    fixture.detectChanges();
  }
  afterEach(() => http.verify());

  it('shows the sections offered with their strength, marks a full one, and the history of earlier changes', () => {
    openAndCheck();
    const opts = [...root().querySelectorAll<HTMLOptionElement>('[data-target] option')];
    expect(opts.map((o) => o.textContent?.trim())).toEqual(['Choose…', 'Section B (10 of 10, full)', 'Section C (4)']);
    expect(opts[1].disabled).toBe(true);
    expect(q('[data-history]')?.textContent).toContain('Moved back to A');
  });

  it('moves the student with the chosen section, a typed roll, the date and the reason (ten characters)', async () => {
    openAndCheck();
    const sel = q<HTMLSelectElement>('[data-target]')!; sel.value = '62'; sel.dispatchEvent(new Event('change')); fixture.detectChanges();
    const btn = () => q<HTMLButtonElement>('[data-change-btn] button')!;
    expect(btn().disabled).toBe(true);
    type('[data-roll]', '21');
    type('[data-date]', '2026-11-20');
    type('[data-reason]', 'short');
    expect(btn().disabled).toBe(true);
    type('[data-reason]', 'Subject choice changed');
    btn().click();
    await fixture.whenStable();
    const post = http.expectOne((r) => r.method === 'POST' && r.url === `${API}/api/SectionChanges`);
    expect(post.request.body).toEqual({ studentId: 'S1', toSectionId: 62, reason: 'Subject choice changed', confirm: true, rollNumber: '21', effectiveDate: '2026-11-20' });
    post.flush(env({ studentId: 'S1', studentName: 'Stu 1', fromSection: 'A', toSection: 'C', fromRoll: '3', toRoll: '21', effectiveDate: '2026-11-20' }));
    http.expectOne((r) => r.url === `${API}/api/SectionChanges/check`).flush(env(CHECK({ currentSectionId: 62, currentSectionName: 'C' })));
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${API}/api/SectionChanges/history`).flush(env(HIST));
  });

  it('a student who cannot be moved shows the reason and no controls', () => {
    openAndCheck(CHECK({ canChange: false, blockers: ['Stu 1 already has a published report card in 2026-27.'] }));
    expect(q('[data-blockers]')?.textContent).toContain('published report card');
    expect(q('[data-change-btn]')).toBeNull();
  });
});
